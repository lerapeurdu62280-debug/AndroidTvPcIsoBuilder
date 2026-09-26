using System.Text;
using System.Text.Json;
using AndroidTvPcIsoBuilder.Application.Interfaces;
using AndroidTvPcIsoBuilder.Application.Services;
using AndroidTvPcIsoBuilder.Domain.Entities;
using AndroidTvPcIsoBuilder.Domain.Enums;
using DiscUtils.Iso9660;

namespace AndroidTvPcIsoBuilder.Infrastructure.Iso;

/// <summary>
/// Construit l'ISO de sortie à partir de l'image source : copie le contenu tel quel,
/// dépose les APK sélectionnés dans /APPS et la bootanimation dans /BOOTANIM, ajoute le
/// script de démarrage /scripts/atvbuilder qui les branche sur le système Android, et
/// préserve le catalogue de boot El Torito d'origine (BIOS et/ou UEFI).
/// </summary>
public class IsoBuilder : IIsoBuilder
{
    private const string AppsDirectory = "apps";
    private const string ManifestFileName = "manifest.json";

    /// <summary>
    /// L'init de l'initrd Android-x86/BlissOS source chaque fichier de /src/scripts/* (racine
    /// de l'ISO) après le montage du système et avant switch_root. DiscUtils écrit les noms en
    /// majuscules, que le pilote iso9660 de Linux présente en minuscules : "scripts/atvbuilder" côté Android.
    /// </summary>
    private const string BootScriptDirectory = "scripts";
    private const string BootScriptFileName = "atvbuilder";
    private const string BootScriptResourceName = "AndroidTvPcIsoBuilder.Infrastructure.Assets.Scripts.atvbuilder-boot.sh";

    /// <summary>
    /// Programme qui anime le logo sur le framebuffer avant Android (Assets/Splash/atvsplash.c),
    /// lancé par le script de démarrage depuis "bootanim/atvsplash" avec "bootanim/splash.atvs".
    /// </summary>
    private const string SplashProgramFileName = "atvsplash";
    private const string SplashImageFileName = "splash.atvs";
    private const string SplashProgramResourceName = "AndroidTvPcIsoBuilder.Infrastructure.Assets.Splash.atvsplash";

    private readonly IBootAnimationGenerator _bootAnimationGenerator;

    public IsoBuilder(IBootAnimationGenerator bootAnimationGenerator)
    {
        _bootAnimationGenerator = bootAnimationGenerator;
    }

    public async Task BuildAsync(
        AndroidTvProject project,
        string sourceIsoPath,
        IProgress<BuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new BuildProgress("Lecture de l'image source", 5));

        await using var sourceStream = File.OpenRead(sourceIsoPath);
        var reader = new CDReader(sourceStream, joliet: true);

        var preserver = new BootCatalogPreserver();
        var bootCatalog = preserver.ReadBootCatalog(sourceStream, reader);

        // Le nom de volume d'origine est conservé (voir VolumeIdentityPreserver) : le système
        // Android peut s'en servir pour retrouver son support. Le nom du projet ne sert que si
        // la source n'en a pas.
        var sourceIdentity = VolumeIdentityPreserver.Read(sourceStream);

        progress?.Report(new BuildProgress("Copie du contenu de l'image", 20));

        var builder = new CDBuilder
        {
            UseJoliet = true,
            VolumeIdentifier = SanitizeVolumeIdentifier(project.Name)
        };

        // Image de boot provisoire : elle fait réserver par CDBuilder le descripteur "Boot Record"
        // que BootCatalogPreserver remplace ensuite par celui de la source. Sans elle, il n'y a
        // aucun emplacement libre parmi les descripteurs de volume.
        if (bootCatalog is not null)
            builder.SetBootImage(new MemoryStream(new byte[BootCatalogPreserver.SectorSize]), BootDeviceEmulation.NoEmulation, 0);

        // Avec le logo animé, le menu de démarrage d'origine disparaît : l'ISO démarre
        // directement, sans texte, sur la première entrée du menu.
        var replacedFiles = project.BootAnimation.Enabled
            ? BuildSilentBootReplacements(reader)
            : new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        var openStreams = new List<Stream>();
        try
        {
            CopyDirectoryRecursive(reader.Root, builder, openStreams, replacedFiles, cancellationToken);

            progress?.Report(new BuildProgress("Injection des applications", 60));

            AddAppsToImage(builder, project, openStreams);

            progress?.Report(new BuildProgress("Injection de l'animation de démarrage", 70));

            var bootAnimationAdded = await AddBootAnimationToImageAsync(builder, project, openStreams, _bootAnimationGenerator, cancellationToken);

            if (project.Apps.Count > 0 || bootAnimationAdded)
            {
                progress?.Report(new BuildProgress("Ajout du script de démarrage", 76));
                AddBootScriptToImage(builder);
            }

            progress?.Report(new BuildProgress("Génération de l'image ISO", 80));

            using var builtStream = builder.Build();

            Directory.CreateDirectory(Path.GetDirectoryName(project.OutputIsoPath)!);
            await using (var outputStream = File.Create(project.OutputIsoPath))
            {
                builtStream.Seek(0, SeekOrigin.Begin);
                await builtStream.CopyToAsync(outputStream, cancellationToken);
            }
        }
        finally
        {
            foreach (var stream in openStreams)
                stream.Dispose();
        }

        if (bootCatalog is not null)
        {
            progress?.Report(new BuildProgress("Restauration du catalogue de boot", 95));

            var platformsToKeep = GetPlatformsForBootMode(project.BootMode, bootCatalog);

            await using var outputStream = File.Open(project.OutputIsoPath, FileMode.Open, FileAccess.ReadWrite);
            preserver.ApplyBootCatalog(outputStream, bootCatalog, platformsToKeep);
        }

        await using (var outputStream = File.Open(project.OutputIsoPath, FileMode.Open, FileAccess.ReadWrite))
        {
            JolietFileNameFixer.Fix(outputStream);
            if (sourceIdentity is { Label.Length: > 0 })
                VolumeIdentityPreserver.Apply(outputStream, sourceIdentity);
        }
    }

    public BuildVerification Verify(AndroidTvProject project)
    {
        var issues = new List<string>();

        if (!File.Exists(project.OutputIsoPath))
            return new BuildVerification(false, false, 0, project.Apps.Count, 0, new[] { "Le fichier ISO de sortie est introuvable." });

        var actualSize = new FileInfo(project.OutputIsoPath).Length;

        bool isReadable;
        bool hasBootImage;
        var appsFound = 0;

        try
        {
            using var stream = File.OpenRead(project.OutputIsoPath);
            var reader = new CDReader(stream, joliet: true);
            isReadable = true;

            var preserver = new BootCatalogPreserver();
            hasBootImage = preserver.ReadBootCatalog(stream) is { BootImages.Count: > 0 };
            if (!hasBootImage)
                issues.Add("Aucune image de boot n'a été retrouvée dans l'ISO générée.");

            if (project.Apps.Count > 0 && !reader.FileExists($"{BootScriptDirectory}\\{BootScriptFileName}"))
                issues.Add("Le script de démarrage qui installe les applications est absent de l'ISO générée.");

            foreach (var app in project.Apps)
            {
                var apkFileName = GetApkImageFileName(app.SourceApkPath);
                if (reader.FileExists($"{AppsDirectory}\\{apkFileName}"))
                    appsFound++;
                else
                    issues.Add($"L'application '{app.Name}' ({apkFileName}) est absente de l'ISO générée.");
            }
        }
        catch (Exception ex)
        {
            isReadable = false;
            hasBootImage = false;
            issues.Add($"L'ISO générée n'a pas pu être relue : {ex.Message}");
        }

        return new BuildVerification(isReadable, hasBootImage, appsFound, project.Apps.Count, actualSize, issues);
    }

    /// <summary>
    /// Détermine quelles plateformes de boot conserver selon le mode choisi par l'utilisateur.
    /// On ne peut que filtrer les entrées présentes dans la source : si celle-ci ne contient pas
    /// d'entrée UEFI, choisir "Uefi" ou "Hybrid" ne peut pas en faire apparaître une par magie —
    /// dans ce cas on conserve tout ce qui est disponible plutôt que de produire une image sans
    /// aucun boot valide.
    /// </summary>
    private static HashSet<BootPlatformId>? GetPlatformsForBootMode(BootMode bootMode, BootCatalogInfo bootCatalog)
    {
        var availablePlatforms = bootCatalog.BootImages.Select(i => i.PlatformId).ToHashSet();

        var desired = bootMode switch
        {
            BootMode.Bios => new HashSet<BootPlatformId> { BootPlatformId.X86 },
            BootMode.Uefi => new HashSet<BootPlatformId> { BootPlatformId.Uefi },
            _ => availablePlatforms
        };

        var intersection = desired.Intersect(availablePlatforms).ToHashSet();
        return intersection.Count > 0 ? intersection : null;
    }

    /// <param name="replacedFiles">Fichiers dont le contenu est remplacé, par chemin dans l'image.</param>
    private static void CopyDirectoryRecursive(
        DiscUtils.DiscDirectoryInfo sourceDir,
        CDBuilder builder,
        List<Stream> openStreams,
        IReadOnlyDictionary<string, byte[]> replacedFiles,
        CancellationToken cancellationToken)
    {
        foreach (var file in sourceDir.GetFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var imagePath = NormalizeIso9660Name(file.FullName.TrimStart('\\'));
            if (replacedFiles.TryGetValue(imagePath, out var replacement))
            {
                builder.AddFile(imagePath, replacement);
                continue;
            }

            var fileStream = file.OpenRead();
            openStreams.Add(fileStream);
            builder.AddFile(imagePath, fileStream);
        }

        foreach (var subDir in sourceDir.GetDirectories())
        {
            cancellationToken.ThrowIfCancellationRequested();

            builder.AddDirectory(subDir.FullName.TrimStart('\\'));
            CopyDirectoryRecursive(subDir, builder, openStreams, replacedFiles, cancellationToken);
        }
    }

    /// <summary>
    /// Nouvelles configurations de démarrage (voir <see cref="SilentBootConfig"/>) : chaque
    /// isolinux.cfg / syslinux.cfg qui démarre un noyau (BIOS) et /boot/grub/grub.cfg (UEFI).
    /// L'entrée retenue est celle du menu ISOLINUX d'origine, sinon celle de grub.cfg quand
    /// l'ISO n'a pas d'ISOLINUX. Sans entrée reconnue, rien n'est modifié : mieux vaut garder
    /// le menu d'origine qu'une ISO qui ne démarre plus.
    /// </summary>
    private static Dictionary<string, byte[]> BuildSilentBootReplacements(CDReader reader)
    {
        var replacements = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var utf8 = new UTF8Encoding(false);

        BootEntry? entry = null;
        foreach (var file in EnumerateFiles(reader.Root))
        {
            var fileName = Path.GetFileName(NormalizeIso9660Name(file.FullName));
            if (!fileName.Equals("isolinux.cfg", StringComparison.OrdinalIgnoreCase)
                && !fileName.Equals("syslinux.cfg", StringComparison.OrdinalIgnoreCase))
                continue;

            string content;
            using (var stream = file.OpenRead())
            using (var streamReader = new StreamReader(stream, Encoding.UTF8))
                content = streamReader.ReadToEnd();

            var fileEntry = SilentBootConfig.FindDefaultIsolinuxEntry(content);
            if (fileEntry is null)
                continue; // Fichier qui ne fait que charger une autre configuration.

            entry ??= fileEntry;
            replacements[NormalizeIso9660Name(file.FullName.TrimStart('\\'))] =
                utf8.GetBytes(SilentBootConfig.BuildIsolinuxConfig(fileEntry));
        }

        const string grubConfigPath = "boot\\grub\\grub.cfg";
        if (!reader.FileExists(grubConfigPath))
            return replacements;

        // ISO démarrée par GRUB seul (LineageOS TV x86, BlissOS) : l'entrée vient de grub.cfg.
        if (entry is null)
        {
            using var stream = reader.OpenFile(grubConfigPath, FileMode.Open, FileAccess.Read);
            using var streamReader = new StreamReader(stream, Encoding.UTF8);
            entry = SilentBootConfig.FindDefaultGrubEntry(streamReader.ReadToEnd());
        }

        if (entry is not null)
            replacements[grubConfigPath] = utf8.GetBytes(SilentBootConfig.BuildGrubConfig(entry));

        return replacements;
    }

    private static IEnumerable<DiscUtils.DiscFileInfo> EnumerateFiles(DiscUtils.DiscDirectoryInfo directory)
    {
        foreach (var file in directory.GetFiles())
            yield return file;
        foreach (var subDirectory in directory.GetDirectories())
            foreach (var file in EnumerateFiles(subDirectory))
                yield return file;
    }

    /// <summary>
    /// Les noms de fichiers ISO9660 retournés par DiscUtils portent le suffixe de version
    /// (ex: "KERNEL.;1"), voire un point final orphelin sans extension (ex: "KERNEL.") quand
    /// aucune version n'est présente. CDBuilder.AddFile attend un nom sans ce suffixe.
    /// </summary>
    private static string NormalizeIso9660Name(string name)
    {
        var versionIndex = name.IndexOf(';');
        if (versionIndex >= 0)
            name = name[..versionIndex];

        return name.EndsWith('.') ? name[..^1] : name;
    }

    private static void AddAppsToImage(CDBuilder builder, AndroidTvProject project, List<Stream> openStreams)
    {
        if (project.Apps.Count == 0)
            return;

        builder.AddDirectory(AppsDirectory);

        var manifestApps = new List<object>();
        foreach (var app in project.Apps)
        {
            var apkFileName = GetApkImageFileName(app.SourceApkPath);
            var apkStream = File.OpenRead(app.SourceApkPath);
            openStreams.Add(apkStream);
            builder.AddFile($"{AppsDirectory}\\{apkFileName}", apkStream);

            manifestApps.Add(new
            {
                name = app.Name,
                fileName = apkFileName,
                preinstalled = app.IsPreinstalled
            });
        }

        var manifestJson = JsonSerializer.Serialize(new { apps = manifestApps }, new JsonSerializerOptions { WriteIndented = true });
        var manifestBytes = System.Text.Encoding.UTF8.GetBytes(manifestJson);
        builder.AddFile($"{AppsDirectory}\\{ManifestFileName}", manifestBytes);
    }

    /// <summary>
    /// Nom de l'APK dans /APPS. Le script de démarrage copie ces fichiers vers
    /// /system/etc/user_app, que init.sh parcourt avec "for apk in $USER_APPS" : un espace
    /// dans le nom couperait le chemin en deux. On ne garde donc que [A-Za-z0-9._-], une
    /// extension ".apk" en minuscules (le script copie "*.apk") et une longueur compatible
    /// avec la limite Joliet de 64 caractères.
    /// </summary>
    public static string GetApkImageFileName(string sourceApkPath)
    {
        var baseName = Path.GetFileNameWithoutExtension(sourceApkPath)
            .Normalize(NormalizationForm.FormD);

        var cleaned = new StringBuilder();
        foreach (var c in baseName)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark)
                continue;
            cleaned.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_');
        }

        var name = cleaned.ToString().Trim('_', '.');
        if (name.Length == 0)
            name = "app";
        if (name.Length > 56)
            name = name[..56];

        return $"{name}.apk";
    }

    /// <summary>
    /// Génère la bootanimation personnalisée (si activée) et la dépose sous
    /// /BOOTANIM/bootanimation.zip. Elle n'est pas écrite directement dans le système :
    /// system.img est une image ext4 imbriquée dans system.sfs (squashfs), en lecture seule.
    /// C'est le script de démarrage (voir <see cref="AddBootScriptToImage"/>) qui la monte
    /// par-dessus l'animation d'origine (system/product/media/bootanimation.zip sur les bases
    /// Google TV / LineageOS TV, system/media/bootanimation.zip sur d'autres).
    /// </summary>
    /// <returns><c>true</c> si l'animation a bien été ajoutée à l'image.</returns>
    private static async Task<bool> AddBootAnimationToImageAsync(
        CDBuilder builder,
        AndroidTvProject project,
        List<Stream> openStreams,
        IBootAnimationGenerator bootAnimationGenerator,
        CancellationToken cancellationToken)
    {
        if (!project.BootAnimation.Enabled)
            return false;

        const string bootAnimationDirectory = "bootanim";
        const string bootAnimationFileName = "bootanimation.zip";

        var stagingDirectory = Path.Combine(Path.GetTempPath(), "AndroidTvPcIsoBuilder", "BootAnimation");
        var generateResult = await bootAnimationGenerator.GenerateAsync(project.BootAnimation, stagingDirectory, cancellationToken);
        if (!generateResult.IsSuccess)
            return false; // Best-effort : une bootanimation manquée ne doit pas faire échouer tout le build.

        builder.AddDirectory(bootAnimationDirectory);
        var zipStream = File.OpenRead(generateResult.Value);
        openStreams.Add(zipStream);
        builder.AddFile($"{bootAnimationDirectory}\\{bootAnimationFileName}", zipStream);

        // Logo animé dès le début du démarrage (avant Android) : programme atvsplash + son image.
        var splashResult = await bootAnimationGenerator.GenerateSplashImageAsync(project.BootAnimation, stagingDirectory, cancellationToken);
        if (splashResult.IsSuccess)
        {
            var splashStream = File.OpenRead(splashResult.Value);
            openStreams.Add(splashStream);
            builder.AddFile($"{bootAnimationDirectory}\\{SplashImageFileName}", splashStream);
            builder.AddFile($"{bootAnimationDirectory}\\{SplashProgramFileName}", ReadEmbeddedResource(SplashProgramResourceName));
        }

        return true;
    }

    private static byte[] ReadEmbeddedResource(string resourceName)
    {
        using var resourceStream = typeof(IsoBuilder).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Ressource embarquée introuvable : {resourceName}");
        using var memory = new MemoryStream();
        resourceStream.CopyTo(memory);
        return memory.ToArray();
    }

    /// <summary>
    /// Ajoute /scripts/atvbuilder, sourcé par l'init de l'initrd au démarrage : il monte la
    /// bootanimation et les APK de l'ISO sur le système Android (voir le script lui-même,
    /// Assets/Scripts/atvbuilder-boot.sh). Les fins de ligne sont forcées en LF, un retour chariot
    /// cassant l'interprétation du script par le shell busybox.
    /// </summary>
    private static void AddBootScriptToImage(CDBuilder builder)
    {
        using var resourceStream = typeof(IsoBuilder).Assembly.GetManifestResourceStream(BootScriptResourceName)
            ?? throw new InvalidOperationException($"Ressource embarquée introuvable : {BootScriptResourceName}");
        using var resourceReader = new StreamReader(resourceStream, Encoding.UTF8);
        var script = resourceReader.ReadToEnd().Replace("\r\n", "\n");

        builder.AddDirectory(BootScriptDirectory);
        builder.AddFile($"{BootScriptDirectory}\\{BootScriptFileName}", new UTF8Encoding(false).GetBytes(script));
    }

    /// <summary>
    /// Le champ Volume Identifier d'ISO9660 n'accepte que des lettres ASCII, chiffres et '_'
    /// (norme "d-characters"). On translittère d'abord les lettres accentuées (é→E, à→A, ...)
    /// via une décomposition Unicode avant de filtrer, pour éviter que ces caractères ne soient
    /// simplement remplacés par '_' ou mal encodés.
    /// </summary>
    private static string SanitizeVolumeIdentifier(string name)
    {
        var decomposed = name.ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var withoutDiacritics = new string(decomposed
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray());

        var sanitized = new string(withoutDiacritics
            .Select(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' ? c : '_')
            .ToArray());

        return sanitized.Length > 32 ? sanitized[..32] : sanitized;
    }
}

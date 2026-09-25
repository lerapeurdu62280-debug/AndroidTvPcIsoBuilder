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
/// dépose les APK sélectionnés dans /apps (installés par un script au premier démarrage,
/// voir manifest.json), et préserve le catalogue de boot El Torito d'origine (BIOS et/ou UEFI).
/// </summary>
public class IsoBuilder : IIsoBuilder
{
    private const string AppsDirectory = "APPS";
    private const string DriversDirectory = "DRIVERS";
    private const string ManifestFileName = "MANIFEST.JSON";

    private readonly IBootAnimationGenerator _bootAnimationGenerator;

    public IsoBuilder(IBootAnimationGenerator bootAnimationGenerator)
    {
        _bootAnimationGenerator = bootAnimationGenerator;
    }

    public async Task BuildAsync(
        AndroidTvProject project,
        string sourceIsoPath,
        IReadOnlyList<string>? resolvedDriverFilePaths = null,
        IProgress<BuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new BuildProgress("Lecture de l'image source", 5));

        await using var sourceStream = File.OpenRead(sourceIsoPath);
        var reader = new CDReader(sourceStream, joliet: true);

        var preserver = new BootCatalogPreserver();
        var bootCatalog = preserver.ReadBootCatalog(sourceStream, reader);

        progress?.Report(new BuildProgress("Copie du contenu de l'image", 20));

        var builder = new CDBuilder
        {
            UseJoliet = true,
            VolumeIdentifier = SanitizeVolumeIdentifier(project.Name)
        };

        var openStreams = new List<Stream>();
        try
        {
            CopyDirectoryRecursive(reader.Root, builder, openStreams, cancellationToken);

            progress?.Report(new BuildProgress("Injection des applications", 60));

            AddAppsToImage(builder, project, openStreams);

            progress?.Report(new BuildProgress("Injection des pilotes Wi-Fi/BT", 68));

            AddDriversToImage(builder, resolvedDriverFilePaths, openStreams);

            progress?.Report(new BuildProgress("Injection de l'animation de démarrage", 74));

            await AddBootAnimationToImageAsync(builder, project, openStreams, _bootAnimationGenerator, cancellationToken);

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

            foreach (var app in project.Apps)
            {
                var apkFileName = Path.GetFileName(app.SourceApkPath);
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

    private static void CopyDirectoryRecursive(DiscUtils.DiscDirectoryInfo sourceDir, CDBuilder builder, List<Stream> openStreams, CancellationToken cancellationToken)
    {
        foreach (var file in sourceDir.GetFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileStream = file.OpenRead();
            openStreams.Add(fileStream);
            builder.AddFile(NormalizeIso9660Name(file.FullName.TrimStart('\\')), fileStream);
        }

        foreach (var subDir in sourceDir.GetDirectories())
        {
            cancellationToken.ThrowIfCancellationRequested();

            builder.AddDirectory(subDir.FullName.TrimStart('\\'));
            CopyDirectoryRecursive(subDir, builder, openStreams, cancellationToken);
        }
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
            var apkFileName = Path.GetFileName(app.SourceApkPath);
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
    /// Dépose les fichiers pilotes/script first-boot déjà résolus localement (voir
    /// IDriverPackInjector.ResolveDriverFilesAsync) dans /DRIVERS, sur le même modèle
    /// que AddAppsToImage : aucun accès réseau ici, juste la copie de fichiers déjà
    /// téléchargés/générés dans un répertoire de staging.
    /// </summary>
    private static void AddDriversToImage(CDBuilder builder, IReadOnlyList<string>? resolvedDriverFilePaths, List<Stream> openStreams)
    {
        if (resolvedDriverFilePaths is null || resolvedDriverFilePaths.Count == 0)
            return;

        builder.AddDirectory(DriversDirectory);

        var manifestFiles = new List<object>();
        foreach (var filePath in resolvedDriverFilePaths)
        {
            var fileName = Path.GetFileName(filePath);
            var fileStream = File.OpenRead(filePath);
            openStreams.Add(fileStream);
            builder.AddFile($"{DriversDirectory}\\{fileName}", fileStream);

            manifestFiles.Add(new { fileName });
        }

        var manifestJson = JsonSerializer.Serialize(new { files = manifestFiles }, new JsonSerializerOptions { WriteIndented = true });
        var manifestBytes = System.Text.Encoding.UTF8.GetBytes(manifestJson);
        builder.AddFile($"{DriversDirectory}\\{ManifestFileName}", manifestBytes);
    }

    /// <summary>
    /// Génère la bootanimation personnalisée (si activée) et la dépose dans l'image.
    ///
    /// EMPLACEMENT PROVISOIRE : dépose à la racine sous /BOOTANIM/bootanimation.zip plutôt
    /// que l'emplacement standard Android "system/media/bootanimation.zip", qui n'est pas
    /// garanti directement accessible depuis la racine ISO9660 (system.img est souvent une
    /// image disque imbriquée sur les distributions Android x86, pas un dossier à plat).
    /// L'emplacement final réel doit être validé empiriquement sur une vraie image du
    /// catalogue avant de considérer cette étape comme fonctionnellement complète — voir
    /// le plan de la Phase 2. En l'état, ce dépôt seul ne remplace PAS l'animation de
    /// démarrage par défaut tant que ce point n'est pas tranché.
    /// </summary>
    private static async Task AddBootAnimationToImageAsync(
        CDBuilder builder,
        AndroidTvProject project,
        List<Stream> openStreams,
        IBootAnimationGenerator bootAnimationGenerator,
        CancellationToken cancellationToken)
    {
        if (!project.BootAnimation.Enabled)
            return;

        const string bootAnimationDirectory = "BOOTANIM";
        const string bootAnimationFileName = "bootanimation.zip";

        var stagingDirectory = Path.Combine(Path.GetTempPath(), "AndroidTvPcIsoBuilder", "BootAnimation");
        var generateResult = await bootAnimationGenerator.GenerateAsync(project.BootAnimation, stagingDirectory, cancellationToken);
        if (!generateResult.IsSuccess)
            return; // Best-effort : une bootanimation manquée ne doit pas faire échouer tout le build.

        builder.AddDirectory(bootAnimationDirectory);
        var zipStream = File.OpenRead(generateResult.Value);
        openStreams.Add(zipStream);
        builder.AddFile($"{bootAnimationDirectory}\\{bootAnimationFileName}", zipStream);
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

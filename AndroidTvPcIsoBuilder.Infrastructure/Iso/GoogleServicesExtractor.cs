using System.Text.RegularExpressions;
using AndroidTvPcIsoBuilder.Application.Common;
using AndroidTvPcIsoBuilder.Application.Interfaces;
using AndroidTvPcIsoBuilder.Domain.Entities;
using DiscUtils.Iso9660;
using DiscUtils.SquashFs;

namespace AndroidTvPcIsoBuilder.Infrastructure.Iso;

/// <summary>
/// Extrait d'une ISO Android TV x86 « donneuse » (system.sfs → system.img ext4, ou system.img
/// directement) les seuls composants Google nécessaires aux services Google TV. Liste fermée :
/// les ISO communautaires embarquent aussi des outils tiers ou des applications Google
/// modifiées (signature cassée) qu'on ne veut surtout pas propager.
/// </summary>
public class GoogleServicesExtractor : IGoogleServicesExtractor
{
    /// <summary>Dossiers d'applications (noms de dossier) repris de la donneuse.</summary>
    private static readonly Regex AppDirectoryPattern = new(
        @"^(PrebuiltGmsCore\w*|GmsCore|GoogleServicesFramework|Katniss\w*|GoogleTTS|Phonesky\w*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Fichiers de configuration repris, par dossier (permissions privilégiées, etc.).</summary>
    private static readonly (string Directory, Regex Pattern)[] ConfigFilePatterns =
    {
        ("permissions", new Regex(@"^(privapp-permissions-(google|atv)[\w.-]*|split-permissions-google[\w.-]*|com\.google\.android\.tv[\w.-]*)\.xml$", RegexOptions.IgnoreCase)),
        ("sysconfig", new Regex(@"^(google|google-hiddenapi-package-whitelist|google-staged-installer-whitelist)\.xml$", RegexOptions.IgnoreCase)),
        ("default-permissions", new Regex(@"^google-default-permissions[\w.-]*\.xml$", RegexOptions.IgnoreCase)),
    };

    /// <summary>Partitions (relatives à la racine du système) où chercher applications et configuration.</summary>
    private static readonly string[] Partitions = { "", "product", "system_ext" };

    private const string PlayStoreRelativePath = "product\\priv-app\\Phonesky\\Phonesky.apk";

    public async Task<Result<string>> ExtractAsync(GoogleServicesConfig config, string outputDirectory, CancellationToken cancellationToken = default)
    {
        var hasDonor = !string.IsNullOrWhiteSpace(config.DonorIsoPath);
        var hasPlayStore = !string.IsNullOrWhiteSpace(config.PlayStoreApkPath);
        if (!hasDonor && !hasPlayStore)
            return Result<string>.Failure("Aucune ISO donneuse ni APK Play Store indiqué pour les services Google TV.");

        if (Directory.Exists(outputDirectory))
            Directory.Delete(outputDirectory, recursive: true);
        Directory.CreateDirectory(outputDirectory);

        if (hasDonor)
        {
            if (!File.Exists(config.DonorIsoPath))
                return Result<string>.Failure($"ISO donneuse introuvable : {config.DonorIsoPath}");

            var donorResult = await Task.Run(() => ExtractFromDonor(config.DonorIsoPath!, outputDirectory, cancellationToken), cancellationToken);
            if (!donorResult.IsSuccess)
                return Result<string>.Failure(donorResult.Errors);
        }

        if (hasPlayStore)
        {
            if (!File.Exists(config.PlayStoreApkPath))
                return Result<string>.Failure($"APK Play Store introuvable : {config.PlayStoreApkPath}");
            if (!IsZipFile(config.PlayStoreApkPath!))
                return Result<string>.Failure($"Le fichier Play Store n'est pas un APK valide : {config.PlayStoreApkPath}");

            // Un Phonesky de la donneuse est remplacé par celui fourni, plus récent a priori.
            foreach (var existing in Directory.GetDirectories(outputDirectory, "Phonesky*", SearchOption.AllDirectories))
                Directory.Delete(existing, recursive: true);

            var target = Path.Combine(outputDirectory, PlayStoreRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(config.PlayStoreApkPath!, target, overwrite: true);
        }

        return Result<string>.Success(outputDirectory);
    }

    private static Result ExtractFromDonor(string donorIsoPath, string outputDirectory, CancellationToken cancellationToken)
    {
        using var isoStream = File.OpenRead(donorIsoPath);
        var iso = new CDReader(isoStream, joliet: true);

        var disposables = new List<IDisposable>();
        try
        {
            IReadOnlyFileTree? system = null;
            var sfs = FindRootFile(iso, "system.sfs");
            var img = FindRootFile(iso, "system.img");
            if (sfs is not null)
            {
                var sfsStream = iso.OpenFile(sfs, FileMode.Open);
                disposables.Add(sfsStream);

                // system.img dépasse souvent 4 Go : DiscUtils ne lit pas ce type d'inode.
                var imgStream = SquashFsLargeFileReader.OpenRootFile(sfsStream, "system.img");
                if (imgStream is not null)
                {
                    disposables.Add(imgStream);
                    system = new Ext4Reader(imgStream);
                }
                else
                {
                    var squash = new SquashFileSystemReader(sfsStream); // squashfs contenant directement l'arborescence du système
                    disposables.Add(squash);
                    system = new DiscFileTree(squash);
                }
            }
            else if (img is not null)
            {
                var imgStream = iso.OpenFile(img, FileMode.Open);
                disposables.Add(imgStream);
                system = new Ext4Reader(imgStream);
            }

            if (system is null)
            {
                return FindRootFile(iso, "system.efs") is not null
                    ? Result.Failure("L'ISO donneuse utilise un système EROFS (system.efs), qui ne peut pas être lu. Choisissez une ISO avec system.sfs ou system.img (ex. Google TV x86).")
                    : Result.Failure("L'ISO donneuse ne contient ni system.sfs ni system.img.");
            }

            // Système « system-as-root » : l'arborescence Android est sous \system.
            var root = system.FileExists("system\\build.prop") ? "system" : "";

            var gmsFound = false;
            foreach (var partition in Partitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var partitionPath = Combine(root, partition);

                foreach (var appRoot in new[] { "priv-app", "app" })
                {
                    var appRootPath = Combine(partitionPath, appRoot);
                    if (!system.DirectoryExists(appRootPath))
                        continue;

                    foreach (var appDir in system.GetDirectories(appRootPath))
                    {
                        var name = Path.GetFileName(appDir);
                        if (!AppDirectoryPattern.IsMatch(name))
                            continue;
                        gmsFound |= name.Contains("GmsCore", StringComparison.OrdinalIgnoreCase);
                        CopyDirectory(system, appDir, Path.Combine(outputDirectory, partition, appRoot, name), cancellationToken);
                    }
                }

                foreach (var (directory, pattern) in ConfigFilePatterns)
                {
                    var configPath = Combine(Combine(partitionPath, "etc"), directory);
                    if (!system.DirectoryExists(configPath))
                        continue;

                    foreach (var file in system.GetFiles(configPath).Where(f => pattern.IsMatch(Path.GetFileName(f))))
                        CopyFile(system, file, Path.Combine(outputDirectory, partition, "etc", directory, Path.GetFileName(file)));
                }
            }

            return gmsFound
                ? Result.Success()
                : Result.Failure("Les services Google (GmsCore) sont introuvables dans l'ISO donneuse.");
        }
        finally
        {
            for (var i = disposables.Count - 1; i >= 0; i--)
                disposables[i].Dispose();
        }
    }

    /// <summary>
    /// Copie un dossier d'application sans son sous-dossier "oat" : le code précompilé dépend
    /// du framework de la donneuse, Android le régénère pour la base cible.
    /// </summary>
    private static void CopyDirectory(IReadOnlyFileTree system, string sourceDir, string targetDir, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in system.GetFiles(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            CopyFile(system, file, Path.Combine(targetDir, Path.GetFileName(file)));
        }

        foreach (var subDir in system.GetDirectories(sourceDir))
        {
            var name = Path.GetFileName(subDir);
            if (!name.Equals("oat", StringComparison.OrdinalIgnoreCase))
                CopyDirectory(system, subDir, Path.Combine(targetDir, name), cancellationToken);
        }
    }

    private static void CopyFile(IReadOnlyFileTree system, string sourcePath, string targetPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        using var source = system.OpenFile(sourcePath);
        using var target = File.Create(targetPath);
        source.CopyTo(target);
    }

    private static string? FindRootFile(CDReader iso, string name)
        => iso.GetFiles("\\").FirstOrDefault(f => NormalizeIsoName(f).Equals(name, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeIsoName(string path)
    {
        var name = Path.GetFileName(path);
        var version = name.IndexOf(';');
        return (version >= 0 ? name[..version] : name).TrimEnd('.');
    }

    private static string Combine(string left, string right)
        => left.Length == 0 ? right : right.Length == 0 ? left : $"{left}\\{right}";

    private static bool IsZipFile(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[4];
        return stream.Read(header) == 4 && header[0] == (byte)'P' && header[1] == (byte)'K' && header[2] == 3 && header[3] == 4;
    }
}

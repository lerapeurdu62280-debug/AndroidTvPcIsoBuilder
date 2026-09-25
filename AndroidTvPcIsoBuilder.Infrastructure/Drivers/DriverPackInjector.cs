using AndroidTvPcIsoBuilder.Application.Common;
using AndroidTvPcIsoBuilder.Application.Interfaces;
using AndroidTvPcIsoBuilder.Application.Services;
using AndroidTvPcIsoBuilder.Domain.Entities;
using AndroidTvPcIsoBuilder.Domain.Enums;

namespace AndroidTvPcIsoBuilder.Infrastructure.Drivers;

/// <summary>
/// Télécharge le pack de pilotes Wi-Fi/Bluetooth sélectionné et génère le script de
/// détection automatique au premier démarrage dans un répertoire de staging local. Ne
/// manipule aucune arborescence de sortie compilée : les fichiers résolus sont ensuite
/// injectés dans l'image ISO finale par <see cref="IIsoBuilder"/>, au même moment et de
/// la même façon que les APK (voir IsoBuilder.AddDriversToImage).
/// </summary>
public class DriverPackInjector : IDriverPackInjector
{
    private const string FirstBootScriptFileName = "first-boot-detect.sh";

    private readonly IDriverCatalogService _driverCatalogService;
    private readonly IFileDownloader _fileDownloader;
    private readonly FirstBootScriptGenerator _firstBootScriptGenerator;

    public DriverPackInjector(
        IDriverCatalogService driverCatalogService,
        IFileDownloader fileDownloader,
        FirstBootScriptGenerator firstBootScriptGenerator)
    {
        _driverCatalogService = driverCatalogService;
        _fileDownloader = fileDownloader;
        _firstBootScriptGenerator = firstBootScriptGenerator;
    }

    public async Task<Result<IReadOnlyList<string>>> ResolveDriverFilesAsync(
        AndroidTvProject project,
        IProgress<IsoAssemblyProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var selection = project.DriverSelection;
        var resolvedFilePaths = new List<string>();

        if (!selection.EmbeddedDriverPackEnabled && !selection.AutoDetectFirstBootEnabled)
            return Result<IReadOnlyList<string>>.Success(resolvedFilePaths);

        var warnings = new List<string>();

        if (selection.EmbeddedDriverPackEnabled)
        {
            var downloadResult = await DownloadEmbeddedDriverPackAsync(project, warnings, progress, cancellationToken);
            if (!downloadResult.IsSuccess)
                return Result<IReadOnlyList<string>>.Failure(downloadResult.Errors);

            resolvedFilePaths.AddRange(downloadResult.Value);
        }

        if (selection.AutoDetectFirstBootEnabled)
        {
            var scriptResult = WriteFirstBootScript(project, progress);
            if (!scriptResult.IsSuccess)
                return Result<IReadOnlyList<string>>.Failure(scriptResult.Errors);

            resolvedFilePaths.Add(scriptResult.Value);
        }

        if (selection.EmbeddedDriverPackEnabled
            && resolvedFilePaths.Count == 0
            && selection.SelectedChipsetVendorIds.Count > 0
            && !selection.AutoDetectFirstBootEnabled)
        {
            // Aucun pilote n'a pu être résolu du tout, et la détection automatique n'est pas
            // activée comme filet de sécurité : on le signale clairement, sans faire échouer
            // le build pour autant (best-effort, voir contrat de l'interface).
            warnings.Add(
                "Aucun pilote n'a pu être résolu pour les vendors sélectionnés et la source ISO ciblée. " +
                "La détection automatique au premier démarrage étant désactivée, aucune couverture Wi-Fi/Bluetooth " +
                "supplémentaire n'a été ajoutée à l'image.");
        }

        if (warnings.Count > 0)
        {
            progress?.Report(new IsoAssemblyProgress(
                BuildMilestone.DriverInjection,
                100,
                "Avertissement(s) pilotes : " + string.Join(" | ", warnings)));
        }

        return Result<IReadOnlyList<string>>.Success(resolvedFilePaths);
    }

    private async Task<Result<IReadOnlyList<string>>> DownloadEmbeddedDriverPackAsync(
        AndroidTvProject project,
        List<string> warnings,
        IProgress<IsoAssemblyProgress>? progress,
        CancellationToken cancellationToken)
    {
        var isoSourceId = project.SourceIso.SourceId;
        var availableDrivers = _driverCatalogService.GetAvailableDrivers();
        var downloadedFilePaths = new List<string>();

        var stagingDirectory = Path.Combine(Path.GetTempPath(), "AndroidTvPcIsoBuilder", "Drivers");
        Directory.CreateDirectory(stagingDirectory);

        foreach (var vendorId in project.DriverSelection.SelectedChipsetVendorIds)
        {
            var entry = availableDrivers.FirstOrDefault(d =>
                string.Equals(d.VendorId, vendorId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(d.SupportedIsoSourceId, isoSourceId, StringComparison.OrdinalIgnoreCase));

            if (entry is null)
            {
                warnings.Add(
                    $"Aucun pilote disponible dans le catalogue pour le vendor '{vendorId}' et la source ISO '{isoSourceId}'. " +
                    "Ce chipset ne sera pas couvert par le pack embarqué (la détection automatique au premier démarrage, si activée, peut compenser partiellement).");
                continue;
            }

            var destinationPath = Path.Combine(stagingDirectory, entry.KernelModuleFileName);

            progress?.Report(new IsoAssemblyProgress(
                BuildMilestone.DriverInjection,
                0,
                $"Téléchargement du pilote {entry.DisplayName}..."));

            try
            {
                var downloadProgress = new SynchronousProgress<FileDownloadProgress>(p =>
                    progress?.Report(new IsoAssemblyProgress(
                        BuildMilestone.DriverInjection,
                        p.PercentComplete ?? 0,
                        $"Téléchargement de {entry.KernelModuleFileName} : {p.BytesReceived} octets reçus.")));

                await _fileDownloader.DownloadAsync(
                    entry.DownloadUrl,
                    destinationPath,
                    entry.Sha256,
                    downloadProgress,
                    cancellationToken);

                downloadedFilePaths.Add(destinationPath);
            }
            catch (Exception ex)
            {
                return Result<IReadOnlyList<string>>.Failure($"Échec du téléchargement du pilote '{entry.DisplayName}' : {ex.Message}");
            }
        }

        return Result<IReadOnlyList<string>>.Success(downloadedFilePaths);
    }

    private Result<string> WriteFirstBootScript(
        AndroidTvProject project,
        IProgress<IsoAssemblyProgress>? progress)
    {
        progress?.Report(new IsoAssemblyProgress(BuildMilestone.DriverInjection, 0, "Génération du script de détection au premier démarrage..."));

        try
        {
            var script = _firstBootScriptGenerator.Generate(project.DriverSelection);
            var stagingDirectory = Path.Combine(Path.GetTempPath(), "AndroidTvPcIsoBuilder", "Drivers");
            Directory.CreateDirectory(stagingDirectory);

            var destinationPath = Path.Combine(stagingDirectory, FirstBootScriptFileName);
            File.WriteAllText(destinationPath, script);

            progress?.Report(new IsoAssemblyProgress(BuildMilestone.DriverInjection, 100, "Script de détection first-boot généré."));

            return Result<string>.Success(destinationPath);
        }
        catch (Exception ex)
        {
            return Result<string>.Failure($"Échec de l'écriture du script de détection first-boot : {ex.Message}");
        }
    }
}

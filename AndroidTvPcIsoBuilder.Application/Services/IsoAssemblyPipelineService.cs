using AndroidTvPcIsoBuilder.Application.Common;
using AndroidTvPcIsoBuilder.Application.Interfaces;
using AndroidTvPcIsoBuilder.Domain.Entities;
using AndroidTvPcIsoBuilder.Domain.Enums;

namespace AndroidTvPcIsoBuilder.Application.Services;

/// <summary>
/// Orchestre le flux complet de génération d'une image Android TV à partir d'un projet
/// déjà configuré (source ISO sélectionnée, apps, bootanimation) : récupération
/// de l'ISO source (déjà téléchargée/importée via le sous-dialogue du wizard dans le cas
/// courant ; retéléchargée ici en filet de sécurité si absente), puis délégation de l'assemblage et de la vérification à <see cref="BuildOrchestrationService"/>.
/// Remplace AospBuildOrchestrationService (supprimé) comme point d'entrée pour l'écran
/// "Compilation en direct" du wizard.
/// </summary>
public class IsoAssemblyPipelineService
{
    private readonly IProjectRepository _repository;
    private readonly IsoDownloadOrchestrationService _downloadService;
    private readonly BuildOrchestrationService _buildOrchestrationService;

    public IsoAssemblyPipelineService(
        IProjectRepository repository,
        IsoDownloadOrchestrationService downloadService,
        BuildOrchestrationService buildOrchestrationService)
    {
        _repository = repository;
        _downloadService = downloadService;
        _buildOrchestrationService = buildOrchestrationService;
    }

    public async Task<Result> RunAsync(
        Guid projectId,
        IProgress<IsoAssemblyProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var project = await _repository.GetByIdAsync(projectId, cancellationToken);
        if (project is null)
            return Result.Failure($"Aucun projet trouvé avec l'identifiant '{projectId}'.");

        // Filet de sécurité : dans le flux normal, l'ISO est déjà téléchargée/importée
        // via le sous-dialogue du wizard avant d'arriver à cette étape. On ne retélécharge
        // ici que si LocalPath est absent ou que le fichier a disparu entre-temps.
        var needsDownload = string.IsNullOrWhiteSpace(project.SourceIso.LocalPath) || !File.Exists(project.SourceIso.LocalPath);
        if (needsDownload)
        {
            if (string.IsNullOrWhiteSpace(project.SourceIso.SourceId))
                return Result.Failure("Aucune source ISO sélectionnée pour ce projet.");

            var source = _downloadService.GetAvailableSources()
                .FirstOrDefault(s => string.Equals(s.Id, project.SourceIso.SourceId, StringComparison.OrdinalIgnoreCase));
            if (source is null)
                return Result.Failure($"Source ISO '{project.SourceIso.SourceId}' introuvable dans le catalogue.");

            progress?.Report(new IsoAssemblyProgress(BuildMilestone.Downloading, 0, $"Téléchargement de {source.DisplayName}..."));

            var destinationPath = Path.Combine(Path.GetTempPath(), "AndroidTvPcIsoBuilder", "SourceIso", $"{source.Id}.iso");
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

            var downloadProgress = new Progress<DownloadProgress>(p =>
                progress?.Report(new IsoAssemblyProgress(
                    BuildMilestone.Downloading,
                    p.PercentComplete ?? 0,
                    $"Téléchargement : {p.BytesReceived} octets reçus.",
                    p.BytesReceived,
                    p.TotalBytes)));

            var downloadResult = await _downloadService.DownloadAsync(source, destinationPath, downloadProgress, cancellationToken);
            if (!downloadResult.IsSuccess)
                return downloadResult;

            project.SourceIso.LocalPath = destinationPath;
            await _repository.SaveAsync(project, cancellationToken);
        }

        var buildProgress = new Progress<BuildProgress>(p =>
            progress?.Report(new IsoAssemblyProgress(BuildMilestone.IsoAssembly, p.PercentComplete, p.Message ?? p.Step)));

        return await _buildOrchestrationService.BuildAsync(
            projectId,
            project.SourceIso.LocalPath!,
            buildProgress,
            cancellationToken);
    }
}

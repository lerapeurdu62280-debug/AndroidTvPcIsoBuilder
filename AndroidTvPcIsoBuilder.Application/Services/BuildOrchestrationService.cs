using AndroidTvPcIsoBuilder.Application.Common;
using AndroidTvPcIsoBuilder.Application.Interfaces;
using AndroidTvPcIsoBuilder.Application.Validation;
using AndroidTvPcIsoBuilder.Domain.Entities;

namespace AndroidTvPcIsoBuilder.Application.Services;

public class BuildOrchestrationService
{
    private const int MaxBuildHistoryEntries = 20;

    private readonly IProjectRepository _repository;
    private readonly IIsoBuilder _isoBuilder;
    private readonly ProjectValidator _validator;
    private readonly IFileSystem _fileSystem;

    public BuildOrchestrationService(IProjectRepository repository, IIsoBuilder isoBuilder, ProjectValidator validator, IFileSystem fileSystem)
    {
        _repository = repository;
        _isoBuilder = isoBuilder;
        _validator = validator;
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Assemble l'image ISO finale à partir d'une source ISO officielle déjà téléchargée
    /// (<paramref name="sourceIsoPath"/>) : injection des APK et de la
    /// bootanimation, puis préservation du boot El Torito d'origine.
    /// </summary>
    public async Task<Result> BuildAsync(
        Guid projectId,
        string sourceIsoPath,
        IProgress<BuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var project = await _repository.GetByIdAsync(projectId, cancellationToken);
        if (project is null)
            return Result.Failure($"Aucun projet trouvé avec l'identifiant '{projectId}'.");

        var validation = _validator.Validate(project);
        if (!validation.IsSuccess)
            return validation;

        if (_fileSystem.FileExists(sourceIsoPath))
        {
            var requiredSpace = (long)(_fileSystem.GetFileSize(sourceIsoPath) * 1.15);
            var freeSpace = _fileSystem.GetAvailableFreeSpace(project.OutputIsoPath);
            if (freeSpace < requiredSpace)
            {
                return Result.Failure(
                    $"Espace disque insuffisant pour générer l'ISO : {FormatBytes(freeSpace)} disponibles, " +
                    $"environ {FormatBytes(requiredSpace)} nécessaires.");
            }
        }

        progress?.Report(new BuildProgress("Démarrage", 0, $"Construction de l'ISO pour le projet '{project.Name}'."));

        try
        {
            await _isoBuilder.BuildAsync(project, sourceIsoPath, progress, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await RecordBuildHistoryAsync(project, succeeded: false, "Annulée par l'utilisateur.", cancellationToken);
            return Result.Failure("La construction de l'ISO a été annulée.");
        }
        catch (Exception ex)
        {
            await RecordBuildHistoryAsync(project, succeeded: false, $"Échec : {ex.Message}", cancellationToken);
            return Result.Failure($"Échec de la construction de l'ISO : {ex.Message}");
        }

        progress?.Report(new BuildProgress("Vérification de l'image générée", 98));
        var verification = _isoBuilder.Verify(project);

        var summary = verification.Issues.Count == 0
            ? $"ISO générée avec succès ({verification.AppsFoundInOutput}/{verification.AppsExpected} applications confirmées)."
            : $"ISO générée avec des avertissements : {string.Join(" ", verification.Issues)}";

        await RecordBuildHistoryAsync(project, succeeded: verification.Issues.Count == 0, summary, cancellationToken, verification.ActualOutputSizeBytes);

        progress?.Report(new BuildProgress("Terminé", 100, $"ISO générée : {project.OutputIsoPath}"));

        return verification.Issues.Count == 0
            ? Result.Success()
            : Result.Failure(verification.Issues);
    }

    public async Task<Result<AndroidTvProject>> PrepareAndValidateAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await _repository.GetByIdAsync(projectId, cancellationToken);
        if (project is null)
            return Result<AndroidTvProject>.Failure($"Aucun projet trouvé avec l'identifiant '{projectId}'.");

        var validation = _validator.Validate(project);
        return validation.IsSuccess
            ? Result<AndroidTvProject>.Success(project)
            : Result<AndroidTvProject>.Failure(validation.Errors);
    }

    public async Task<Result<BuildPreview>> GetPreviewAsync(Guid projectId, string sourceIsoPath, CancellationToken cancellationToken = default)
    {
        var project = await _repository.GetByIdAsync(projectId, cancellationToken);
        if (project is null)
            return Result<BuildPreview>.Failure($"Aucun projet trouvé avec l'identifiant '{projectId}'.");

        var warnings = new List<string>();

        long sourceSize = 0;
        if (_fileSystem.FileExists(sourceIsoPath))
            sourceSize = _fileSystem.GetFileSize(sourceIsoPath);
        else
            warnings.Add("L'image ISO source est introuvable.");

        long appsSize = 0;
        foreach (var app in project.Apps)
        {
            if (_fileSystem.FileExists(app.SourceApkPath))
                appsSize += _fileSystem.GetFileSize(app.SourceApkPath);
            else
                warnings.Add($"L'APK de '{app.Name}' est introuvable ({app.SourceApkPath}).");
        }

        if (project.Apps.Count == 0)
            warnings.Add("Aucune application ne sera injectée dans cette image.");

        var preview = new BuildPreview(
            SourceSizeBytes: sourceSize,
            AppsSizeBytes: appsSize,
            EstimatedOutputSizeBytes: sourceSize + appsSize,
            AppCount: project.Apps.Count,
            Warnings: warnings);

        return Result<BuildPreview>.Success(preview);
    }

    private async Task RecordBuildHistoryAsync(
        AndroidTvProject project,
        bool succeeded,
        string summary,
        CancellationToken cancellationToken,
        long? outputSizeBytes = null)
    {
        project.BuildHistory.Insert(0, new BuildHistoryEntry(DateTimeOffset.Now, succeeded, summary, outputSizeBytes));
        if (project.BuildHistory.Count > MaxBuildHistoryEntries)
            project.BuildHistory.RemoveRange(MaxBuildHistoryEntries, project.BuildHistory.Count - MaxBuildHistoryEntries);

        await _repository.SaveAsync(project, cancellationToken);
    }

    private static string FormatBytes(long bytes)
    {
        double value = bytes;
        string[] units = { "o", "Ko", "Mo", "Go" };
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }
        return $"{value:0.#} {units[unitIndex]}";
    }
}

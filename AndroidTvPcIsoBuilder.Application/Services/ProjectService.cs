using System.Text.Json;
using AndroidTvPcIsoBuilder.Application.Common;
using AndroidTvPcIsoBuilder.Application.Interfaces;
using AndroidTvPcIsoBuilder.Domain.Entities;
using AndroidTvPcIsoBuilder.Domain.Enums;

namespace AndroidTvPcIsoBuilder.Application.Services;

public class ProjectService
{
    private readonly IProjectRepository _repository;

    public ProjectService(IProjectRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<AndroidTvProject>> CreateProjectAsync(
        string name,
        string outputIsoPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result<AndroidTvProject>.Failure("Le nom du projet est requis.");

        if (string.IsNullOrWhiteSpace(outputIsoPath))
            return Result<AndroidTvProject>.Failure("Le chemin de sortie est requis.");

        var project = new AndroidTvProject
        {
            Name = name,
            OutputIsoPath = outputIsoPath,
        };

        await _repository.SaveAsync(project, cancellationToken);
        return Result<AndroidTvProject>.Success(project);
    }

    public async Task<Result<AndroidTvProject>> UpdateProjectSettingsAsync(
        Guid projectId,
        string? name = null,
        string? outputIsoPath = null,
        string? resolution = null,
        string? language = null,
        BootMode? bootMode = null,
        string? sourceIsoLocalPath = null,
        CancellationToken cancellationToken = default)
    {
        var project = await _repository.GetByIdAsync(projectId, cancellationToken);
        if (project is null)
            return Result<AndroidTvProject>.Failure($"Aucun projet trouvé avec l'identifiant '{projectId}'.");

        if (name is not null) project.Name = name;
        if (outputIsoPath is not null) project.OutputIsoPath = outputIsoPath;
        if (resolution is not null) project.Resolution = resolution;
        if (language is not null) project.Language = language;
        if (bootMode is not null) project.BootMode = bootMode.Value;
        if (sourceIsoLocalPath is not null) project.SourceIso.LocalPath = sourceIsoLocalPath;

        await _repository.SaveAsync(project, cancellationToken);
        return Result<AndroidTvProject>.Success(project);
    }

    /// <summary>Enregistre le choix d'ajout des services Google TV (voir <see cref="GoogleServicesConfig"/>).</summary>
    public async Task<Result<AndroidTvProject>> UpdateGoogleServicesAsync(
        Guid projectId,
        GoogleServicesConfig googleServices,
        CancellationToken cancellationToken = default)
    {
        var project = await _repository.GetByIdAsync(projectId, cancellationToken);
        if (project is null)
            return Result<AndroidTvProject>.Failure($"Aucun projet trouvé avec l'identifiant '{projectId}'.");

        project.GoogleServices = googleServices;
        await _repository.SaveAsync(project, cancellationToken);
        return Result<AndroidTvProject>.Success(project);
    }

    public async Task<Result<AndroidTvProject>> GetProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await _repository.GetByIdAsync(projectId, cancellationToken);
        return project is null
            ? Result<AndroidTvProject>.Failure($"Aucun projet trouvé avec l'identifiant '{projectId}'.")
            : Result<AndroidTvProject>.Success(project);
    }

    public async Task<IReadOnlyList<AndroidTvProject>> GetAllProjectsAsync(CancellationToken cancellationToken = default)
        => await _repository.GetAllAsync(cancellationToken);

    public async Task<Result> DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await _repository.GetByIdAsync(projectId, cancellationToken);
        if (project is null)
            return Result.Failure($"Aucun projet trouvé avec l'identifiant '{projectId}'.");

        await _repository.DeleteAsync(projectId, cancellationToken);
        return Result.Success();
    }

    private static readonly JsonSerializerOptions ExportSerializerOptions = new() { WriteIndented = true };

    public async Task<Result> ExportProjectAsync(Guid projectId, string exportPath, CancellationToken cancellationToken = default)
    {
        var project = await _repository.GetByIdAsync(projectId, cancellationToken);
        if (project is null)
            return Result.Failure($"Aucun projet trouvé avec l'identifiant '{projectId}'.");

        try
        {
            await using var stream = File.Create(exportPath);
            await JsonSerializer.SerializeAsync(stream, project, ExportSerializerOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            return Result.Failure($"Échec de l'export du projet : {ex.Message}");
        }

        return Result.Success();
    }

    public async Task<Result<AndroidTvProject>> ImportProjectAsync(string importPath, CancellationToken cancellationToken = default)
    {
        AndroidTvProject? imported;
        try
        {
            await using var stream = File.OpenRead(importPath);
            imported = await JsonSerializer.DeserializeAsync<AndroidTvProject>(stream, ExportSerializerOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            return Result<AndroidTvProject>.Failure($"Échec de l'import du projet : {ex.Message}");
        }

        if (imported is null)
            return Result<AndroidTvProject>.Failure("Le fichier importé ne contient pas un projet valide.");

        // Nouvel identifiant pour éviter d'écraser un projet existant avec le même Id (import
        // d'un fichier exporté depuis une autre machine, ou réimport du même fichier deux fois).
        imported.Id = Guid.NewGuid();
        imported.BuildHistory.Clear();

        await _repository.SaveAsync(imported, cancellationToken);
        return Result<AndroidTvProject>.Success(imported);
    }
}

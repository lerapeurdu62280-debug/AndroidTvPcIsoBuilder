using AndroidTvPcIsoBuilder.Application.Common;
using AndroidTvPcIsoBuilder.Application.Interfaces;
using AndroidTvPcIsoBuilder.Domain.Entities;

namespace AndroidTvPcIsoBuilder.Application.Services;

public class AppPackageService
{
    private readonly IProjectRepository _repository;
    private readonly IFileSystem _fileSystem;

    public AppPackageService(IProjectRepository repository, IFileSystem fileSystem)
    {
        _repository = repository;
        _fileSystem = fileSystem;
    }

    public async Task<Result<AppPackage>> AddAppAsync(
        Guid projectId,
        string name,
        string sourceApkPath,
        bool isPreinstalled = true,
        CancellationToken cancellationToken = default)
    {
        var project = await _repository.GetByIdAsync(projectId, cancellationToken);
        if (project is null)
            return Result<AppPackage>.Failure($"Aucun projet trouvé avec l'identifiant '{projectId}'.");

        if (string.IsNullOrWhiteSpace(name))
            return Result<AppPackage>.Failure("Le nom de l'application est requis.");

        if (string.IsNullOrWhiteSpace(sourceApkPath) || !sourceApkPath.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
            return Result<AppPackage>.Failure("Le chemin doit pointer vers un fichier .apk.");

        if (!_fileSystem.FileExists(sourceApkPath))
            return Result<AppPackage>.Failure($"Le fichier '{sourceApkPath}' est introuvable.");

        if (project.Apps.Any(a => a.SourceApkPath.Equals(sourceApkPath, StringComparison.OrdinalIgnoreCase)))
            return Result<AppPackage>.Failure($"L'application '{sourceApkPath}' est déjà présente dans le projet.");

        var app = new AppPackage
        {
            Name = name,
            SourceApkPath = sourceApkPath,
            IsPreinstalled = isPreinstalled
        };

        project.Apps.Add(app);
        await _repository.SaveAsync(project, cancellationToken);
        return Result<AppPackage>.Success(app);
    }

    public async Task<Result> RemoveAppAsync(Guid projectId, string sourceApkPath, CancellationToken cancellationToken = default)
    {
        var project = await _repository.GetByIdAsync(projectId, cancellationToken);
        if (project is null)
            return Result.Failure($"Aucun projet trouvé avec l'identifiant '{projectId}'.");

        var app = project.Apps.FirstOrDefault(a => a.SourceApkPath.Equals(sourceApkPath, StringComparison.OrdinalIgnoreCase));
        if (app is null)
            return Result.Failure($"Aucune application avec le chemin '{sourceApkPath}' n'a été trouvée dans ce projet.");

        project.Apps.Remove(app);
        await _repository.SaveAsync(project, cancellationToken);
        return Result.Success();
    }
}

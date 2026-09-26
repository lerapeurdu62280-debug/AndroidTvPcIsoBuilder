using AndroidTvPcIsoBuilder.Application.Common;
using AndroidTvPcIsoBuilder.Application.Interfaces;
using AndroidTvPcIsoBuilder.Domain.Entities;

namespace AndroidTvPcIsoBuilder.Application.Validation;

public class ProjectValidator
{
    private readonly IFileSystem _fileSystem;

    public ProjectValidator(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public Result Validate(AndroidTvProject project)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(project.Name))
            errors.Add("Le nom du projet est requis.");

        if (string.IsNullOrWhiteSpace(project.SourceIso.LocalPath))
            errors.Add("Aucune image ISO source n'a été téléchargée ou importée.");
        else if (!_fileSystem.FileExists(project.SourceIso.LocalPath))
            errors.Add($"L'image ISO source '{project.SourceIso.LocalPath}' est introuvable.");

        if (string.IsNullOrWhiteSpace(project.OutputIsoPath))
            errors.Add("Le chemin de sortie de l'ISO est requis.");
        else if (!project.OutputIsoPath.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
            errors.Add("Le chemin de sortie doit se terminer par '.iso'.");

        if (!IsValidResolution(project.Resolution))
            errors.Add($"La résolution '{project.Resolution}' est invalide. Format attendu : LARGEURxHAUTEUR (ex: 1920x1080).");

        errors.AddRange(ValidateGoogleServices(project.GoogleServices));
        if (project.AptoideTv.Enabled && !string.IsNullOrWhiteSpace(project.AptoideTv.ApkPath) && !_fileSystem.FileExists(project.AptoideTv.ApkPath))
            errors.Add($"Aptoide TV : l'APK '{project.AptoideTv.ApkPath}' est introuvable.");

        foreach (var app in project.Apps)
        {
            var appErrors = ValidateApp(app);
            errors.AddRange(appErrors);
        }

        return errors.Count == 0 ? Result.Success() : Result.Failure(errors);
    }

    private List<string> ValidateApp(AppPackage app)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(app.Name))
            errors.Add("Le nom d'une application est requis.");

        if (string.IsNullOrWhiteSpace(app.SourceApkPath))
            errors.Add($"Le chemin de l'APK est requis pour l'application '{app.Name}'.");
        else if (!app.SourceApkPath.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
            errors.Add($"Le fichier '{app.SourceApkPath}' n'est pas un APK valide.");
        else if (!_fileSystem.FileExists(app.SourceApkPath))
            errors.Add($"L'APK '{app.SourceApkPath}' est introuvable pour l'application '{app.Name}'.");

        return errors;
    }

    private List<string> ValidateGoogleServices(GoogleServicesConfig config)
    {
        var errors = new List<string>();
        if (!config.Enabled)
            return errors;

        if (string.IsNullOrWhiteSpace(config.DonorIsoPath) && string.IsNullOrWhiteSpace(config.PlayStoreApkPath))
            errors.Add("Services Google TV : choisissez l'ISO donneuse qui les contient.");
        if (!string.IsNullOrWhiteSpace(config.DonorIsoPath) && !_fileSystem.FileExists(config.DonorIsoPath))
            errors.Add($"Services Google TV : l'ISO donneuse '{config.DonorIsoPath}' est introuvable.");
        if (!string.IsNullOrWhiteSpace(config.PlayStoreApkPath) && !_fileSystem.FileExists(config.PlayStoreApkPath))
            errors.Add($"Services Google TV : l'APK Play Store '{config.PlayStoreApkPath}' est introuvable.");

        return errors;
    }

    private static bool IsValidResolution(string resolution)
    {
        var parts = resolution.Split('x');
        return parts.Length == 2
            && int.TryParse(parts[0], out var width) && width > 0
            && int.TryParse(parts[1], out var height) && height > 0;
    }
}

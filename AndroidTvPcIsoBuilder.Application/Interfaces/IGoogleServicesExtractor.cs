using AndroidTvPcIsoBuilder.Application.Common;
using AndroidTvPcIsoBuilder.Domain.Entities;

namespace AndroidTvPcIsoBuilder.Application.Interfaces;

/// <summary>
/// Prépare les services Google TV à greffer : extrait de l'ISO donneuse les seuls composants
/// Google (applications et fichiers de permissions) et y ajoute l'APK Play Store fourni.
/// </summary>
public interface IGoogleServicesExtractor
{
    /// <summary>
    /// Écrit dans <paramref name="outputDirectory"/> une arborescence calquée sur le système
    /// Android (product/priv-app/..., product/etc/permissions/..., system_ext/priv-app/...) et
    /// retourne son chemin.
    /// </summary>
    Task<Result<string>> ExtractAsync(GoogleServicesConfig config, string outputDirectory, CancellationToken cancellationToken = default);
}

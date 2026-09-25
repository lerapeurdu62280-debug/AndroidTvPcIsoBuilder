using AndroidTvPcIsoBuilder.Application.Common;
using AndroidTvPcIsoBuilder.Application.Services;
using AndroidTvPcIsoBuilder.Domain.Entities;

namespace AndroidTvPcIsoBuilder.Application.Interfaces;

/// <summary>
/// Télécharge (via <see cref="IFileDownloader"/>) les modules pilotes sélectionnés et
/// génère le script de détection first-boot dans un répertoire de staging local, puis
/// retourne les chemins des fichiers résolus, prêts à être injectés dans l'image ISO
/// finale par <see cref="IIsoBuilder"/> (même mécanisme que l'injection des APK).
/// </summary>
public interface IDriverPackInjector
{
    Task<Result<IReadOnlyList<string>>> ResolveDriverFilesAsync(
        AndroidTvProject project,
        IProgress<IsoAssemblyProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

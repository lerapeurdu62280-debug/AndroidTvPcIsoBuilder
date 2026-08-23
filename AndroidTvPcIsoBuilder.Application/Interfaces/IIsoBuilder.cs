using AndroidTvPcIsoBuilder.Application.Services;
using AndroidTvPcIsoBuilder.Domain.Entities;

namespace AndroidTvPcIsoBuilder.Application.Interfaces;

public interface IIsoBuilder
{
    Task BuildAsync(AndroidTvProject project, IProgress<BuildProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Relit l'ISO produite pour confirmer qu'elle est lisible, contient un catalogue de boot
    /// et que les APK attendus sont bien présents dans /APPS.
    /// </summary>
    BuildVerification Verify(AndroidTvProject project);
}

public sealed record BuildProgress(string Step, int PercentComplete, string? Message = null);

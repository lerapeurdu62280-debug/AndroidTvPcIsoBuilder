using AndroidTvPcIsoBuilder.Application.Services;
using AndroidTvPcIsoBuilder.Domain.Entities;

namespace AndroidTvPcIsoBuilder.Application.Interfaces;

public interface IIsoBuilder
{
    /// <summary>
    /// Construit l'ISO de sortie à partir d'une image source déjà bootable téléchargée
    /// (<paramref name="sourceIsoPath"/>) : copie son contenu, y injecte les APK
    /// sélectionnés (/APPS), la bootanimation personnalisée si activée (/BOOTANIM) et le
    /// script de démarrage qui les branche sur le système (/scripts), puis préserve le catalogue de boot El Torito d'origine.
    /// </summary>
    Task BuildAsync(
        AndroidTvProject project,
        string sourceIsoPath,
        IProgress<BuildProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Relit l'ISO produite pour confirmer qu'elle est lisible, contient un catalogue de boot
    /// et que les APK attendus sont bien présents dans /APPS.
    /// </summary>
    BuildVerification Verify(AndroidTvProject project);
}

public sealed record BuildProgress(string Step, int PercentComplete, string? Message = null);

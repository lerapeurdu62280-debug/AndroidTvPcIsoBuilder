using AndroidTvPcIsoBuilder.Application.Common;
using AndroidTvPcIsoBuilder.Domain.Entities;

namespace AndroidTvPcIsoBuilder.Application.Interfaces;

/// <summary>
/// Génère le fichier <c>bootanimation.zip</c> (mécanisme standard Android : séquences de
/// PNG numérotées + <c>desc.txt</c>) à partir d'une <see cref="BootAnimationConfig"/>.
/// </summary>
public interface IBootAnimationGenerator
{
    /// <summary>Génère le zip dans <paramref name="outputDirectory"/> et retourne son chemin complet.</summary>
    Task<Result<string>> GenerateAsync(BootAnimationConfig config, string outputDirectory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Génère l'image du logo lue par le programme <c>atvsplash</c>, qui anime ce logo sur le
    /// framebuffer dès le début du démarrage, avant que la bootanimation d'Android ne prenne le
    /// relais. Même logo, même taille et même rythme de respiration que la bootanimation.
    /// Retourne le chemin complet du fichier produit dans <paramref name="outputDirectory"/>.
    /// </summary>
    Task<Result<string>> GenerateSplashImageAsync(BootAnimationConfig config, string outputDirectory, CancellationToken cancellationToken = default);
}

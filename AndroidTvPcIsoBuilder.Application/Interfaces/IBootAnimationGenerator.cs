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
}

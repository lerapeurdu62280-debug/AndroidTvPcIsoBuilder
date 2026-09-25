namespace AndroidTvPcIsoBuilder.Domain.Enums;

/// <summary>
/// Jalons nommés du pipeline de génération d'une image Android TV à partir d'une ISO
/// officielle téléchargée : téléchargement, vérification, injection (apps/
/// bootanimation), assemblage, restauration du boot, vérification finale.
/// </summary>
public enum BuildMilestone
{
    Starting,
    Downloading,
    ChecksumVerification,
    ReadingSourceImage,
    AppInjection,
    BootAnimationInjection,
    IsoAssembly,
    BootCatalogRestore,
    Verification,
    Done
}

namespace AndroidTvPcIsoBuilder.Domain.Enums;

/// <summary>
/// Jalons nommés du pipeline de génération d'une image Android TV à partir d'une ISO
/// officielle téléchargée : téléchargement, vérification, injection (pilotes/apps/
/// bootanimation), assemblage, restauration du boot, vérification finale.
/// </summary>
public enum BuildMilestone
{
    Starting,
    Downloading,
    ChecksumVerification,
    ReadingSourceImage,
    DriverInjection,
    AppInjection,
    BootAnimationInjection,
    IsoAssembly,
    BootCatalogRestore,
    Verification,
    Done
}

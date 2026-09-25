namespace AndroidTvPcIsoBuilder.Domain.Entities;

/// <summary>
/// Une entrée du catalogue de pilotes Wi-Fi/Bluetooth pré-compilés, téléchargeable
/// à la demande. Chaque pack de modules noyau est lié à l'ABI d'une image ISO précise :
/// <see cref="SupportedIsoSourceId"/> doit référencer l'Id d'une <c>IsoDownloadSource</c>
/// du catalogue ISO (voir IIsoDownloadService.GetAvailableSources()), et être tenu à jour
/// à chaque nouvelle source ISO ajoutée au catalogue.
/// </summary>
public sealed record DriverCatalogEntry(
    string VendorId,
    string DisplayName,
    string Description,
    IReadOnlyList<string> SupportedChipsetModels,
    string KernelModuleFileName,
    string SupportedIsoSourceId,
    Uri DownloadUrl,
    long ApproximateSizeBytes,
    string? Sha256);

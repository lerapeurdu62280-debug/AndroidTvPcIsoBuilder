using AndroidTvPcIsoBuilder.Application.Interfaces;
using AndroidTvPcIsoBuilder.Domain.Entities;

namespace AndroidTvPcIsoBuilder.Infrastructure.Drivers;

/// <summary>
/// Catalogue statique (figé en dur, pas de fichier externe) des packs de pilotes
/// Wi-Fi/Bluetooth pré-compilés proposés à l'injection. Une entrée par couple
/// (vendor, source ISO ciblée) : voir <see cref="HttpIsoDownloadService"/> pour les Id
/// des sources ISO courantes.
///
/// IMPORTANT — PLACEHOLDER : les URLs de téléchargement ci-dessous
/// (<c>https://cdn.example.com/drivers/...</c>) sont des adresses plausibles mais
/// FICTIVES. Elles devront être remplacées par de vraies URLs d'hébergement une fois
/// les packs de pilotes réellement produits et publiés (le hachage SHA256 sera alors
/// également renseigné pour activer la vérification d'intégrité de
/// <see cref="IFileDownloader"/>).
/// </summary>
public class StaticDriverCatalogService : IDriverCatalogService
{
    private const string AndroidX86X64SourceId = "android-x86-9.0-r2-x64";
    private const string AndroidX86X86SourceId = "android-x86-9.0-r2-x86";
    private const string LineageOsTvSourceId = "lineageos-tv-x86-21.0";
    private const string GoogleTvSourceId = "googletv-x86-14-v27t";

    /// <summary>
    /// Poids heuristiques de couverture par vendor, utilisés par <see cref="EstimateCoveragePercent"/>.
    /// Valeurs approximatives basées sur la popularité relative des chipsets Wi-Fi/BT PC courants ;
    /// à affiner avec des données réelles de terrain lorsque disponibles.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, double> CoverageWeights = new Dictionary<string, double>
    {
        ["realtek"] = 35d,
        ["broadcom"] = 25d,
        ["intel"] = 25d,
        ["atheros_qualcomm"] = 15d,
    };

    private static readonly IReadOnlyList<DriverCatalogEntry> Catalog = BuildCatalog();

    public IReadOnlyList<DriverCatalogEntry> GetAvailableDrivers() => Catalog;

    /// <summary>
    /// Heuristique simple et volontairement approximative : chaque vendor id sélectionné
    /// ajoute son poids fixe (voir <see cref="CoverageWeights"/>), la somme est plafonnée à 99%
    /// (jamais 100%, une couverture totale ne pouvant pas être garantie par une estimation statique).
    /// Un vendor id inconnu du catalogue n'ajoute rien.
    /// </summary>
    public double EstimateCoveragePercent(IReadOnlyList<string> selectedVendorIds)
    {
        if (selectedVendorIds.Count == 0)
            return 0d;

        var total = selectedVendorIds
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Sum(vendorId => CoverageWeights.TryGetValue(vendorId, out var weight) ? weight : 0d);

        return Math.Min(total, 99d);
    }

    private static IReadOnlyList<DriverCatalogEntry> BuildCatalog()
    {
        var entries = new List<DriverCatalogEntry>();

        foreach (var isoSourceId in new[] { AndroidX86X64SourceId, AndroidX86X86SourceId, LineageOsTvSourceId, GoogleTvSourceId })
        {
            entries.Add(new DriverCatalogEntry(
                VendorId: "realtek",
                DisplayName: "Realtek RTL8xxx (Wi-Fi/Bluetooth)",
                Description: "Pilotes combinés Wi-Fi/Bluetooth pour les chipsets Realtek RTL8188/RTL8192/RTL8723/RTL8821/RTL8852, très répandus sur les cartes PCIe et modules M.2 grand public.",
                SupportedChipsetModels: new[] { "RTL8188EE", "RTL8192EE", "RTL8723DE", "RTL8821CE", "RTL8852BE" },
                KernelModuleFileName: "rtw_pci.ko",
                SupportedIsoSourceId: isoSourceId,
                DownloadUrl: new Uri($"https://cdn.example.com/drivers/realtek/{isoSourceId}/rtw_pci.tar.gz"),
                ApproximateSizeBytes: 4_500_000,
                Sha256: null));

            entries.Add(new DriverCatalogEntry(
                VendorId: "broadcom",
                DisplayName: "Broadcom BCM43xx (Wi-Fi/Bluetooth)",
                Description: "Pilotes Wi-Fi/Bluetooth pour les chipsets Broadcom BCM4356/BCM4360/BCM43602, courants sur les configurations OEM et certains mini-PC.",
                SupportedChipsetModels: new[] { "BCM4356", "BCM4360", "BCM43602" },
                KernelModuleFileName: "brcmfmac.ko",
                SupportedIsoSourceId: isoSourceId,
                DownloadUrl: new Uri($"https://cdn.example.com/drivers/broadcom/{isoSourceId}/brcmfmac.tar.gz"),
                ApproximateSizeBytes: 3_800_000,
                Sha256: null));

            entries.Add(new DriverCatalogEntry(
                VendorId: "intel",
                DisplayName: "Intel AX2xx/9260/8265 (Wi-Fi/Bluetooth)",
                Description: "Pilotes Wi-Fi/Bluetooth pour les chipsets Intel AX200/AX201/AX210, 9260 et 8265, standards sur les cartes mini-PCIe/M.2 des PC de bureau et portables récents.",
                SupportedChipsetModels: new[] { "AX200", "AX201", "AX210", "9260", "8265" },
                KernelModuleFileName: "iwlwifi.ko",
                SupportedIsoSourceId: isoSourceId,
                DownloadUrl: new Uri($"https://cdn.example.com/drivers/intel/{isoSourceId}/iwlwifi.tar.gz"),
                ApproximateSizeBytes: 5_200_000,
                Sha256: null));

            entries.Add(new DriverCatalogEntry(
                VendorId: "atheros_qualcomm",
                DisplayName: "Atheros/Qualcomm QCA6174/9377 (Wi-Fi/Bluetooth)",
                Description: "Pilotes Wi-Fi/Bluetooth pour les chipsets Atheros/Qualcomm QCA6174 et QCA9377, présents sur certains mini-PC et cartes M.2 d'entrée de gamme.",
                SupportedChipsetModels: new[] { "QCA6174", "QCA9377" },
                KernelModuleFileName: "ath10k_pci.ko",
                SupportedIsoSourceId: isoSourceId,
                DownloadUrl: new Uri($"https://cdn.example.com/drivers/atheros_qualcomm/{isoSourceId}/ath10k_pci.tar.gz"),
                ApproximateSizeBytes: 2_900_000,
                Sha256: null));
        }

        return entries;
    }
}

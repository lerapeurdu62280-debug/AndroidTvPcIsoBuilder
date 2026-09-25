namespace AndroidTvPcIsoBuilder.Domain.Entities;

/// <summary>
/// Choix de l'utilisateur pour la couverture Wi-Fi/Bluetooth de l'image générée :
/// les deux mécanismes (pack embarqué et détection automatique au premier boot)
/// sont complémentaires et peuvent être activés simultanément.
/// </summary>
public class WifiBluetoothDriverSelection
{
    public bool EmbeddedDriverPackEnabled { get; set; } = true;
    public bool AutoDetectFirstBootEnabled { get; set; } = true;

    /// <summary>Identifiants (VendorId) du catalogue <see cref="DriverCatalogEntry"/> sélectionnés.</summary>
    public List<string> SelectedChipsetVendorIds { get; set; } = new();
}

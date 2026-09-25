using AndroidTvPcIsoBuilder.Domain.Entities;

namespace AndroidTvPcIsoBuilder.Application.Interfaces;

/// <summary>
/// Expose le catalogue de pilotes Wi-Fi/Bluetooth pré-compilés téléchargeables à la
/// demande, et une estimation de couverture matérielle pour un ensemble de choix.
/// </summary>
public interface IDriverCatalogService
{
    IReadOnlyList<DriverCatalogEntry> GetAvailableDrivers();

    /// <summary>
    /// Estimation heuristique (0-99) de la couverture des chipsets Wi-Fi/BT PC courants
    /// pour l'ensemble de vendor ids sélectionné. Approximative par construction :
    /// à présenter côté UI comme une estimation, jamais comme une garantie.
    /// </summary>
    double EstimateCoveragePercent(IReadOnlyList<string> selectedVendorIds);
}

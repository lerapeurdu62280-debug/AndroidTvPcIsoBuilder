using AndroidTvPcIsoBuilder.Application.Interfaces;
using AndroidTvPcIsoBuilder.Domain.Entities;

namespace AndroidTvPcIsoBuilder.Tests.TestDoubles;

/// <summary>Double de test pour <see cref="IDriverCatalogService"/> : catalogue et couverture configurables.</summary>
public class FakeDriverCatalogService : IDriverCatalogService
{
    public List<DriverCatalogEntry> Drivers { get; set; } = new();
    public double CoveragePercentToReturn { get; set; }

    public IReadOnlyList<DriverCatalogEntry> GetAvailableDrivers() => Drivers;

    public double EstimateCoveragePercent(IReadOnlyList<string> selectedVendorIds) => CoveragePercentToReturn;
}

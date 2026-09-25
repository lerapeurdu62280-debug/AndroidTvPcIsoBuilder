using AndroidTvPcIsoBuilder.Infrastructure.Drivers;

namespace AndroidTvPcIsoBuilder.Tests.Drivers;

[TestClass]
public class StaticDriverCatalogServiceTests
{
    [TestMethod]
    public void GetAvailableDrivers_ContientLesQuatreVendorsAttendus()
    {
        var service = new StaticDriverCatalogService();

        var vendorIds = service.GetAvailableDrivers().Select(d => d.VendorId).Distinct().ToList();

        CollectionAssert.AreEquivalent(
            new[] { "realtek", "broadcom", "intel", "atheros_qualcomm" },
            vendorIds);
    }

    [TestMethod]
    public void GetAvailableDrivers_ContientUneEntreeParVendorEtParSourceIsoSupportee()
    {
        var service = new StaticDriverCatalogService();
        var drivers = service.GetAvailableDrivers();

        foreach (var vendorId in new[] { "realtek", "broadcom", "intel", "atheros_qualcomm" })
        {
            var isoSourceIds = drivers.Where(d => d.VendorId == vendorId).Select(d => d.SupportedIsoSourceId).Distinct().ToList();

            Assert.IsTrue(isoSourceIds.Contains("android-x86-9.0-r2-x64"), $"{vendorId} devrait supporter android-x86-9.0-r2-x64");
            Assert.IsTrue(isoSourceIds.Contains("lineageos-tv-x86-21.0"), $"{vendorId} devrait supporter lineageos-tv-x86-21.0");
            Assert.IsTrue(isoSourceIds.Contains("googletv-x86-14-v27t"), $"{vendorId} devrait supporter googletv-x86-14-v27t");
        }
    }

    [TestMethod]
    public void GetAvailableDrivers_ChaqueEntreeAUneUrlDeTelechargementValide()
    {
        var service = new StaticDriverCatalogService();

        foreach (var entry in service.GetAvailableDrivers())
        {
            Assert.IsNotNull(entry.DownloadUrl);
            Assert.IsTrue(entry.DownloadUrl.IsAbsoluteUri);
            Assert.IsFalse(string.IsNullOrWhiteSpace(entry.KernelModuleFileName));
        }
    }

    [TestMethod]
    public void EstimateCoveragePercent_AucunVendor_RetourneZero()
    {
        var service = new StaticDriverCatalogService();

        var coverage = service.EstimateCoveragePercent(Array.Empty<string>());

        Assert.AreEqual(0d, coverage);
    }

    [TestMethod]
    public void EstimateCoveragePercent_PlusDeVendors_DonnePlusDeCouverture()
    {
        var service = new StaticDriverCatalogService();

        var coverageOne = service.EstimateCoveragePercent(new[] { "realtek" });
        var coverageTwo = service.EstimateCoveragePercent(new[] { "realtek", "broadcom" });
        var coverageThree = service.EstimateCoveragePercent(new[] { "realtek", "broadcom", "intel" });

        Assert.IsTrue(coverageTwo > coverageOne);
        Assert.IsTrue(coverageThree > coverageTwo);
    }

    [TestMethod]
    public void EstimateCoveragePercent_TousLesVendors_NeDepassePasQuatreVingtDixNeuf()
    {
        var service = new StaticDriverCatalogService();

        var coverage = service.EstimateCoveragePercent(new[] { "realtek", "broadcom", "intel", "atheros_qualcomm" });

        Assert.IsTrue(coverage <= 99d);
        Assert.AreNotEqual(100d, coverage);
    }

    [TestMethod]
    public void EstimateCoveragePercent_VendorInconnu_NAjouteRien()
    {
        var service = new StaticDriverCatalogService();

        var coverage = service.EstimateCoveragePercent(new[] { "vendor_inexistant" });

        Assert.AreEqual(0d, coverage);
    }
}

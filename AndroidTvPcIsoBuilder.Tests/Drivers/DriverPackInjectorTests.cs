using AndroidTvPcIsoBuilder.Application.Services;
using AndroidTvPcIsoBuilder.Domain.Entities;
using AndroidTvPcIsoBuilder.Infrastructure.Drivers;
using AndroidTvPcIsoBuilder.Tests.TestDoubles;

namespace AndroidTvPcIsoBuilder.Tests.Drivers;

[TestClass]
public class DriverPackInjectorTests
{
    private static AndroidTvProject CreateProject(string isoSourceId = "lineageos-tv-x86-21.0") => new()
    {
        Name = "Projet Test",
        OutputIsoPath = "C:\\Output\\out.iso",
        SourceIso = new SourceIsoSelection { SourceId = isoSourceId },
    };

    private static DriverCatalogEntry CreateEntry(string vendorId, string isoSourceId) => new(
        VendorId: vendorId,
        DisplayName: $"Pilote {vendorId}",
        Description: "Description de test",
        SupportedChipsetModels: new[] { "MODEL_TEST" },
        KernelModuleFileName: $"{vendorId}.ko",
        SupportedIsoSourceId: isoSourceId,
        DownloadUrl: new Uri($"https://cdn.example.com/drivers/{vendorId}/{isoSourceId}/{vendorId}.tar.gz"),
        ApproximateSizeBytes: 1024,
        Sha256: null);

    [TestMethod]
    public async Task ResolveDriverFilesAsync_LesDeuxFlagsDesactives_NeFaitRienEtRetourneListeVide()
    {
        var project = CreateProject();
        project.DriverSelection.EmbeddedDriverPackEnabled = false;
        project.DriverSelection.AutoDetectFirstBootEnabled = false;
        project.DriverSelection.SelectedChipsetVendorIds = new List<string> { "realtek" };

        var catalogService = new FakeDriverCatalogService();
        var downloader = new FakeFileDownloader();
        var generator = new FirstBootScriptGenerator();
        var injector = new DriverPackInjector(catalogService, downloader, generator);

        var result = await injector.ResolveDriverFilesAsync(project);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(0, result.Value.Count);
        Assert.AreEqual(0, downloader.RequestedUrls.Count);
    }

    [TestMethod]
    public async Task ResolveDriverFilesAsync_VendorConnu_TelechargeEtRetourneLeCheminDuModule()
    {
        var project = CreateProject();
        project.DriverSelection.EmbeddedDriverPackEnabled = true;
        project.DriverSelection.AutoDetectFirstBootEnabled = false;
        project.DriverSelection.SelectedChipsetVendorIds = new List<string> { "realtek" };

        var catalogService = new FakeDriverCatalogService
        {
            Drivers = new List<DriverCatalogEntry> { CreateEntry("realtek", "lineageos-tv-x86-21.0") }
        };
        var downloader = new FakeFileDownloader { ContentToWrite = new byte[] { 1, 2, 3 } };
        var generator = new FirstBootScriptGenerator();
        var injector = new DriverPackInjector(catalogService, downloader, generator);

        var reports = new List<IsoAssemblyProgress>();
        var progress = new Progress<IsoAssemblyProgress>(reports.Add);

        var result = await injector.ResolveDriverFilesAsync(project, progress);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, downloader.RequestedUrls.Count);
        Assert.AreEqual(1, result.Value.Count);
        Assert.IsTrue(result.Value[0].EndsWith("realtek.ko"), $"Chemin inattendu : {result.Value[0]}");
        Assert.IsTrue(File.Exists(result.Value[0]), $"Fichier résolu introuvable : {result.Value[0]}");
    }

    [TestMethod]
    public async Task ResolveDriverFilesAsync_VendorSansCorrespondancePourLaSourceIso_AjouteUnAvertissementSansEchouer()
    {
        var project = CreateProject("lineageos-tv-x86-21.0");
        project.DriverSelection.EmbeddedDriverPackEnabled = true;
        project.DriverSelection.AutoDetectFirstBootEnabled = false;
        project.DriverSelection.SelectedChipsetVendorIds = new List<string> { "realtek" };

        var catalogService = new FakeDriverCatalogService
        {
            // Entrée présente seulement pour une autre source ISO : aucune correspondance.
            Drivers = new List<DriverCatalogEntry> { CreateEntry("realtek", "googletv-x86-14-v27t") }
        };
        var downloader = new FakeFileDownloader();
        var generator = new FirstBootScriptGenerator();
        var injector = new DriverPackInjector(catalogService, downloader, generator);

        var reports = new List<IsoAssemblyProgress>();
        var progress = new Progress<IsoAssemblyProgress>(reports.Add);

        var result = await injector.ResolveDriverFilesAsync(project, progress);

        Assert.IsTrue(result.IsSuccess, "Un pilote manquant reste un avertissement, pas un échec.");
        Assert.AreEqual(0, downloader.RequestedUrls.Count);
        Assert.IsTrue(reports.Any(r => r.LogLine != null && r.LogLine.Contains("Avertissement")),
            "Un rapport de progression contenant un avertissement était attendu.");
    }

    [TestMethod]
    public async Task ResolveDriverFilesAsync_AutoDetectActive_RetourneLeCheminDuScriptFirstBoot()
    {
        var project = CreateProject();
        project.DriverSelection.EmbeddedDriverPackEnabled = false;
        project.DriverSelection.AutoDetectFirstBootEnabled = true;
        project.DriverSelection.SelectedChipsetVendorIds = new List<string> { "broadcom" };

        var catalogService = new FakeDriverCatalogService();
        var downloader = new FakeFileDownloader();
        var generator = new FirstBootScriptGenerator();
        var injector = new DriverPackInjector(catalogService, downloader, generator);

        var result = await injector.ResolveDriverFilesAsync(project);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, result.Value.Count);
        Assert.IsTrue(result.Value[0].EndsWith("first-boot-detect.sh"), $"Chemin inattendu : {result.Value[0]}");
        Assert.IsTrue(File.Exists(result.Value[0]));

        var content = await File.ReadAllTextAsync(result.Value[0]);
        StringAssert.Contains(content, "\"broadcom\"");
    }
}

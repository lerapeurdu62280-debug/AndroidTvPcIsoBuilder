using AndroidTvPcIsoBuilder.Domain.Entities;
using AndroidTvPcIsoBuilder.Infrastructure.Iso;
using AndroidTvPcIsoBuilder.Tests.TestDoubles;
using DiscUtils.Iso9660;

namespace AndroidTvPcIsoBuilder.Tests.Iso;

[TestClass]
public class IsoBuilderTests
{
    private string _tempDirectory = null!;
    private string _sourceIsoPath = null!;
    private string _outputIsoPath = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "IsoBuilderTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDirectory);
        _sourceIsoPath = Path.Combine(_tempDirectory, "source.iso");
        _outputIsoPath = Path.Combine(_tempDirectory, "output.iso");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    private static readonly byte[] BootImageBytes = CreateBootImage();

    private static byte[] CreateBootImage()
    {
        // Image de boot factice de la taille d'une disquette 1.44M pour rester dans un cas standard.
        var data = new byte[1440 * 1024];
        for (var i = 0; i < 512; i++)
            data[i] = (byte)(i % 256);
        return data;
    }

    private void CreateSourceIsoWithBoot(string kernelContent = "fake kernel content")
    {
        var builder = new CDBuilder { UseJoliet = true, VolumeIdentifier = "ANDROIDTV" };
        builder.AddFile("KERNEL", System.Text.Encoding.ASCII.GetBytes(kernelContent));
        builder.AddDirectory("ISOLINUX");
        builder.AddFile("ISOLINUX\\ISOLINUX.BIN", new byte[2048]);

        builder.SetBootImage(new MemoryStream(BootImageBytes), BootDeviceEmulation.Diskette1440KiB, 0);

        builder.Build(_sourceIsoPath);
    }

    private static AndroidTvProject CreateProject(string outputPath) => new()
    {
        Name = "Mon Projet TV",
        OutputIsoPath = outputPath
    };

    [TestMethod]
    public async Task BuildAsync_ImageSourceSansApps_ProduitUneIsoLisibleAvecLeMemeContenu()
    {
        CreateSourceIsoWithBoot();
        var project = CreateProject(_outputIsoPath);
        var builder = new IsoBuilder(new FakeBootAnimationGenerator());

        await builder.BuildAsync(project, _sourceIsoPath);

        Assert.IsTrue(File.Exists(_outputIsoPath));

        await using var outputStream = File.OpenRead(_outputIsoPath);
        var reader = new CDReader(outputStream, joliet: true);

        Assert.IsTrue(reader.FileExists("KERNEL"));
        using var kernelStream = reader.OpenFile("KERNEL", FileMode.Open);
        using var readerText = new StreamReader(kernelStream);
        Assert.AreEqual("fake kernel content", readerText.ReadToEnd());
    }

    [TestMethod]
    public async Task BuildAsync_AvecApps_AjouteLesApkEtLeManifeste()
    {
        CreateSourceIsoWithBoot();

        var apkPath = Path.Combine(_tempDirectory, "youtube.apk");
        await File.WriteAllBytesAsync(apkPath, new byte[] { 1, 2, 3, 4 });

        var project = CreateProject(_outputIsoPath);
        project.Apps.Add(new AppPackage { Name = "YouTube", SourceApkPath = apkPath });

        var builder = new IsoBuilder(new FakeBootAnimationGenerator());
        await builder.BuildAsync(project, _sourceIsoPath);

        await using var outputStream = File.OpenRead(_outputIsoPath);
        var reader = new CDReader(outputStream, joliet: true);

        Assert.IsTrue(reader.FileExists("APPS\\YOUTUBE.APK"));
        Assert.IsTrue(reader.FileExists("APPS\\MANIFEST.JSON"));

        using var manifestStream = reader.OpenFile("APPS\\MANIFEST.JSON", FileMode.Open);
        using var manifestReader = new StreamReader(manifestStream);
        var manifestContent = await manifestReader.ReadToEndAsync();
        StringAssert.Contains(manifestContent, "YouTube");
    }

    [TestMethod]
    public async Task BuildAsync_PreserveLeCatalogueDeBoot_ImageDeBootIdentiqueEnSortie()
    {
        CreateSourceIsoWithBoot();
        var project = CreateProject(_outputIsoPath);
        var builder = new IsoBuilder(new FakeBootAnimationGenerator());

        await builder.BuildAsync(project, _sourceIsoPath);

        var preserver = new BootCatalogPreserver();

        await using var sourceStream = File.OpenRead(_sourceIsoPath);
        var sourceCatalog = preserver.ReadBootCatalog(sourceStream);

        await using var outputStream = File.OpenRead(_outputIsoPath);
        var outputCatalog = preserver.ReadBootCatalog(outputStream);

        Assert.IsNotNull(sourceCatalog);
        Assert.IsNotNull(outputCatalog);
        Assert.AreEqual(1, sourceCatalog.BootImages.Count);
        Assert.AreEqual(1, outputCatalog.BootImages.Count);
        CollectionAssert.AreEqual(sourceCatalog.BootImages[0].Data, outputCatalog.BootImages[0].Data);
    }

    [TestMethod]
    public async Task BuildAsync_RapportelaProgressionJusquATerminaison()
    {
        CreateSourceIsoWithBoot();
        var project = CreateProject(_outputIsoPath);
        var builder = new IsoBuilder(new FakeBootAnimationGenerator());
        var reports = new List<int>();
        var progress = new Progress<AndroidTvPcIsoBuilder.Application.Interfaces.BuildProgress>(p => reports.Add(p.PercentComplete));

        await builder.BuildAsync(project, _sourceIsoPath, progress: progress);

        Assert.IsTrue(reports.Count > 0);
        Assert.IsTrue(reports.Max() >= 80);
    }

    [TestMethod]
    public async Task BuildAsync_SansAppsNiAnimation_NAjoutePasDeScriptDeDemarrage()
    {
        CreateSourceIsoWithBoot();
        var project = CreateProject(_outputIsoPath);
        project.BootAnimation.Enabled = false;
        var builder = new IsoBuilder(new FakeBootAnimationGenerator());

        await builder.BuildAsync(project, _sourceIsoPath);

        await using var outputStream = File.OpenRead(_outputIsoPath);
        var reader = new CDReader(outputStream, joliet: true);
        Assert.IsFalse(reader.DirectoryExists("SCRIPTS"));
    }

    [TestMethod]
    public async Task BuildAsync_AvecApps_AjouteLeScriptDeDemarrageAvecFinsDeLigneLf()
    {
        CreateSourceIsoWithBoot();
        var apkPath = Path.Combine(_tempDirectory, "youtube.apk");
        await File.WriteAllBytesAsync(apkPath, new byte[] { 1, 2, 3, 4 });
        var project = CreateProject(_outputIsoPath);
        project.BootAnimation.Enabled = false;
        project.Apps.Add(new AppPackage { Name = "YouTube", SourceApkPath = apkPath });
        var builder = new IsoBuilder(new FakeBootAnimationGenerator());

        await builder.BuildAsync(project, _sourceIsoPath);

        await using var outputStream = File.OpenRead(_outputIsoPath);
        var reader = new CDReader(outputStream, joliet: true);

        // Vu depuis Linux (initrd), ce fichier apparaît en minuscules : /src/scripts/atvbuilder.
        Assert.IsTrue(reader.FileExists("SCRIPTS\\ATVBUILDER"));

        using var scriptStream = reader.OpenFile("SCRIPTS\\ATVBUILDER", FileMode.Open);
        using var scriptReader = new StreamReader(scriptStream);
        var script = scriptReader.ReadToEnd();
        Assert.IsFalse(script.Contains('\r'));
        StringAssert.Contains(script, "system/etc/user_app");
        StringAssert.Contains(script, "$atvb_src/apps");
        StringAssert.Contains(script, "$atvb_src/bootanim/bootanimation.zip");
    }

    [TestMethod]
    public async Task BuildAsync_AvecAnimationActivee_AjouteLAnimationEtLeScript()
    {
        CreateSourceIsoWithBoot();
        var project = CreateProject(_outputIsoPath);
        project.BootAnimation.Enabled = true;
        var builder = new IsoBuilder(new FakeBootAnimationGenerator());

        await builder.BuildAsync(project, _sourceIsoPath);

        await using var outputStream = File.OpenRead(_outputIsoPath);
        var reader = new CDReader(outputStream, joliet: true);
        Assert.IsTrue(reader.FileExists("BOOTANIM\\BOOTANIMATION.ZIP"));
        Assert.IsTrue(reader.FileExists("SCRIPTS\\ATVBUILDER"));
    }

    [TestMethod]
    [DataRow(@"C:\Apps\youtube.apk", "youtube.apk")]
    [DataRow(@"C:\Apps\Mon Appli (v2).APK", "Mon_Appli__v2.apk")]
    [DataRow(@"C:\Apps\Télé à la carte.apk", "Tele_a_la_carte.apk")]
    [DataRow(@"C:\Apps\ .apk", "app.apk")]
    public void GetApkImageFileName_ProduitUnNomSansEspaceAvecExtensionEnMinuscules(string sourcePath, string expected)
    {
        Assert.AreEqual(expected, IsoBuilder.GetApkImageFileName(sourcePath));
    }

    [TestMethod]
    public void GetApkImageFileName_TronqueLesNomsTropLongsPourJoliet()
    {
        var name = IsoBuilder.GetApkImageFileName(@"C:\Apps\" + new string('a', 120) + ".apk");

        Assert.IsTrue(name.Length <= 64);
        Assert.IsTrue(name.EndsWith(".apk"));
    }
}

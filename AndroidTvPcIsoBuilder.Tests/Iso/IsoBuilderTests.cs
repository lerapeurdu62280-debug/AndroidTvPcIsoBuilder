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

    /// <summary>
    /// Cas de l'ISO Google TV 14 : le loader ISOLINUX chargé par le BIOS ("No Emulation", seuls
    /// 4 secteurs de 512 octets déclarés au catalogue) n'est pas retrouvable dans l'arborescence
    /// par son LBA. Sa taille réelle ne se lit que dans sa Boot Info Table : l'image recopiée doit
    /// être complète, sinon ISOLINUX ne trouve pas la suite de son code (écran noir au boot).
    /// </summary>
    [TestMethod]
    public async Task BuildAsync_LoaderIsolinuxHorsArborescence_RecopieLeLoaderCompletViaSaBootInfoTable()
    {
        const int loaderSize = 8192;
        var loader = new byte[loaderSize];
        for (var i = 64; i < loaderSize; i++)
            loader[i] = (byte)(i * 7 % 251);

        var sourceBuilder = new CDBuilder { UseJoliet = true, VolumeIdentifier = "ANDROIDTV" };
        sourceBuilder.AddFile("KERNEL", new byte[] { 1, 2, 3 });
        sourceBuilder.SetBootImage(new MemoryStream(loader), BootDeviceEmulation.NoEmulation, 0);
        sourceBuilder.Build(_sourceIsoPath);

        // Écrit dans l'ISO source la Boot Info Table telle que mkisofs -boot-info-table la produit.
        await using (var sourceStream = File.Open(_sourceIsoPath, FileMode.Open, FileAccess.ReadWrite))
        {
            var loadRba = new BootCatalogPreserver().ReadBootCatalog(sourceStream)!.BootImages[0].OriginalLba;
            BitConverter.GetBytes(16).CopyTo(loader, 8);
            BitConverter.GetBytes(loadRba).CopyTo(loader, 12);
            BitConverter.GetBytes(loaderSize).CopyTo(loader, 16);
            sourceStream.Seek((long)loadRba * BootCatalogPreserver.SectorSize, SeekOrigin.Begin);
            sourceStream.Write(loader);
        }

        var project = CreateProject(_outputIsoPath);
        project.BootAnimation.Enabled = false;
        await new IsoBuilder(new FakeBootAnimationGenerator()).BuildAsync(project, _sourceIsoPath);

        await using var outputStream = File.OpenRead(_outputIsoPath);
        var bootImage = new BootCatalogPreserver().ReadBootCatalog(outputStream)!.BootImages[0];

        Assert.AreEqual(loaderSize, bootImage.SizeInBytes);
        Assert.AreEqual(loaderSize, BitConverter.ToInt32(bootImage.Data, 16));
        Assert.AreEqual(bootImage.OriginalLba, BitConverter.ToInt32(bootImage.Data, 12));
        CollectionAssert.AreEqual(loader[64..], bootImage.Data[64..]);
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
    public async Task BuildAsync_SansAppsNiAnimationNiLangue_NAjoutePasDeScriptDeDemarrage()
    {
        CreateSourceIsoWithBoot();
        var project = CreateProject(_outputIsoPath);
        project.BootAnimation.Enabled = false;
        project.Language = "";
        var builder = new IsoBuilder(new FakeBootAnimationGenerator());

        await builder.BuildAsync(project, _sourceIsoPath);

        await using var outputStream = File.OpenRead(_outputIsoPath);
        var reader = new CDReader(outputStream, joliet: true);
        Assert.IsFalse(reader.DirectoryExists("SCRIPTS"));
        Assert.IsFalse(reader.DirectoryExists("LOCALE"));
    }

    [TestMethod]
    public async Task BuildAsync_ModeDiagnostic_AjouteLeScriptDeReleves()
    {
        CreateSourceIsoWithBoot();
        var project = CreateProject(_outputIsoPath);
        project.BootAnimation.Enabled = false;
        project.Language = "";
        project.DiagnosticMode = true;
        var builder = new IsoBuilder(new FakeBootAnimationGenerator());

        await builder.BuildAsync(project, _sourceIsoPath);

        await using var outputStream = File.OpenRead(_outputIsoPath);
        var reader = new CDReader(outputStream, joliet: true);
        Assert.IsTrue(reader.FileExists("SCRIPTS\\ATVBUILDER"));
        using var diagStream = reader.OpenFile("DIAG\\ATVDIAG.SH", FileMode.Open);
        using var diagReader = new StreamReader(diagStream);
        var diag = diagReader.ReadToEnd();
        StringAssert.Contains(diag, "ATVLOGS");
        Assert.IsFalse(diag.Contains('\r'));
    }

    [TestMethod]
    public async Task BuildAsync_SourceDejaGeneree_RefaitLesAjoutsSansDoublon()
    {
        CreateSourceIsoWithBoot();
        var firstProject = CreateProject(_outputIsoPath);
        firstProject.BootAnimation.Enabled = false;
        firstProject.Language = "fr-FR";
        firstProject.DiagnosticMode = true;
        var builder = new IsoBuilder(new FakeBootAnimationGenerator());
        await builder.BuildAsync(firstProject, _sourceIsoPath);

        var secondOutput = Path.ChangeExtension(_outputIsoPath, ".2.iso");
        try
        {
            var secondProject = CreateProject(secondOutput);
            secondProject.BootAnimation.Enabled = false;
            secondProject.Language = "en-GB";

            await builder.BuildAsync(secondProject, _outputIsoPath);

            await using var outputStream = File.OpenRead(secondOutput);
            var reader = new CDReader(outputStream, joliet: true);
            Assert.IsTrue(reader.FileExists("KERNEL"));
            Assert.IsTrue(reader.FileExists("SCRIPTS\\ATVBUILDER"));
            Assert.IsFalse(reader.DirectoryExists("DIAG"));
            using var propStream = reader.OpenFile("LOCALE\\LOCALE.PROP", FileMode.Open);
            using var propReader = new StreamReader(propStream);
            StringAssert.Contains(propReader.ReadToEnd(), "persist.sys.locale=en-GB");
        }
        finally
        {
            File.Delete(secondOutput);
        }
    }

    [TestMethod]
    public async Task BuildAsync_EnFrancais_AjouteLangueFuseauClavierAzertyEtScript()
    {
        CreateSourceIsoWithBoot();
        var project = CreateProject(_outputIsoPath);
        project.BootAnimation.Enabled = false;
        project.Language = "fr_fr";
        var builder = new IsoBuilder(new FakeBootAnimationGenerator());

        await builder.BuildAsync(project, _sourceIsoPath);

        await using var outputStream = File.OpenRead(_outputIsoPath);
        var reader = new CDReader(outputStream, joliet: true);
        Assert.IsTrue(reader.FileExists("SCRIPTS\\ATVBUILDER"));

        using (var propStream = reader.OpenFile("LOCALE\\LOCALE.PROP", FileMode.Open))
        using (var propReader = new StreamReader(propStream))
            Assert.AreEqual("persist.sys.locale=fr-FR\npersist.sys.timezone=Europe/Paris\n", propReader.ReadToEnd());

        using var kcmStream = reader.OpenFile("LOCALE\\GENERIC.KCM", FileMode.Open);
        using var kcmReader = new StreamReader(kcmStream);
        var kcm = kcmReader.ReadToEnd();
        StringAssert.Contains(kcm, "type FULL");
        StringAssert.Contains(kcm, "map key 16 A");
    }

    [TestMethod]
    public async Task BuildAsync_LangueSansDisposition_AjouteLaLangueSansClavier()
    {
        CreateSourceIsoWithBoot();
        var project = CreateProject(_outputIsoPath);
        project.BootAnimation.Enabled = false;
        project.Language = "en-US";
        var builder = new IsoBuilder(new FakeBootAnimationGenerator());

        await builder.BuildAsync(project, _sourceIsoPath);

        await using var outputStream = File.OpenRead(_outputIsoPath);
        var reader = new CDReader(outputStream, joliet: true);
        Assert.IsTrue(reader.FileExists("LOCALE\\LOCALE.PROP"));
        Assert.IsFalse(reader.FileExists("LOCALE\\GENERIC.KCM"));
    }

    [TestMethod]
    public async Task BuildAsync_AvecServicesGoogle_AjouteUneImageSquashfsLisibleEtLeScript()
    {
        CreateSourceIsoWithBoot();
        var project = CreateProject(_outputIsoPath);
        project.BootAnimation.Enabled = false;
        project.Language = "";
        project.GoogleServices = new GoogleServicesConfig { Enabled = true, DonorIsoPath = "donneuse.iso" };
        var builder = new IsoBuilder(new FakeBootAnimationGenerator(), new FakeGoogleServicesExtractor());

        await builder.BuildAsync(project, _sourceIsoPath);

        await using var outputStream = File.OpenRead(_outputIsoPath);
        var reader = new CDReader(outputStream, joliet: true);
        Assert.IsTrue(reader.FileExists("SCRIPTS\\ATVBUILDER"));
        using var sfsStream = reader.OpenFile("GAPPS.SFS", FileMode.Open);
        var squash = new DiscUtils.SquashFs.SquashFileSystemReader(sfsStream);
        using var apk = squash.OpenFile("product\\priv-app\\PrebuiltGmsCorePano\\PrebuiltGmsCorePano.apk", FileMode.Open);
        Assert.AreEqual(3, apk.Length);
    }

    [TestMethod]
    public async Task BuildAsync_ServicesGoogleDesactives_NAppellePasLExtracteur()
    {
        CreateSourceIsoWithBoot();
        var project = CreateProject(_outputIsoPath);
        project.GoogleServices = new GoogleServicesConfig { Enabled = false, DonorIsoPath = "donneuse.iso" };
        var extractor = new FakeGoogleServicesExtractor();
        var builder = new IsoBuilder(new FakeBootAnimationGenerator(), extractor);

        await builder.BuildAsync(project, _sourceIsoPath);

        Assert.AreEqual(0, extractor.CallCount);
        await using var outputStream = File.OpenRead(_outputIsoPath);
        Assert.IsFalse(new CDReader(outputStream, joliet: true).FileExists("GAPPS.SFS"));
    }

    [TestMethod]
    [DataRow("fr-FR", "fr-FR")]
    [DataRow(" FR_be ", "fr-BE")]
    [DataRow("fr", null)]
    [DataRow("fr-FR\npersist.x=1", null)]
    [DataRow(null, null)]
    public void NormalizeLocale_NAccepteQueLaFormeLangueRegion(string? input, string? expected)
    {
        Assert.AreEqual(expected, IsoBuilder.NormalizeLocale(input));
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
    public async Task BuildAsync_AvecAnimationActivee_AjouteLeProgrammeDeLogoEtSonImage()
    {
        CreateSourceIsoWithBoot();
        var project = CreateProject(_outputIsoPath);
        var builder = new IsoBuilder(new FakeBootAnimationGenerator());

        await builder.BuildAsync(project, _sourceIsoPath);

        await using var outputStream = File.OpenRead(_outputIsoPath);
        var reader = new CDReader(outputStream, joliet: true);
        Assert.IsTrue(reader.FileExists("BOOTANIM\\SPLASH.ATVS"));
        Assert.IsTrue(reader.FileExists("BOOTANIM\\ATVSPLASH"));

        using var programStream = reader.OpenFile("BOOTANIM\\ATVSPLASH", FileMode.Open);
        var magic = new byte[4];
        programStream.ReadExactly(magic);
        CollectionAssert.AreEqual(new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F' }, magic, "atvsplash doit être un exécutable Linux (ELF).");

        using var scriptStream = reader.OpenFile("SCRIPTS\\ATVBUILDER", FileMode.Open);
        StringAssert.Contains(new StreamReader(scriptStream).ReadToEnd(), "$atvb_src/bootanim/atvsplash");
    }

    private const string IsolinuxMenu = """
        default vesamenu.c32
        timeout 600
        menu background GTV.png

        label Live
        	menu label Google TV 14 Kernel 6.1
        	kernel /kernel
        	append initrd=/initrd.img quiet androidboot.enable_console=1 SRC= DATA=

        label Local
        	menu label Boot from local drive
        	kernel chain.c32
        	append hd0
        """;

    private void CreateSourceIsoWithBootMenus()
    {
        var builder = new CDBuilder { UseJoliet = true, VolumeIdentifier = "ANDROIDTV" };
        builder.AddFile("KERNEL", new byte[16]);
        builder.AddFile("syslinux.cfg", System.Text.Encoding.ASCII.GetBytes("DEFAULT loadconfig\nLABEL loadconfig\n  CONFIG /isolinux/syslinux.cfg\n"));
        builder.AddDirectory("ISOLINUX");
        builder.AddFile("ISOLINUX\\ISOLINUX.BIN", new byte[2048]);
        builder.AddFile("ISOLINUX\\syslinux.cfg", System.Text.Encoding.ASCII.GetBytes(IsolinuxMenu));
        builder.AddDirectory("boot\\grub");
        builder.AddFile("boot\\grub\\grub.cfg", System.Text.Encoding.ASCII.GetBytes("set timeout=60\nsource /efi/boot/android.cfg\n"));
        builder.SetBootImage(new MemoryStream(BootImageBytes), BootDeviceEmulation.Diskette1440KiB, 0);
        builder.Build(_sourceIsoPath);
    }

    private static string ReadText(CDReader reader, string path)
    {
        using var stream = reader.OpenFile(path, FileMode.Open);
        return new StreamReader(stream).ReadToEnd();
    }

    [TestMethod]
    public async Task BuildAsync_AvecAnimation_RemplaceLesMenusParUnDemarrageDirectSilencieux()
    {
        CreateSourceIsoWithBootMenus();
        var project = CreateProject(_outputIsoPath);
        var builder = new IsoBuilder(new FakeBootAnimationGenerator());

        await builder.BuildAsync(project, _sourceIsoPath);

        await using var outputStream = File.OpenRead(_outputIsoPath);
        var reader = new CDReader(outputStream, joliet: true);

        var isolinux = ReadText(reader, "ISOLINUX\\SYSLINUX.CFG");
        StringAssert.Contains(isolinux, "timeout 0");
        StringAssert.Contains(isolinux, "kernel /kernel");
        StringAssert.Contains(isolinux, "append initrd=/initrd.img androidboot.enable_console=1 SRC= DATA= quiet loglevel=0");
        Assert.IsFalse(isolinux.Contains("vesamenu"), "Le menu graphique doit disparaître.");

        // Le syslinux.cfg racine ne fait que charger l'autre : il reste tel quel.
        StringAssert.Contains(ReadText(reader, "SYSLINUX.CFG"), "CONFIG /isolinux/syslinux.cfg");

        var grub = ReadText(reader, "BOOT\\GRUB\\GRUB.CFG");
        StringAssert.Contains(grub, "set timeout=0");
        StringAssert.Contains(grub, "linux /kernel androidboot.enable_console=1");
        StringAssert.Contains(grub, "initrd /initrd.img");
    }

    /// <summary>ISO démarrée par GRUB seul, comme LineageOS TV x86 (menu BlissOS, pas d'ISOLINUX).</summary>
    private void CreateSourceIsoWithGrubOnly()
    {
        var builder = new CDBuilder { UseJoliet = true, VolumeIdentifier = "ANDROIDTV" };
        builder.AddFile("kernel", new byte[16]);
        builder.AddDirectory("boot\\grub");
        builder.AddFile("boot\\grub\\grub.cfg", System.Text.Encoding.ASCII.GetBytes(SilentBootConfigTests.LineageOsGrubConfig.Replace("\r\n", "\n")));
        builder.SetBootImage(new MemoryStream(BootImageBytes), BootDeviceEmulation.Diskette1440KiB, 0);
        builder.Build(_sourceIsoPath);
    }

    [TestMethod]
    public async Task BuildAsync_IsoGrubSeul_RemplaceLeMenuGrubParUnDemarrageDirect()
    {
        CreateSourceIsoWithGrubOnly();
        var builder = new IsoBuilder(new FakeBootAnimationGenerator());

        await builder.BuildAsync(CreateProject(_outputIsoPath), _sourceIsoPath);

        await using var outputStream = File.OpenRead(_outputIsoPath);
        var grub = ReadText(new CDReader(outputStream, joliet: true), "BOOT\\GRUB\\GRUB.CFG");
        StringAssert.Contains(grub, "set timeout=0");
        StringAssert.Contains(grub, "linux /kernel root=/dev/ram0 androidboot.live=true ROOT=LABEL=LineageOS_20260331 quiet");
        StringAssert.Contains(grub, "initrd /initrd.img");
        Assert.IsFalse(grub.Contains("functions.cfg"), "Le menu d'origine ne doit plus être chargé.");
    }

    [TestMethod]
    public async Task BuildAsync_AvecCatalogueDeBoot_ConserveLeDescripteurJoliet()
    {
        CreateSourceIsoWithGrubOnly();
        var builder = new IsoBuilder(new FakeBootAnimationGenerator());

        await builder.BuildAsync(CreateProject(_outputIsoPath), _sourceIsoPath);

        var descriptorTypes = new List<byte>();
        await using (var stream = File.OpenRead(_outputIsoPath))
        {
            var sector = new byte[2048];
            for (var i = 16; ; i++)
            {
                stream.Seek(i * 2048L, SeekOrigin.Begin);
                stream.ReadExactly(sector);
                descriptorTypes.Add(sector[0]);
                if (sector[0] == 255)
                    break;
            }
        }
        CollectionAssert.Contains(descriptorTypes, (byte)0, "Boot Record El Torito absent.");
        CollectionAssert.Contains(descriptorTypes, (byte)2, "Descripteur Joliet écrasé : GRUB ne trouverait plus boot/grub/i386-pc.");

        await using var outputStream = File.OpenRead(_outputIsoPath);
        Assert.IsTrue(new CDReader(outputStream, joliet: true).DirectoryExists("boot\\grub"), "Les noms Joliet doivent rester lisibles.");
    }

    /// <summary>GRUB compare les noms Joliet tels quels : "kernel." ne correspond pas à "/kernel".</summary>
    [TestMethod]
    public async Task BuildAsync_FichierSansExtension_NomJolietSansPointFinal()
    {
        CreateSourceIsoWithGrubOnly();
        var builder = new IsoBuilder(new FakeBootAnimationGenerator());

        await builder.BuildAsync(CreateProject(_outputIsoPath), _sourceIsoPath);

        await using var outputStream = File.OpenRead(_outputIsoPath);
        var names = new CDReader(outputStream, joliet: true).Root.GetFiles().Select(f => f.Name).ToList();
        CollectionAssert.Contains(names, "kernel");
        Assert.IsFalse(names.Any(n => n.EndsWith('.')), string.Join(", ", names));
    }

    [TestMethod]
    public async Task BuildAsync_ConserveLeNomEtLesDatesDuVolumeSource()
    {
        CreateSourceIsoWithBoot();
        // Nom hors "d-characters" (minuscules, point), comme "LineageOS_21.0" : écrit en binaire.
        byte[] sourceDates;
        await using (var source = File.Open(_sourceIsoPath, FileMode.Open, FileAccess.ReadWrite))
        {
            var dates = Enumerable.Range(0, 68).Select(i => (byte)('0' + i % 10)).ToArray();
            VolumeIdentityPreserver.Apply(source, new VolumeIdentity("LineageOS_21.0", dates));
            sourceDates = VolumeIdentityPreserver.Read(source)!.Dates;
        }
        var builder = new IsoBuilder(new FakeBootAnimationGenerator());

        await builder.BuildAsync(CreateProject(_outputIsoPath), _sourceIsoPath);

        await using var outputStream = File.OpenRead(_outputIsoPath);
        var identity = VolumeIdentityPreserver.Read(outputStream);
        Assert.IsNotNull(identity);
        Assert.AreEqual("LineageOS_21.0", identity.Label);
        CollectionAssert.AreEqual(sourceDates, identity.Dates);
        Assert.AreEqual("LineageOS_21.0", new CDReader(outputStream, joliet: true).VolumeLabel, "Le nom Joliet doit suivre.");
    }

    [TestMethod]
    public async Task BuildAsync_SansAnimation_ConserveLesMenusDOrigine()
    {
        CreateSourceIsoWithBootMenus();
        var project = CreateProject(_outputIsoPath);
        project.BootAnimation.Enabled = false;
        var builder = new IsoBuilder(new FakeBootAnimationGenerator());

        await builder.BuildAsync(project, _sourceIsoPath);

        await using var outputStream = File.OpenRead(_outputIsoPath);
        var reader = new CDReader(outputStream, joliet: true);
        StringAssert.Contains(ReadText(reader, "ISOLINUX\\SYSLINUX.CFG"), "vesamenu.c32");
        StringAssert.Contains(ReadText(reader, "BOOT\\GRUB\\GRUB.CFG"), "set timeout=60");
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

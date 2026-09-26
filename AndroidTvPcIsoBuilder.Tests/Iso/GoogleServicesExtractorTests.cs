using AndroidTvPcIsoBuilder.Domain.Entities;
using AndroidTvPcIsoBuilder.Infrastructure.Iso;
using DiscUtils.Iso9660;
using DiscUtils.SquashFs;

namespace AndroidTvPcIsoBuilder.Tests.Iso;

[TestClass]
public class GoogleServicesExtractorTests
{
    private string _tempDirectory = null!;
    private string _outputDirectory = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "GoogleServicesExtractorTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDirectory);
        _outputDirectory = Path.Combine(_tempDirectory, "out");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    /// <summary>ISO donneuse minimale : system.sfs contenant directement une arborescence « system-as-root ».</summary>
    private string CreateDonorIso(params string[] systemFiles)
    {
        var squash = new SquashFileSystemBuilder();
        squash.AddFile("system\\build.prop", "ro.build.version.sdk=34"u8.ToArray());
        foreach (var file in systemFiles)
            squash.AddFile("system\\" + file, new byte[] { 1, 2, 3 });
        using var sfs = new MemoryStream();
        squash.Build(sfs);

        var iso = new CDBuilder { UseJoliet = true };
        iso.AddFile("system.sfs", sfs.ToArray());
        var path = Path.Combine(_tempDirectory, "donor.iso");
        iso.Build(path);
        return path;
    }

    [TestMethod]
    public async Task ExtractAsync_NeReprendQueLesComposantsGoogle()
    {
        var donor = CreateDonorIso(
            "product\\priv-app\\PrebuiltGmsCorePano\\PrebuiltGmsCorePano.apk",
            "product\\priv-app\\PrebuiltGmsCorePano\\oat\\x86_64\\PrebuiltGmsCorePano.odex",
            "product\\priv-app\\KatnissPrebuilt\\KatnissPrebuilt.apk",
            "product\\priv-app\\TVLauncherX\\TVLauncherX.apk",
            "product\\app\\Aptoide\\Aptoide.apk",
            "system_ext\\priv-app\\GoogleServicesFramework\\GoogleServicesFramework.apk",
            "product\\etc\\permissions\\privapp-permissions-google-product.xml",
            "product\\etc\\permissions\\privapp-permissions-atv-product.xml",
            "product\\etc\\permissions\\privapp-permissions-lineage-atv.xml",
            "product\\etc\\sysconfig\\google.xml",
            "product\\etc\\sysconfig\\pixel_experience_2020.xml",
            "product\\etc\\default-permissions\\google-default-permissions.xml",
            "etc\\permissions\\privapp-permissions-google-system.xml",
            "etc\\user_app\\KernelSU.apk");

        var result = await new GoogleServicesExtractor().ExtractAsync(new GoogleServicesConfig { Enabled = true, DonorIsoPath = donor }, _outputDirectory);

        Assert.IsTrue(result.IsSuccess, string.Join(" ", result.Errors));
        var files = Directory.GetFiles(_outputDirectory, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(_outputDirectory, f).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
        CollectionAssert.AreEqual(new[]
        {
            "etc/permissions/privapp-permissions-google-system.xml",
            "product/etc/default-permissions/google-default-permissions.xml",
            "product/etc/permissions/privapp-permissions-atv-product.xml",
            "product/etc/permissions/privapp-permissions-google-product.xml",
            "product/etc/sysconfig/google.xml",
            "product/priv-app/KatnissPrebuilt/KatnissPrebuilt.apk",
            "product/priv-app/PrebuiltGmsCorePano/PrebuiltGmsCorePano.apk",
            "system_ext/priv-app/GoogleServicesFramework/GoogleServicesFramework.apk",
        }, files);
    }

    [TestMethod]
    public async Task ExtractAsync_SansGmsCoreDansLaDonneuse_Echoue()
    {
        var donor = CreateDonorIso("product\\app\\Aptoide\\Aptoide.apk");

        var result = await new GoogleServicesExtractor().ExtractAsync(new GoogleServicesConfig { Enabled = true, DonorIsoPath = donor }, _outputDirectory);

        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public async Task ExtractAsync_AvecPlayStore_LePlaceEnApplicationPrivilegiee()
    {
        var donor = CreateDonorIso("product\\priv-app\\PrebuiltGmsCorePano\\PrebuiltGmsCorePano.apk");
        var playStore = Path.Combine(_tempDirectory, "play-store-tv.apk");
        await File.WriteAllBytesAsync(playStore, new byte[] { (byte)'P', (byte)'K', 3, 4, 0 });

        var result = await new GoogleServicesExtractor().ExtractAsync(
            new GoogleServicesConfig { Enabled = true, DonorIsoPath = donor, PlayStoreApkPath = playStore }, _outputDirectory);

        Assert.IsTrue(result.IsSuccess, string.Join(" ", result.Errors));
        Assert.IsTrue(File.Exists(Path.Combine(_outputDirectory, "product", "priv-app", "Phonesky", "Phonesky.apk")));
    }

    [TestMethod]
    public async Task ExtractAsync_PlayStoreQuiNEstPasUnApk_Echoue()
    {
        var notApk = Path.Combine(_tempDirectory, "faux.apk");
        await File.WriteAllTextAsync(notApk, "pas un zip");

        var result = await new GoogleServicesExtractor().ExtractAsync(
            new GoogleServicesConfig { Enabled = true, PlayStoreApkPath = notApk }, _outputDirectory);

        Assert.IsFalse(result.IsSuccess);
    }
}

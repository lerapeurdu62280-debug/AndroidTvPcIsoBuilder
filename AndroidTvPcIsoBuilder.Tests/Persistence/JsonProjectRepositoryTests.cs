using AndroidTvPcIsoBuilder.Domain.Entities;
using AndroidTvPcIsoBuilder.Domain.Enums;
using AndroidTvPcIsoBuilder.Infrastructure.Persistence;

namespace AndroidTvPcIsoBuilder.Tests.Persistence;

[TestClass]
public class JsonProjectRepositoryTests
{
    private string _tempDirectory = null!;
    private JsonProjectRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "AndroidTvPcIsoBuilderTests_" + Guid.NewGuid());
        _repository = new JsonProjectRepository(_tempDirectory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    private static AndroidTvProject CreateProject() => new()
    {
        Name = "Projet Test",
        OutputIsoPath = "C:\\Output\\out.iso",
        SourceIso = new SourceIsoSelection
        {
            SourceId = "lineageos-tv-x86-21.0",
            DisplayName = "LineageOS TV 21.0 (x86)",
            LocalPath = "C:\\Sources\\lineageos-tv.iso",
            Sha256 = "abc123"
        },
        BootMode = BootMode.Uefi,
        BootAnimation = new BootAnimationConfig
        {
            Enabled = true,
            SourceImagePath = "C:\\Logos\\logo.png",
            FrameRate = 24,
            DurationSeconds = 5
        },
        DriverSelection = new WifiBluetoothDriverSelection
        {
            EmbeddedDriverPackEnabled = true,
            AutoDetectFirstBootEnabled = false,
            SelectedChipsetVendorIds = { "realtek", "broadcom" }
        },
        Apps = { new AppPackage { Name = "App1", SourceApkPath = "C:\\Apps\\app1.apk" } }
    };

    [TestMethod]
    public async Task SaveAsync_PuisGetByIdAsync_RetourneLeProjetIdentique()
    {
        var project = CreateProject();

        await _repository.SaveAsync(project);
        var loaded = await _repository.GetByIdAsync(project.Id);

        Assert.IsNotNull(loaded);
        Assert.AreEqual(project.Name, loaded.Name);
        Assert.AreEqual(1, loaded.Apps.Count);
        Assert.AreEqual("App1", loaded.Apps[0].Name);

        Assert.AreEqual(project.SourceIso.SourceId, loaded.SourceIso.SourceId);
        Assert.AreEqual(project.SourceIso.DisplayName, loaded.SourceIso.DisplayName);
        Assert.AreEqual(project.SourceIso.LocalPath, loaded.SourceIso.LocalPath);
        Assert.AreEqual(project.SourceIso.Sha256, loaded.SourceIso.Sha256);
        Assert.AreEqual(project.BootMode, loaded.BootMode);

        Assert.AreEqual(project.BootAnimation.Enabled, loaded.BootAnimation.Enabled);
        Assert.AreEqual(project.BootAnimation.SourceImagePath, loaded.BootAnimation.SourceImagePath);
        Assert.AreEqual(project.BootAnimation.FrameRate, loaded.BootAnimation.FrameRate);
        Assert.AreEqual(project.BootAnimation.DurationSeconds, loaded.BootAnimation.DurationSeconds);

        Assert.AreEqual(project.DriverSelection.EmbeddedDriverPackEnabled, loaded.DriverSelection.EmbeddedDriverPackEnabled);
        Assert.AreEqual(project.DriverSelection.AutoDetectFirstBootEnabled, loaded.DriverSelection.AutoDetectFirstBootEnabled);
        CollectionAssert.AreEqual(project.DriverSelection.SelectedChipsetVendorIds, loaded.DriverSelection.SelectedChipsetVendorIds);
    }

    [TestMethod]
    public async Task GetByIdAsync_ProjetInexistant_RetourneNull()
    {
        var loaded = await _repository.GetByIdAsync(Guid.NewGuid());

        Assert.IsNull(loaded);
    }

    [TestMethod]
    public async Task SaveAsync_ProjetExistant_EcraseLaVersionPrecedente()
    {
        var project = CreateProject();
        await _repository.SaveAsync(project);

        project.Name = "Nom modifié";
        await _repository.SaveAsync(project);

        var loaded = await _repository.GetByIdAsync(project.Id);
        Assert.AreEqual("Nom modifié", loaded!.Name);
    }

    [TestMethod]
    public async Task GetAllAsync_RetourneTousLesProjetsSauvegardes()
    {
        var project1 = CreateProject();
        var project2 = CreateProject();

        await _repository.SaveAsync(project1);
        await _repository.SaveAsync(project2);

        var all = await _repository.GetAllAsync();

        Assert.AreEqual(2, all.Count);
    }

    [TestMethod]
    public async Task DeleteAsync_ProjetExistant_LeSupprimeDuDisque()
    {
        var project = CreateProject();
        await _repository.SaveAsync(project);

        await _repository.DeleteAsync(project.Id);

        var loaded = await _repository.GetByIdAsync(project.Id);
        Assert.IsNull(loaded);
    }

    [TestMethod]
    public async Task DeleteAsync_ProjetInexistant_NeLeveAucuneException()
    {
        await _repository.DeleteAsync(Guid.NewGuid());
    }
}

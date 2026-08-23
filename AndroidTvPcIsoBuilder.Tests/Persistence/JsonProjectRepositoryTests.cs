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
        SourcePath = "C:\\Source",
        OutputIsoPath = "C:\\Output\\out.iso",
        BaseSystem = BaseSystemType.LineageOsTvX86,
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
        Assert.AreEqual(project.BaseSystem, loaded.BaseSystem);
        Assert.AreEqual(1, loaded.Apps.Count);
        Assert.AreEqual("App1", loaded.Apps[0].Name);
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

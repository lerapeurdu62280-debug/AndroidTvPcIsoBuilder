using AndroidTvPcIsoBuilder.Application.Services;
using AndroidTvPcIsoBuilder.Tests.TestDoubles;

namespace AndroidTvPcIsoBuilder.Tests.Services;

[TestClass]
public class AppPackageServiceTests
{
    private InMemoryProjectRepository _repository = null!;
    private FakeFileSystem _fileSystem = null!;
    private AppPackageService _service = null!;
    private Guid _projectId;

    [TestInitialize]
    public async Task Setup()
    {
        _repository = new InMemoryProjectRepository();
        _fileSystem = new FakeFileSystem();
        _service = new AppPackageService(_repository, _fileSystem);

        var projectService = new ProjectService(_repository);
        var project = await projectService.CreateProjectAsync("Projet", "C:\\Source", "C:\\Output\\out.iso");
        _projectId = project.Value.Id;
    }

    [TestMethod]
    public async Task AddAppAsync_AvecApkExistant_AjouteLApplication()
    {
        _fileSystem.AddFile("C:\\Apps\\youtube.apk");

        var result = await _service.AddAppAsync(_projectId, "YouTube", "C:\\Apps\\youtube.apk");

        Assert.IsTrue(result.IsSuccess);
        var project = await _repository.GetByIdAsync(_projectId);
        Assert.AreEqual(1, project!.Apps.Count);
    }

    [TestMethod]
    public async Task AddAppAsync_ApkIntrouvable_RetourneUnEchec()
    {
        var result = await _service.AddAppAsync(_projectId, "YouTube", "C:\\Apps\\inexistant.apk");

        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public async Task AddAppAsync_ExtensionInvalide_RetourneUnEchec()
    {
        _fileSystem.AddFile("C:\\Apps\\fichier.txt");

        var result = await _service.AddAppAsync(_projectId, "Faux", "C:\\Apps\\fichier.txt");

        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public async Task AddAppAsync_ApkDejaPresent_RetourneUnEchec()
    {
        _fileSystem.AddFile("C:\\Apps\\youtube.apk");
        await _service.AddAppAsync(_projectId, "YouTube", "C:\\Apps\\youtube.apk");

        var result = await _service.AddAppAsync(_projectId, "YouTube (bis)", "C:\\Apps\\youtube.apk");

        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public async Task RemoveAppAsync_ApplicationExistante_LaSupprimeDuProjet()
    {
        _fileSystem.AddFile("C:\\Apps\\youtube.apk");
        await _service.AddAppAsync(_projectId, "YouTube", "C:\\Apps\\youtube.apk");

        var result = await _service.RemoveAppAsync(_projectId, "C:\\Apps\\youtube.apk");

        Assert.IsTrue(result.IsSuccess);
        var project = await _repository.GetByIdAsync(_projectId);
        Assert.AreEqual(0, project!.Apps.Count);
    }

    [TestMethod]
    public async Task RemoveAppAsync_ApplicationInexistante_RetourneUnEchec()
    {
        var result = await _service.RemoveAppAsync(_projectId, "C:\\Apps\\inconnue.apk");

        Assert.IsFalse(result.IsSuccess);
    }
}

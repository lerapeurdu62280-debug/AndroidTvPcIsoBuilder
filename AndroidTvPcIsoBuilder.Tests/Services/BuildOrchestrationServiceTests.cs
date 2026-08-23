using AndroidTvPcIsoBuilder.Application.Services;
using AndroidTvPcIsoBuilder.Application.Validation;
using AndroidTvPcIsoBuilder.Tests.TestDoubles;

namespace AndroidTvPcIsoBuilder.Tests.Services;

[TestClass]
public class BuildOrchestrationServiceTests
{
    private InMemoryProjectRepository _repository = null!;
    private FakeFileSystem _fileSystem = null!;
    private FakeIsoBuilder _isoBuilder = null!;
    private BuildOrchestrationService _orchestrator = null!;

    [TestInitialize]
    public void Setup()
    {
        _repository = new InMemoryProjectRepository();
        _fileSystem = new FakeFileSystem();
        _isoBuilder = new FakeIsoBuilder();
        _orchestrator = new BuildOrchestrationService(_repository, _isoBuilder, new ProjectValidator(_fileSystem), _fileSystem);
    }

    [TestMethod]
    public async Task BuildAsync_ProjetInexistant_RetourneUnEchecSansAppelerLeBuilder()
    {
        var result = await _orchestrator.BuildAsync(Guid.NewGuid());

        Assert.IsFalse(result.IsSuccess);
        Assert.IsFalse(_isoBuilder.WasCalled);
    }

    [TestMethod]
    public async Task BuildAsync_ProjetInvalide_RetourneUnEchecSansAppelerLeBuilder()
    {
        var projectService = new ProjectService(_repository);
        var project = await projectService.CreateProjectAsync("Projet", "C:\\Source\\Inexistant", "C:\\Output\\out.iso");

        var result = await _orchestrator.BuildAsync(project.Value.Id);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsFalse(_isoBuilder.WasCalled);
    }

    [TestMethod]
    public async Task BuildAsync_ProjetValide_AppelleLeBuilderEtReussit()
    {
        _fileSystem.AddFile("C:\\Source\\android.iso");
        var projectService = new ProjectService(_repository);
        var project = await projectService.CreateProjectAsync("Projet", "C:\\Source\\android.iso", "C:\\Output\\out.iso");

        var result = await _orchestrator.BuildAsync(project.Value.Id);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(_isoBuilder.WasCalled);
    }

    [TestMethod]
    public async Task BuildAsync_LeBuilderLeveUneException_RetourneUnEchec()
    {
        _fileSystem.AddFile("C:\\Source\\android.iso");
        var projectService = new ProjectService(_repository);
        var project = await projectService.CreateProjectAsync("Projet", "C:\\Source\\android.iso", "C:\\Output\\out.iso");
        _isoBuilder.ExceptionToThrow = new InvalidOperationException("Erreur disque");

        var result = await _orchestrator.BuildAsync(project.Value.Id);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsTrue(result.Errors.Any(e => e.Contains("Erreur disque")));
    }

    [TestMethod]
    public async Task PrepareAndValidateAsync_ProjetValide_RetourneLeProjet()
    {
        _fileSystem.AddFile("C:\\Source\\android.iso");
        var projectService = new ProjectService(_repository);
        var project = await projectService.CreateProjectAsync("Projet", "C:\\Source\\android.iso", "C:\\Output\\out.iso");

        var result = await _orchestrator.PrepareAndValidateAsync(project.Value.Id);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(project.Value.Id, result.Value.Id);
    }
}

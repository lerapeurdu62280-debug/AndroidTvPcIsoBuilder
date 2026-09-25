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

    private const string SourceIsoPath = "C:\\Source\\android.iso";

    [TestMethod]
    public async Task BuildAsync_ProjetInexistant_RetourneUnEchecSansAppelerLeBuilder()
    {
        var result = await _orchestrator.BuildAsync(Guid.NewGuid(), SourceIsoPath);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsFalse(_isoBuilder.WasCalled);
    }

    [TestMethod]
    public async Task BuildAsync_ProjetInvalide_RetourneUnEchecSansAppelerLeBuilder()
    {
        var projectService = new ProjectService(_repository);
        var project = await projectService.CreateProjectAsync("Projet", "C:\\Output\\out.iso");
        await projectService.UpdateProjectSettingsAsync(project.Value.Id, outputIsoPath: "C:\\Output\\out.txt");

        var result = await _orchestrator.BuildAsync(project.Value.Id, SourceIsoPath);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsFalse(_isoBuilder.WasCalled);
    }

    [TestMethod]
    public async Task BuildAsync_ProjetValide_AppelleLeBuilderEtReussit()
    {
        _fileSystem.AddFile(SourceIsoPath);
        var projectService = new ProjectService(_repository);
        var project = await projectService.CreateProjectAsync("Projet", "C:\\Output\\out.iso");
        await projectService.UpdateProjectSettingsAsync(project.Value.Id, sourceIsoLocalPath: SourceIsoPath);

        var result = await _orchestrator.BuildAsync(project.Value.Id, SourceIsoPath);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(_isoBuilder.WasCalled);
    }

    [TestMethod]
    public async Task BuildAsync_LeBuilderLeveUneException_RetourneUnEchec()
    {
        _fileSystem.AddFile(SourceIsoPath);
        var projectService = new ProjectService(_repository);
        var project = await projectService.CreateProjectAsync("Projet", "C:\\Output\\out.iso");
        await projectService.UpdateProjectSettingsAsync(project.Value.Id, sourceIsoLocalPath: SourceIsoPath);
        _isoBuilder.ExceptionToThrow = new InvalidOperationException("Erreur disque");

        var result = await _orchestrator.BuildAsync(project.Value.Id, SourceIsoPath);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsTrue(result.Errors.Any(e => e.Contains("Erreur disque")));
    }

    [TestMethod]
    public async Task PrepareAndValidateAsync_ProjetValide_RetourneLeProjet()
    {
        _fileSystem.AddFile(SourceIsoPath);
        var projectService = new ProjectService(_repository);
        var project = await projectService.CreateProjectAsync("Projet", "C:\\Output\\out.iso");
        await projectService.UpdateProjectSettingsAsync(project.Value.Id, sourceIsoLocalPath: SourceIsoPath);

        var result = await _orchestrator.PrepareAndValidateAsync(project.Value.Id);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(project.Value.Id, result.Value.Id);
    }
}

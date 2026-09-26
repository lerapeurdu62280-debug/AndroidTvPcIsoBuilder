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

    [TestMethod]
    public async Task BuildAsync_AvecAptoideTv_LAjouteLeTempsDeLaGenerationSeulement()
    {
        _fileSystem.AddFile(SourceIsoPath);
        var orchestrator = new BuildOrchestrationService(_repository, _isoBuilder, new ProjectValidator(_fileSystem), _fileSystem, new FakeAptoideTvProvider());
        var projectService = new ProjectService(_repository);
        var project = await projectService.CreateProjectAsync("Projet", "C:\\Output\\out.iso");
        await projectService.UpdateProjectSettingsAsync(project.Value.Id, sourceIsoLocalPath: SourceIsoPath);
        await projectService.UpdateExtrasAsync(project.Value.Id, new() { Enabled = true }, diagnosticMode: false);

        var result = await orchestrator.BuildAsync(project.Value.Id, SourceIsoPath);

        Assert.IsTrue(result.IsSuccess, string.Join(" ", result.Errors));
        CollectionAssert.Contains(_isoBuilder.AppNamesAtBuild, "Aptoide TV");
        var saved = await _repository.GetByIdAsync(project.Value.Id);
        Assert.AreEqual(0, saved!.Apps.Count);
    }

    [TestMethod]
    public async Task BuildAsync_AptoideTvIndisponible_EchoueSansAppelerLeBuilder()
    {
        _fileSystem.AddFile(SourceIsoPath);
        var provider = new FakeAptoideTvProvider { ResultToReturn = Application.Common.Result<string>.Failure("hors ligne") };
        var orchestrator = new BuildOrchestrationService(_repository, _isoBuilder, new ProjectValidator(_fileSystem), _fileSystem, provider);
        var projectService = new ProjectService(_repository);
        var project = await projectService.CreateProjectAsync("Projet", "C:\\Output\\out.iso");
        await projectService.UpdateProjectSettingsAsync(project.Value.Id, sourceIsoLocalPath: SourceIsoPath);
        await projectService.UpdateExtrasAsync(project.Value.Id, new() { Enabled = true }, diagnosticMode: false);

        var result = await orchestrator.BuildAsync(project.Value.Id, SourceIsoPath);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsFalse(_isoBuilder.WasCalled);
    }
}

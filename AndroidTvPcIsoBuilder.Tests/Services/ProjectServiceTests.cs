using AndroidTvPcIsoBuilder.Application.Services;
using AndroidTvPcIsoBuilder.Domain.Enums;
using AndroidTvPcIsoBuilder.Tests.TestDoubles;

namespace AndroidTvPcIsoBuilder.Tests.Services;

[TestClass]
public class ProjectServiceTests
{
    private InMemoryProjectRepository _repository = null!;
    private ProjectService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _repository = new InMemoryProjectRepository();
        _service = new ProjectService(_repository);
    }

    [TestMethod]
    public async Task CreateProjectAsync_AvecDonneesValides_CreeEtPersisteLeProjet()
    {
        var result = await _service.CreateProjectAsync("MonProjet", "C:\\Output\\out.iso");

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("MonProjet", result.Value.Name);

        var persisted = await _repository.GetByIdAsync(result.Value.Id);
        Assert.IsNotNull(persisted);
    }

    [TestMethod]
    public async Task CreateProjectAsync_SansNom_RetourneUnEchec()
    {
        var result = await _service.CreateProjectAsync("", "C:\\Output\\out.iso");

        Assert.IsFalse(result.IsSuccess);
        Assert.IsTrue(result.Errors.Count > 0);
    }

    [TestMethod]
    public async Task UpdateProjectSettingsAsync_ProjetInexistant_RetourneUnEchec()
    {
        var result = await _service.UpdateProjectSettingsAsync(Guid.NewGuid(), name: "Nouveau");

        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public async Task UpdateProjectSettingsAsync_ModifieUniquementLesChampsFournis()
    {
        var created = await _service.CreateProjectAsync("Original", "C:\\Output\\out.iso");

        var updated = await _service.UpdateProjectSettingsAsync(
            created.Value.Id,
            resolution: "1280x720",
            bootMode: BootMode.Uefi);

        Assert.IsTrue(updated.IsSuccess);
        Assert.AreEqual("Original", updated.Value.Name);
        Assert.AreEqual("1280x720", updated.Value.Resolution);
        Assert.AreEqual(BootMode.Uefi, updated.Value.BootMode);
    }

    [TestMethod]
    public async Task DeleteProjectAsync_ProjetExistant_LeSupprimeDuDepot()
    {
        var created = await _service.CreateProjectAsync("AEffacer", "C:\\Output\\out.iso");

        var result = await _service.DeleteProjectAsync(created.Value.Id);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNull(await _repository.GetByIdAsync(created.Value.Id));
    }

    [TestMethod]
    public async Task GetAllProjectsAsync_RetourneTousLesProjetsCrees()
    {
        await _service.CreateProjectAsync("P1", "C:\\Output\\p1.iso");
        await _service.CreateProjectAsync("P2", "C:\\Output\\p2.iso");

        var all = await _service.GetAllProjectsAsync();

        Assert.AreEqual(2, all.Count);
    }
}

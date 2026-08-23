using AndroidTvPcIsoBuilder.Application.Validation;
using AndroidTvPcIsoBuilder.Domain.Entities;
using AndroidTvPcIsoBuilder.Tests.TestDoubles;

namespace AndroidTvPcIsoBuilder.Tests.Validation;

[TestClass]
public class ProjectValidatorTests
{
    private FakeFileSystem _fileSystem = null!;
    private ProjectValidator _validator = null!;

    [TestInitialize]
    public void Setup()
    {
        _fileSystem = new FakeFileSystem();
        _validator = new ProjectValidator(_fileSystem);
    }

    private AndroidTvProject CreateValidProject() => new()
    {
        Name = "Projet",
        SourcePath = "C:\\Source\\android.iso",
        OutputIsoPath = "C:\\Output\\out.iso",
        Resolution = "1920x1080"
    };

    [TestMethod]
    public void Validate_ProjetComplet_RetourneUnSucces()
    {
        _fileSystem.AddFile("C:\\Source\\android.iso");
        var project = CreateValidProject();

        var result = _validator.Validate(project);

        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public void Validate_CheminSourceIntrouvable_RetourneUnEchec()
    {
        var project = CreateValidProject();

        var result = _validator.Validate(project);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsTrue(result.Errors.Any(e => e.Contains("introuvable")));
    }

    [TestMethod]
    public void Validate_SortieSansExtensionIso_RetourneUnEchec()
    {
        _fileSystem.AddFile("C:\\Source\\android.iso");
        var project = CreateValidProject();
        project.OutputIsoPath = "C:\\Output\\out.txt";

        var result = _validator.Validate(project);

        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    [DataRow("1920x1080", true)]
    [DataRow("1280x720", true)]
    [DataRow("abcxdef", false)]
    [DataRow("1920", false)]
    [DataRow("0x0", false)]
    public void Validate_ResolutionVarieeSelonFormat(string resolution, bool expectedValid)
    {
        _fileSystem.AddFile("C:\\Source\\android.iso");
        var project = CreateValidProject();
        project.Resolution = resolution;

        var result = _validator.Validate(project);

        Assert.AreEqual(expectedValid, result.IsSuccess);
    }

    [TestMethod]
    public void Validate_AppAvecApkIntrouvable_RetourneUnEchec()
    {
        _fileSystem.AddFile("C:\\Source\\android.iso");
        var project = CreateValidProject();
        project.Apps.Add(new AppPackage { Name = "App", SourceApkPath = "C:\\Apps\\manquant.apk" });

        var result = _validator.Validate(project);

        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public void Validate_AppAvecApkExistant_RetourneUnSucces()
    {
        _fileSystem.AddFile("C:\\Source\\android.iso");
        _fileSystem.AddFile("C:\\Apps\\ok.apk");
        var project = CreateValidProject();
        project.Apps.Add(new AppPackage { Name = "App", SourceApkPath = "C:\\Apps\\ok.apk" });

        var result = _validator.Validate(project);

        Assert.IsTrue(result.IsSuccess);
    }
}

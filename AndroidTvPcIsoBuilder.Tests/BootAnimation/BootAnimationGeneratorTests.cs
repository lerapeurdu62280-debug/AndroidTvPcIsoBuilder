using System.IO.Compression;
using AndroidTvPcIsoBuilder.Domain.Entities;
using AndroidTvPcIsoBuilder.Infrastructure.BootAnimation;

namespace AndroidTvPcIsoBuilder.Tests.BootAnimation;

[TestClass]
public class BootAnimationGeneratorTests
{
    private string _outputDirectory = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _outputDirectory = Path.Combine(Path.GetTempPath(), $"bootanim-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_outputDirectory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_outputDirectory))
            Directory.Delete(_outputDirectory, recursive: true);
    }

    [TestMethod]
    public async Task GenerateAsync_LogoParDefaut_ProduitUnZipAvecDescTxtEtFramesPng()
    {
        var generator = new BootAnimationGenerator();
        // Pas de SourceImagePath fourni : utilise le logo vectoriel par défaut, ce qui garde
        // le test simple et rapide (pas de fichier image à préparer).
        var config = new BootAnimationConfig { FrameRate = 5, DurationSeconds = 1 };

        var result = await generator.GenerateAsync(config, _outputDirectory);

        Assert.IsTrue(result.IsSuccess, string.Join(" ", result.Errors));
        var zipPath = result.Value;
        Assert.IsTrue(File.Exists(zipPath));

        using var archive = ZipFile.OpenRead(zipPath);

        var descEntry = archive.GetEntry("desc.txt");
        Assert.IsNotNull(descEntry);

        using (var reader = new StreamReader(descEntry!.Open()))
        {
            var descContent = await reader.ReadToEndAsync();
            StringAssert.Matches(descContent, new System.Text.RegularExpressions.Regex(@"^\d+ \d+ \d+"));
            StringAssert.Contains(descContent, "p 1 0 part0");
        }

        var pngEntries = archive.Entries.Where(e => e.FullName.StartsWith("part0/") && e.FullName.EndsWith(".png")).ToList();
        Assert.IsTrue(pngEntries.Count > 0, "Le zip doit contenir au moins une frame PNG dans part0/.");
    }

    [TestMethod]
    public async Task GenerateAsync_ZipNestPasCompresse()
    {
        var generator = new BootAnimationGenerator();
        var config = new BootAnimationConfig { FrameRate = 5, DurationSeconds = 1 };

        var result = await generator.GenerateAsync(config, _outputDirectory);

        Assert.IsTrue(result.IsSuccess, string.Join(" ", result.Errors));

        using var archive = ZipFile.OpenRead(result.Value);

        Assert.IsTrue(archive.Entries.Count > 0);
        foreach (var entry in archive.Entries)
        {
            // Le format bootanimation Android exige des entrées ZIP stockées sans compression
            // (Deflate n'est pas supporté par le lecteur bootanimation d'Android). L'API
            // ZipArchiveEntry de ce SDK n'expose pas de propriété CompressionMethod : on
            // vérifie donc l'absence de compression via l'égalité taille compressée/taille
            // réelle, qui n'est vraie que pour la méthode "Stored" (Deflate réduit la taille,
            // sauf cas pathologique improbable sur des PNG/texte réels).
            Assert.AreEqual(entry.Length, entry.CompressedLength,
                $"L'entrée '{entry.FullName}' doit être stockée sans compression (CompressedLength == Length).");
        }
    }

    [TestMethod]
    public async Task GenerateAsync_NombreDeFramesCorrespondAuFrameRateFoisDuree()
    {
        var generator = new BootAnimationGenerator();
        var config = new BootAnimationConfig { FrameRate = 4, DurationSeconds = 2 };

        var result = await generator.GenerateAsync(config, _outputDirectory);

        Assert.IsTrue(result.IsSuccess, string.Join(" ", result.Errors));

        using var archive = ZipFile.OpenRead(result.Value);
        var pngEntries = archive.Entries.Where(e => e.FullName.StartsWith("part0/") && e.FullName.EndsWith(".png")).ToList();

        Assert.AreEqual(8, pngEntries.Count);
    }
}

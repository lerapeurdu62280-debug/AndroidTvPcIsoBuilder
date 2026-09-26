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
    public async Task GenerateSplashImageAsync_LogoParDefaut_ProduitUnFichierAtvsCoherent()
    {
        var generator = new BootAnimationGenerator();
        var config = new BootAnimationConfig { DurationSeconds = 4 };

        var result = await generator.GenerateSplashImageAsync(config, _outputDirectory);

        Assert.IsTrue(result.IsSuccess, string.Join(" ", result.Errors));
        var bytes = await File.ReadAllBytesAsync(result.Value);
        CollectionAssert.AreEqual("ATVS"u8.ToArray(), bytes[..4]);

        int width = BitConverter.ToUInt16(bytes, 4), height = BitConverter.ToUInt16(bytes, 6);
        Assert.IsTrue(width is > 0 and <= 900 && height is > 0 and <= 420, $"Taille inattendue {width}x{height}.");
        Assert.AreEqual(4000, BitConverter.ToUInt16(bytes, 8), "Le cycle de respiration doit suivre DurationSeconds.");
        Assert.AreEqual(1000, BitConverter.ToUInt16(bytes, 10));
        Assert.AreEqual(12 + width * height * 3, bytes.Length);
        Assert.IsTrue(bytes.Skip(12).Any(b => b > 100), "Le logo ne doit pas être entièrement noir.");
    }

    [TestMethod]
    public async Task GenerateAsync_LogoParDefaut_ProduitUnZipAvecDescTxtEtFramesPng()
    {
        var generator = new BootAnimationGenerator();
        // Pas de SourceImagePath fourni : utilise le logo Android TV embarqué par défaut.
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
            // Intro jouée une fois, puis boucle jusqu'à la fin du démarrage.
            StringAssert.Contains(descContent, "p 1 0 part0\np 0 0 part1");
        }

        Assert.IsTrue(archive.Entries.Any(e => e.FullName.StartsWith("part0/") && e.FullName.EndsWith(".png")), "Frames d'intro manquantes dans part0/.");
        Assert.IsTrue(archive.Entries.Any(e => e.FullName.StartsWith("part1/") && e.FullName.EndsWith(".png")), "Frames de boucle manquantes dans part1/.");
    }

    [TestMethod]
    public async Task GenerateAsync_LogoParDefaut_DessineLeLogoEmbarqueAuCentre()
    {
        var generator = new BootAnimationGenerator();
        var config = new BootAnimationConfig { FrameRate = 2, DurationSeconds = 1 };

        var result = await generator.GenerateAsync(config, _outputDirectory);

        Assert.IsTrue(result.IsSuccess, string.Join(" ", result.Errors));
        using var archive = ZipFile.OpenRead(result.Value);
        using var frameStream = new MemoryStream();
        using (var entryStream = archive.Entries.First(e => e.FullName.StartsWith("part1/")).Open())
            entryStream.CopyTo(frameStream);
        frameStream.Position = 0;

        var frame = new System.Windows.Media.Imaging.FormatConvertedBitmap(
            System.Windows.Media.Imaging.BitmapFrame.Create(frameStream, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad),
            System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var stride = frame.PixelWidth * 4;
        var pixels = new byte[stride * frame.PixelHeight];
        frame.CopyPixels(pixels, stride, 0);

        int BrightnessAt(int x, int y) => Math.Max(pixels[y * stride + x * 4], Math.Max(pixels[y * stride + x * 4 + 1], pixels[y * stride + x * 4 + 2]));

        // Coins noirs, et du vert franc (tête du robot) un peu au-dessus du centre.
        Assert.AreEqual(0, BrightnessAt(5, 5));
        Assert.AreEqual(0, BrightnessAt(frame.PixelWidth - 5, frame.PixelHeight - 5));
        var head = (y: frame.PixelHeight / 2 - 60, x: frame.PixelWidth / 2);
        var green = pixels[head.y * stride + head.x * 4 + 1];
        var red = pixels[head.y * stride + head.x * 4 + 2];
        Assert.IsTrue(green > 120 && green > red + 40, $"Pixel attendu vert au centre haut, trouvé G={green} R={red}.");
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
    public async Task GenerateAsync_IntroDUneSecondeEtBoucleDeLaDureeConfiguree()
    {
        var generator = new BootAnimationGenerator();
        var config = new BootAnimationConfig { FrameRate = 4, DurationSeconds = 2 };

        var result = await generator.GenerateAsync(config, _outputDirectory);

        Assert.IsTrue(result.IsSuccess, string.Join(" ", result.Errors));

        using var archive = ZipFile.OpenRead(result.Value);
        int CountFrames(string part) => archive.Entries.Count(e => e.FullName.StartsWith(part + "/") && e.FullName.EndsWith(".png"));

        Assert.AreEqual(4, CountFrames("part0")); // 1 seconde d'intro
        Assert.AreEqual(8, CountFrames("part1")); // FrameRate x DurationSeconds
    }
}

using System.IO.Compression;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AndroidTvPcIsoBuilder.Application.Common;
using AndroidTvPcIsoBuilder.Application.Interfaces;
using AndroidTvPcIsoBuilder.Domain.Entities;

namespace AndroidTvPcIsoBuilder.Infrastructure.BootAnimation;

/// <summary>
/// Génère un <c>bootanimation.zip</c> Android standard affichant un logo animé au centre, sur
/// fond noir, en deux parties (voir desc.txt) :
/// <list type="bullet">
/// <item><c>part0</c> : fondu d'entrée (1 seconde), joué une seule fois ;</item>
/// <item><c>part1</c> : "respiration" du logo (luminosité et échelle qui pulsent doucement),
/// jouée en boucle jusqu'à la fin du démarrage d'Android.</item>
/// </list>
/// Sans image fournie, le logo par défaut est le logo Android TV embarqué
/// (Assets/BootAnimation/androidtv-logo.png).
///
/// CHOIX TECHNIQUE — rendu des frames : ce projet est déjà une application WPF
/// (<c>net10.0-windows</c>, <c>UseWPF=true</c>), donc <see cref="System.Windows.Media.Imaging"/>
/// est utilisé pour dessiner chaque frame (<see cref="DrawingVisual"/> +
/// <see cref="RenderTargetBitmap"/>) et l'encoder en PNG (<see cref="PngBitmapEncoder"/>).
/// C'est l'approche la plus cohérente avec la stack existante : elle ne nécessite AUCUNE
/// nouvelle dépendance NuGet (contrairement à System.Drawing.Common, volontairement évité
/// ici car non référencé par le projet). En contrepartie, cette classe ne fonctionne que sur
/// Windows (cohérent avec le reste de l'application, qui est déjà Windows-only).
/// </summary>
public class BootAnimationGenerator : IBootAnimationGenerator
{
    private const int FrameWidth = 1280;
    private const int FrameHeight = 720;
    private const double MaxLogoWidth = 900;
    private const double MaxLogoHeight = 420;
    private const string DefaultLogoResourceName = "AndroidTvPcIsoBuilder.Infrastructure.Assets.BootAnimation.androidtv-logo.png";

    public async Task<Result<string>> GenerateAsync(BootAnimationConfig config, string outputDirectory, CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(outputDirectory);

            var workingDirectory = Path.Combine(outputDirectory, $"bootanimation-work-{Guid.NewGuid():N}");
            var introDirectory = Path.Combine(workingDirectory, "part0");
            var loopDirectory = Path.Combine(workingDirectory, "part1");
            Directory.CreateDirectory(introDirectory);
            Directory.CreateDirectory(loopDirectory);

            var logo = LoadLogo(config);

            // Fondu d'entrée : opacité 0 -> 1 et échelle 0.95 -> 1.0 sur une seconde.
            var introFrameCount = Math.Max(1, config.FrameRate);
            for (var frameIndex = 0; frameIndex < introFrameCount; frameIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var progress = (frameIndex + 1) / (double)introFrameCount;
                var eased = 1 - Math.Pow(1 - progress, 3);
                var frame = RenderFrame(logo, eased, 0.95 + 0.05 * eased);
                await SavePngAsync(frame, Path.Combine(introDirectory, $"{frameIndex:D5}.png"), cancellationToken);
            }

            // Respiration : un cycle complet (cosinus) sur DurationSeconds, qui se reboucle sans à-coup.
            var loopFrameCount = Math.Max(1, config.FrameRate * config.DurationSeconds);
            for (var frameIndex = 0; frameIndex < loopFrameCount; frameIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var wave = (1 + Math.Cos(2 * Math.PI * frameIndex / loopFrameCount)) / 2; // 1 -> 0 -> 1
                var frame = RenderFrame(logo, 0.8 + 0.2 * wave, 1.0 + 0.015 * wave);
                await SavePngAsync(frame, Path.Combine(loopDirectory, $"{frameIndex:D5}.png"), cancellationToken);
            }

            var descPath = Path.Combine(workingDirectory, "desc.txt");
            // Format standard Android bootanimation : "<width> <height> <fps>" puis une ligne par
            // partie "p <count> <pause> <folder>". part0 est jouée une fois (count=1), part1 en
            // boucle (count=0) jusqu'à la fin du démarrage : un logo figé ou un fondu qui recommence
            // en boucle donnerait l'impression d'un écran bloqué ou qui clignote.
            var descContent = $"{FrameWidth} {FrameHeight} {config.FrameRate}\np 1 0 part0\np 0 0 part1\n";
            await File.WriteAllTextAsync(descPath, descContent, cancellationToken);

            var zipPath = Path.Combine(outputDirectory, "bootanimation.zip");
            if (File.Exists(zipPath))
                File.Delete(zipPath);

            CreateUncompressedZip(workingDirectory, zipPath);

            Directory.Delete(workingDirectory, recursive: true);

            return Result<string>.Success(zipPath);
        }
        catch (Exception ex)
        {
            return Result<string>.Failure($"Échec de la génération du bootanimation : {ex.Message}");
        }
    }

    /// <summary>
    /// Produit le fichier "ATVS" lu par atvsplash (voir Assets/Splash/atvsplash.c) :
    /// "ATVS" | u16 largeur | u16 hauteur | u16 cycle_ms | u16 fondu_ms | pixels RGB.
    /// Le logo y est à la taille exacte où la bootanimation le dessine (le lecteur Android
    /// affiche les frames à l'échelle 1, centrées), déjà composé sur fond noir : la transition
    /// entre les deux animations se fait donc sans saut de taille ni de position.
    /// </summary>
    public async Task<Result<string>> GenerateSplashImageAsync(BootAnimationConfig config, string outputDirectory, CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(outputDirectory);

            var logo = LoadLogo(config);
            var (logoWidth, logoHeight) = GetLogoSize(logo);
            var width = Math.Max(1, (int)Math.Round(logoWidth));
            var height = Math.Max(1, (int)Math.Round(logoHeight));

            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));
                context.DrawImage(logo, new Rect(0, 0, width, height));
            }

            var renderTarget = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            renderTarget.Render(visual);

            var bgra = new byte[width * height * 4];
            renderTarget.CopyPixels(bgra, width * 4, 0);

            var cycleMilliseconds = Math.Clamp(config.DurationSeconds * 1000, 1, ushort.MaxValue);
            const int fadeMilliseconds = 1000; // = part0 de la bootanimation (FrameRate frames à FrameRate i/s)

            var splashPath = Path.Combine(outputDirectory, "splash.atvs");
            await using var output = new FileStream(splashPath, FileMode.Create, FileAccess.Write);
            using var writer = new BinaryWriter(output);
            writer.Write("ATVS"u8);
            writer.Write((ushort)width);
            writer.Write((ushort)height);
            writer.Write((ushort)cycleMilliseconds);
            writer.Write((ushort)fadeMilliseconds);

            var rgb = new byte[width * height * 3];
            for (int source = 0, target = 0; source < bgra.Length; source += 4, target += 3)
            {
                // Fond noir opaque dessiné en premier : les pixels sont déjà composés (alpha = 255).
                rgb[target] = bgra[source + 2];
                rgb[target + 1] = bgra[source + 1];
                rgb[target + 2] = bgra[source];
            }
            writer.Write(rgb);

            return Result<string>.Success(splashPath);
        }
        catch (Exception ex)
        {
            return Result<string>.Failure($"Échec de la génération du logo de démarrage : {ex.Message}");
        }
    }

    private static BitmapSource LoadLogo(BootAnimationConfig config) =>
        !string.IsNullOrWhiteSpace(config.SourceImagePath) && File.Exists(config.SourceImagePath)
            ? LoadSourceImage(config.SourceImagePath)
            : LoadDefaultLogo();

    /// <summary>
    /// Taille du logo dans une frame : réduit si besoin pour tenir dans
    /// <see cref="MaxLogoWidth"/> x <see cref="MaxLogoHeight"/>, jamais agrandi.
    /// </summary>
    private static (double Width, double Height) GetLogoSize(BitmapSource logo)
    {
        var ratio = Math.Min(1.0, Math.Min(MaxLogoWidth / logo.PixelWidth, MaxLogoHeight / logo.PixelHeight));
        return (logo.PixelWidth * ratio, logo.PixelHeight * ratio);
    }

    private static BitmapSource LoadSourceImage(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource LoadDefaultLogo()
    {
        using var stream = typeof(BootAnimationGenerator).Assembly.GetManifestResourceStream(DefaultLogoResourceName)
            ?? throw new InvalidOperationException($"Ressource embarquée introuvable : {DefaultLogoResourceName}");

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// Dessine une frame : fond noir, logo centré, réduit si besoin pour tenir dans
    /// <see cref="MaxLogoWidth"/> x <see cref="MaxLogoHeight"/> (jamais agrandi au-delà de sa
    /// taille d'origine), avec l'opacité et l'échelle de la frame.
    /// </summary>
    private static RenderTargetBitmap RenderFrame(BitmapSource logo, double opacity, double scale)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.Black, null, new Rect(0, 0, FrameWidth, FrameHeight));

            var centerX = FrameWidth / 2.0;
            var centerY = FrameHeight / 2.0;

            context.PushOpacity(opacity);
            context.PushTransform(new ScaleTransform(scale, scale, centerX, centerY));

            var (width, height) = GetLogoSize(logo);
            context.DrawImage(logo, new Rect(centerX - width / 2, centerY - height / 2, width, height));

            context.Pop(); // ScaleTransform
            context.Pop(); // Opacity
        }

        var renderTarget = new RenderTargetBitmap(FrameWidth, FrameHeight, 96, 96, PixelFormats.Pbgra32);
        renderTarget.Render(visual);
        renderTarget.Freeze();
        return renderTarget;
    }

    private static async Task SavePngAsync(BitmapSource bitmap, string path, CancellationToken cancellationToken)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var memoryStream = new MemoryStream();
        encoder.Save(memoryStream);
        memoryStream.Position = 0;

        await using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write);
        await memoryStream.CopyToAsync(fileStream, cancellationToken);
    }

    /// <summary>
    /// Construit le zip SANS COMPRESSION (<see cref="CompressionLevel.NoCompression"/>).
    /// POINT CRITIQUE : le format bootanimation Android exige que les entrées du ZIP soient
    /// stockées non compressées ("Stored") — le lecteur bootanimation d'Android ne sait pas
    /// décompresser les entrées Deflate. <see cref="ZipFile.CreateFromDirectory"/> avec
    /// <see cref="CompressionLevel.NoCompression"/> produit des entrées de méthode "Stored".
    /// </summary>
    private static void CreateUncompressedZip(string sourceDirectory, string zipPath)
    {
        ZipFile.CreateFromDirectory(sourceDirectory, zipPath, CompressionLevel.NoCompression, includeBaseDirectory: false);
    }
}

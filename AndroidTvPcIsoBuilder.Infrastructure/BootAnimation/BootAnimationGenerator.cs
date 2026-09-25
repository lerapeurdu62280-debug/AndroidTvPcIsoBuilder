using System.IO.Compression;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AndroidTvPcIsoBuilder.Application.Common;
using AndroidTvPcIsoBuilder.Application.Interfaces;
using AndroidTvPcIsoBuilder.Domain.Entities;

namespace AndroidTvPcIsoBuilder.Infrastructure.BootAnimation;

/// <summary>
/// Génère un <c>bootanimation.zip</c> Android standard (séquence de PNG numérotées dans
/// <c>part0/</c> + <c>desc.txt</c>) affichant un logo animé (fade-in + léger pulse d'échelle)
/// à la place du texte de boot kernel par défaut.
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
    private static readonly Color AndroidAccentColor = Color.FromRgb(0x7F, 0xBF, 0x7F); // Brush.AndroidAccent du thème WPF

    public async Task<Result<string>> GenerateAsync(BootAnimationConfig config, string outputDirectory, CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(outputDirectory);

            var workingDirectory = Path.Combine(outputDirectory, $"bootanimation-work-{Guid.NewGuid():N}");
            var partDirectory = Path.Combine(workingDirectory, "part0");
            Directory.CreateDirectory(partDirectory);

            var frameCount = Math.Max(1, config.FrameRate * config.DurationSeconds);
            var fadeInFrameCount = Math.Max(1, frameCount / 4);

            BitmapSource? sourceImage = null;
            if (!string.IsNullOrWhiteSpace(config.SourceImagePath) && File.Exists(config.SourceImagePath))
                sourceImage = LoadSourceImage(config.SourceImagePath);

            for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var opacity = frameIndex < fadeInFrameCount
                    ? (frameIndex + 1) / (double)fadeInFrameCount
                    : 1.0;

                // Pulse simple : échelle 0.95 -> 1.0 sur la durée du fade-in, puis maintien à 1.0.
                var scale = frameIndex < fadeInFrameCount
                    ? 0.95 + 0.05 * ((frameIndex + 1) / (double)fadeInFrameCount)
                    : 1.0;

                var frameBitmap = RenderFrame(sourceImage, opacity, scale);
                var framePath = Path.Combine(partDirectory, $"{frameIndex:D5}.png");
                await SavePngAsync(frameBitmap, framePath, cancellationToken);
            }

            var descPath = Path.Combine(workingDirectory, "desc.txt");
            // Format standard Android bootanimation : "<width> <height> <fps>" puis une ligne
            // par partie, ici "p <count> <pause> <folder>" avec count=0 (boucle jusqu'à la fin du
            // démarrage d'Android, comme l'animation d'origine : avec count=1 l'animation resterait
            // figée sur sa dernière image si le boot dure plus longtemps qu'elle) et pause=0.
            var descContent = $"{FrameWidth} {FrameHeight} {config.FrameRate}\np 0 0 part0\n";
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

    /// <summary>
    /// Dessine une frame sur fond noir : soit l'image source centrée (avec fade-in/pulse),
    /// soit, à défaut d'image fournie, un logo vectoriel par défaut (rond vert + triangle
    /// "play") dessiné directement avec <see cref="DrawingContext"/>.
    /// </summary>
    private static RenderTargetBitmap RenderFrame(BitmapSource? sourceImage, double opacity, double scale)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.Black, null, new Rect(0, 0, FrameWidth, FrameHeight));

            var centerX = FrameWidth / 2.0;
            var centerY = FrameHeight / 2.0;

            context.PushOpacity(opacity);
            context.PushTransform(new ScaleTransform(scale, scale, centerX, centerY));

            if (sourceImage is not null)
            {
                const double maxLogoSize = 420;
                var ratio = Math.Min(maxLogoSize / sourceImage.PixelWidth, maxLogoSize / sourceImage.PixelHeight);
                var width = sourceImage.PixelWidth * ratio;
                var height = sourceImage.PixelHeight * ratio;
                var rect = new Rect(centerX - width / 2, centerY - height / 2, width, height);
                context.DrawImage(sourceImage, rect);
            }
            else
            {
                DrawDefaultLogo(context, centerX, centerY);
            }

            context.Pop(); // ScaleTransform
            context.Pop(); // Opacity
        }

        var renderTarget = new RenderTargetBitmap(FrameWidth, FrameHeight, 96, 96, PixelFormats.Pbgra32);
        renderTarget.Render(visual);
        renderTarget.Freeze();
        return renderTarget;
    }

    /// <summary>Logo par défaut : cercle vert (couleur du thème Android) avec un triangle "play" centré.</summary>
    private static void DrawDefaultLogo(DrawingContext context, double centerX, double centerY)
    {
        const double radius = 180;
        var accentBrush = new SolidColorBrush(AndroidAccentColor);
        accentBrush.Freeze();

        context.DrawEllipse(accentBrush, null, new Point(centerX, centerY), radius, radius);

        const double triangleHalfHeight = 90;
        const double triangleWidth = 100;
        var offsetX = centerX - triangleWidth / 4; // léger décalage pour un rendu visuellement centré

        var trianglePoints = new[]
        {
            new Point(offsetX, centerY - triangleHalfHeight),
            new Point(offsetX, centerY + triangleHalfHeight),
            new Point(offsetX + triangleWidth, centerY),
        };

        var figure = new PathFigure { StartPoint = trianglePoints[0], IsClosed = true, IsFilled = true };
        figure.Segments.Add(new LineSegment(trianglePoints[1], isStroked: true));
        figure.Segments.Add(new LineSegment(trianglePoints[2], isStroked: true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();

        context.DrawGeometry(Brushes.Black, null, geometry);
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

using AndroidTvPcIsoBuilder.Application.Common;
using AndroidTvPcIsoBuilder.Application.Interfaces;
using AndroidTvPcIsoBuilder.Domain.Entities;

namespace AndroidTvPcIsoBuilder.Application.Services;

public class IsoDownloadOrchestrationService
{
    private readonly IIsoDownloadService _downloadService;
    private readonly IFileSystem _fileSystem;

    public IsoDownloadOrchestrationService(IIsoDownloadService downloadService, IFileSystem fileSystem)
    {
        _downloadService = downloadService;
        _fileSystem = fileSystem;
    }

    public IReadOnlyList<IsoDownloadSource> GetAvailableSources() => _downloadService.GetAvailableSources();

    public async Task<Result> DownloadAsync(
        IsoDownloadSource source,
        string destinationPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
            return Result.Failure("Le chemin de destination est requis.");

        if (!destinationPath.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
            return Result.Failure("Le chemin de destination doit se terminer par '.iso'.");

        var freeSpace = _fileSystem.GetAvailableFreeSpace(destinationPath);
        if (freeSpace < source.ApproximateSizeBytes)
        {
            return Result.Failure(
                $"Espace disque insuffisant pour ce téléchargement : {FormatBytes(freeSpace)} disponibles, " +
                $"environ {FormatBytes(source.ApproximateSizeBytes)} nécessaires.");
        }

        try
        {
            await _downloadService.DownloadAsync(source, destinationPath, progress, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Result.Failure("Le téléchargement a été annulé.");
        }
        catch (Exception ex)
        {
            return Result.Failure($"Échec du téléchargement : {ex.Message}");
        }

        return Result.Success();
    }

    private static string FormatBytes(long bytes)
    {
        double value = bytes;
        string[] units = { "o", "Ko", "Mo", "Go" };
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }
        return $"{value:0.#} {units[unitIndex]}";
    }
}

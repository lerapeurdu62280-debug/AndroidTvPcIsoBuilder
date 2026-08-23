using AndroidTvPcIsoBuilder.Domain.Entities;

namespace AndroidTvPcIsoBuilder.Application.Interfaces;

public interface IIsoDownloadService
{
    IReadOnlyList<IsoDownloadSource> GetAvailableSources();

    Task DownloadAsync(
        IsoDownloadSource source,
        string destinationPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record DownloadProgress(long BytesReceived, long? TotalBytes, int? PercentComplete);

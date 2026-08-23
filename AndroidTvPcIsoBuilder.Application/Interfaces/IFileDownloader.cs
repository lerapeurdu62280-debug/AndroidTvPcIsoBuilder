namespace AndroidTvPcIsoBuilder.Application.Interfaces;

public interface IFileDownloader
{
    Task DownloadAsync(
        Uri url,
        string destinationPath,
        string? expectedSha256 = null,
        IProgress<FileDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record FileDownloadProgress(long BytesReceived, long? TotalBytes, int? PercentComplete);

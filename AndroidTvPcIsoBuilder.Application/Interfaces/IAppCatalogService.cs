using AndroidTvPcIsoBuilder.Domain.Entities;

namespace AndroidTvPcIsoBuilder.Application.Interfaces;

public interface IAppCatalogService
{
    IReadOnlyList<AppCatalogEntry> GetAvailableApps();

    Task DownloadAsync(
        AppCatalogEntry entry,
        string destinationDirectory,
        IProgress<FileDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

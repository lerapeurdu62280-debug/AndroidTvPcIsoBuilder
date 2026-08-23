using AndroidTvPcIsoBuilder.Application.Common;
using AndroidTvPcIsoBuilder.Application.Interfaces;
using AndroidTvPcIsoBuilder.Domain.Entities;

namespace AndroidTvPcIsoBuilder.Application.Services;

public class AppCatalogOrchestrationService
{
    private readonly IAppCatalogService _catalogService;

    public AppCatalogOrchestrationService(IAppCatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    public IReadOnlyList<AppCatalogEntry> GetAvailableApps() => _catalogService.GetAvailableApps();

    public async Task<Result<string>> DownloadAsync(
        AppCatalogEntry entry,
        string destinationDirectory,
        IProgress<FileDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _catalogService.DownloadAsync(entry, destinationDirectory, progress, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Result<string>.Failure("Le téléchargement a été annulé.");
        }
        catch (Exception ex)
        {
            return Result<string>.Failure($"Échec du téléchargement de '{entry.DisplayName}' : {ex.Message}");
        }

        var apkPath = Path.Combine(destinationDirectory, $"{entry.Id}.apk");
        return Result<string>.Success(apkPath);
    }
}

using AndroidTvPcIsoBuilder.Application.Interfaces;
using AndroidTvPcIsoBuilder.Domain.Entities;

namespace AndroidTvPcIsoBuilder.Infrastructure.Download;

public class HttpIsoDownloadService : IIsoDownloadService
{
    private static readonly IReadOnlyList<IsoDownloadSource> Sources = new[]
    {
        new IsoDownloadSource(
            Id: "android-x86-9.0-r2-x64",
            DisplayName: "Android-x86 9.0-r2 (64 bits)",
            Description: "Build officiel Android-x86, basé sur Android 9 Pie. Compatible PC x86_64 génériques.",
            DownloadUrl: new Uri("https://sourceforge.net/projects/android-x86/files/Release%209.0/android-x86_64-9.0-r2.iso/download"),
            ApproximateSizeBytes: 965_700_000L,
            Sha256: "f7eb8fc56f29ad5432335dc054183acf086c539f3990f0b6e9ff58bd6df4604e"),

        new IsoDownloadSource(
            Id: "android-x86-9.0-r2-x86",
            DisplayName: "Android-x86 9.0-r2 (32 bits)",
            Description: "Build officiel Android-x86, basé sur Android 9 Pie. Compatible PC x86 32 bits plus anciens.",
            DownloadUrl: new Uri("https://sourceforge.net/projects/android-x86/files/Release%209.0/android-x86-9.0-r2.iso/download"),
            ApproximateSizeBytes: 965_700_000L),
    };

    private readonly IFileDownloader _fileDownloader;

    public HttpIsoDownloadService(IFileDownloader fileDownloader)
    {
        _fileDownloader = fileDownloader;
    }

    public IReadOnlyList<IsoDownloadSource> GetAvailableSources() => Sources;

    public async Task DownloadAsync(
        IsoDownloadSource source,
        string destinationPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var innerProgress = progress is null
            ? null
            : new Progress<FileDownloadProgress>(p => progress.Report(new DownloadProgress(p.BytesReceived, p.TotalBytes, p.PercentComplete)));

        await _fileDownloader.DownloadAsync(source.DownloadUrl, destinationPath, source.Sha256, innerProgress, cancellationToken);
    }
}

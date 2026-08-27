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

        new IsoDownloadSource(
            Id: "lineageos-tv-x86-21.0",
            DisplayName: "LineageOS TV 21.0 (64 bits)",
            Description: "Build communautaire LineageOS pour la cible \"x86_64_tv\", avec le vrai launcher Android TV (Leanback) au lieu d'une interface bureau. Basé sur Android 14. Build non officielle (UNOFFICIAL), non signée par l'équipe LineageOS.",
            DownloadUrl: new Uri("https://sourceforge.net/projects/lineageos-tv-x86/files/lineage-21.0/x86_64_tv/lineage-21.0-20260331-UNOFFICIAL-x86_64_tv-signed.iso/download"),
            ApproximateSizeBytes: 2_630_580_224L),

        new IsoDownloadSource(
            Id: "googletv-x86-14-v27t",
            DisplayName: "Google TV 14 (64 bits)",
            Description: "Build communautaire AndroidTV-x86_64 (MRDTeam), avec la véritable interface Google TV (recommandations, onglets) plutôt que le launcher Android TV classique. Basé sur Android 14. Build non officielle, non affiliée à Google. Aucune build Google TV 16 n'existe encore côté communautaire à ce jour ; le system-image officiel Google (sys-img/google-tv) n'est pas utilisable tel quel car conçu pour l'émulateur (kernel/ramdisk Ranchu, sans bootloader).",
            DownloadUrl: new Uri("https://sourceforge.net/projects/androidtv-x86-64/files/GTV14/GTV14-x86_64-MRDTeam-V27T-260811.iso/download"),
            ApproximateSizeBytes: 3_119_212_544L),
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

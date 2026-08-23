namespace AndroidTvPcIsoBuilder.Domain.Entities;

public sealed record IsoDownloadSource(
    string Id,
    string DisplayName,
    string Description,
    Uri DownloadUrl,
    long ApproximateSizeBytes,
    string? Sha256 = null);

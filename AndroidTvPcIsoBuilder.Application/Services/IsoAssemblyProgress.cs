using AndroidTvPcIsoBuilder.Domain.Enums;

namespace AndroidTvPcIsoBuilder.Application.Services;

/// <summary>
/// Modèle de progression unifié du pipeline de génération d'une image Android TV à partir
/// d'une ISO officielle téléchargée (téléchargement -> pilotes -> apps -> bootanimation ->
/// assemblage -> vérification). Les champs de téléchargement (BytesReceived/TotalBytes/
/// DownloadSpeedBytesPerSecond) ne sont renseignés que pendant le jalon Downloading.
/// </summary>
public sealed record IsoAssemblyProgress(
    BuildMilestone Milestone,
    int PercentComplete,
    string? LogLine = null,
    long? BytesReceived = null,
    long? TotalBytes = null,
    double? DownloadSpeedBytesPerSecond = null);

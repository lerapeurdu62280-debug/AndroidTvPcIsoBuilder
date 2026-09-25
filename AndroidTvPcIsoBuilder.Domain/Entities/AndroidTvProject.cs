using AndroidTvPcIsoBuilder.Domain.Enums;

namespace AndroidTvPcIsoBuilder.Domain.Entities;

public class AndroidTvProject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public SourceIsoSelection SourceIso { get; set; } = new();
    public BootMode BootMode { get; set; } = BootMode.Hybrid;
    public BootAnimationConfig BootAnimation { get; set; } = new();
    public required string OutputIsoPath { get; set; }
    public string Resolution { get; set; } = "1920x1080";
    public string Language { get; set; } = "fr-FR";
    public List<AppPackage> Apps { get; set; } = new();
    public List<BuildHistoryEntry> BuildHistory { get; set; } = new();
}

public sealed record BuildHistoryEntry(DateTimeOffset Timestamp, bool Succeeded, string Summary, long? OutputSizeBytes);
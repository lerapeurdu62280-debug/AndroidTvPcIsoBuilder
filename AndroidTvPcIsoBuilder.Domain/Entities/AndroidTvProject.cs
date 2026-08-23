using AndroidTvPcIsoBuilder.Domain.Enums;

namespace AndroidTvPcIsoBuilder.Domain.Entities;

public class AndroidTvProject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public BaseSystemType BaseSystem { get; set; }
    public required string SourcePath { get; set; }
    public required string OutputIsoPath { get; set; }
    public string Resolution { get; set; } = "1920x1080";
    public string Language { get; set; } = "fr-FR";
    public BootMode BootMode { get; set; } = BootMode.Hybrid;
    public List<AppPackage> Apps { get; set; } = new();
    public List<BuildHistoryEntry> BuildHistory { get; set; } = new();
}

public sealed record BuildHistoryEntry(DateTimeOffset Timestamp, bool Succeeded, string Summary, long? OutputSizeBytes);
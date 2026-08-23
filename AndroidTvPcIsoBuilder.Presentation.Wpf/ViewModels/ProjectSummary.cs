namespace AndroidTvPcIsoBuilder.Presentation.Wpf.ViewModels;

public sealed class ProjectSummary
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string BaseSystem { get; init; }
    public required string OutputIsoPath { get; init; }
}

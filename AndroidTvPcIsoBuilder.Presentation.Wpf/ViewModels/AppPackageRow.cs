namespace AndroidTvPcIsoBuilder.Presentation.Wpf.ViewModels;

public sealed class AppPackageRow
{
    public required string Name { get; init; }
    public required string SourceApkPath { get; init; }
    public bool IsPreinstalled { get; init; }
}

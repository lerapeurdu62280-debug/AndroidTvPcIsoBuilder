namespace AndroidTvPcIsoBuilder.Domain.Entities;

public class AppPackage
{
    public required string Name { get; set; }
    public required string SourceApkPath { get; set; }
    public bool IsPreinstalled { get; set; } = true;
}
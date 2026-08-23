namespace AndroidTvPcIsoBuilder.Application.Services;

public sealed record BuildPreview(
    long SourceSizeBytes,
    long AppsSizeBytes,
    long EstimatedOutputSizeBytes,
    int AppCount,
    IReadOnlyList<string> Warnings);

public sealed record BuildVerification(
    bool IsReadable,
    bool HasBootImage,
    int AppsFoundInOutput,
    int AppsExpected,
    long ActualOutputSizeBytes,
    IReadOnlyList<string> Issues);

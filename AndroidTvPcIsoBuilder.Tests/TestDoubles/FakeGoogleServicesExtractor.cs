using AndroidTvPcIsoBuilder.Application.Common;
using AndroidTvPcIsoBuilder.Application.Interfaces;
using AndroidTvPcIsoBuilder.Domain.Entities;

namespace AndroidTvPcIsoBuilder.Tests.TestDoubles;

/// <summary>Écrit un faux GmsCore dans le dossier de sortie, sans ISO donneuse.</summary>
public sealed class FakeGoogleServicesExtractor : IGoogleServicesExtractor
{
    public int CallCount { get; private set; }

    public async Task<Result<string>> ExtractAsync(GoogleServicesConfig config, string outputDirectory, CancellationToken cancellationToken = default)
    {
        CallCount++;
        var apkDirectory = Path.Combine(outputDirectory, "product", "priv-app", "PrebuiltGmsCorePano");
        Directory.CreateDirectory(apkDirectory);
        await File.WriteAllBytesAsync(Path.Combine(apkDirectory, "PrebuiltGmsCorePano.apk"), new byte[] { 1, 2, 3 }, cancellationToken);
        return Result<string>.Success(outputDirectory);
    }
}

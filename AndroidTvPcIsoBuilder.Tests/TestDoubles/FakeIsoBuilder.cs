using AndroidTvPcIsoBuilder.Application.Interfaces;
using AndroidTvPcIsoBuilder.Application.Services;
using AndroidTvPcIsoBuilder.Domain.Entities;

namespace AndroidTvPcIsoBuilder.Tests.TestDoubles;

public class FakeIsoBuilder : IIsoBuilder
{
    public bool WasCalled { get; private set; }
    public Exception? ExceptionToThrow { get; set; }
    public BuildVerification VerificationResult { get; set; } = new(true, true, 0, 0, 1024, Array.Empty<string>());

    public Task BuildAsync(
        AndroidTvProject project,
        string sourceIsoPath,
        IReadOnlyList<string>? resolvedDriverFilePaths = null,
        IProgress<BuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        WasCalled = true;
        if (ExceptionToThrow is not null)
            throw ExceptionToThrow;

        progress?.Report(new BuildProgress("Fake", 50, "Simulation en cours"));
        return Task.CompletedTask;
    }

    public BuildVerification Verify(AndroidTvProject project) => VerificationResult;
}

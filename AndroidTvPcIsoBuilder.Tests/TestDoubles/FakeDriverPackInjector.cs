using AndroidTvPcIsoBuilder.Application.Common;
using AndroidTvPcIsoBuilder.Application.Interfaces;
using AndroidTvPcIsoBuilder.Application.Services;
using AndroidTvPcIsoBuilder.Domain.Entities;
using AndroidTvPcIsoBuilder.Domain.Enums;

namespace AndroidTvPcIsoBuilder.Tests.TestDoubles;

/// <summary>Double de test pour <see cref="IDriverPackInjector"/> : ne fait aucune I/O réelle.</summary>
public class FakeDriverPackInjector : IDriverPackInjector
{
    public bool WasCalled { get; private set; }
    public Exception? ExceptionToThrow { get; set; }
    public bool ShouldFail { get; set; }
    public string FailureMessage { get; set; } = "Échec de résolution des pilotes simulé.";
    public IReadOnlyList<string> FilesToReturn { get; set; } = Array.Empty<string>();

    public Task<Result<IReadOnlyList<string>>> ResolveDriverFilesAsync(
        AndroidTvProject project,
        IProgress<IsoAssemblyProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        WasCalled = true;

        if (ExceptionToThrow is not null)
            throw ExceptionToThrow;

        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report(new IsoAssemblyProgress(BuildMilestone.DriverInjection, 100, "Résolution des pilotes terminée (simulée)."));

        return Task.FromResult(ShouldFail
            ? Result<IReadOnlyList<string>>.Failure(FailureMessage)
            : Result<IReadOnlyList<string>>.Success(FilesToReturn));
    }
}

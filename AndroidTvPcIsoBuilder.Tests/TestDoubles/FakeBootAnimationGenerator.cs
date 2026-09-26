using AndroidTvPcIsoBuilder.Application.Common;
using AndroidTvPcIsoBuilder.Application.Interfaces;
using AndroidTvPcIsoBuilder.Domain.Entities;

namespace AndroidTvPcIsoBuilder.Tests.TestDoubles;

/// <summary>
/// Double de test pour <see cref="IBootAnimationGenerator"/> : écrit un petit fichier réel
/// (nécessaire car <see cref="AndroidTvPcIsoBuilder.Infrastructure.Iso.IsoBuilder"/> ouvre le
/// chemin retourné avec <c>File.OpenRead</c> pour l'insérer dans l'ISO), sans reproduire la
/// vraie logique de génération de bootanimation.
/// </summary>
public class FakeBootAnimationGenerator : IBootAnimationGenerator
{
    private readonly string _pathToReturn = Path.Combine(Path.GetTempPath(), $"fake-bootanimation-{Guid.NewGuid()}.zip");

    public string PathToReturn
    {
        get => _pathToReturn;
        init => _pathToReturn = value;
    }

    public bool ShouldFail { get; set; }
    public BootAnimationConfig? LastConfig { get; private set; }
    public string? LastOutputDirectory { get; private set; }

    public Task<Result<string>> GenerateAsync(BootAnimationConfig config, string outputDirectory, CancellationToken cancellationToken = default)
    {
        LastConfig = config;
        LastOutputDirectory = outputDirectory;

        if (ShouldFail)
            return Task.FromResult(Result<string>.Failure("Échec simulé de génération du bootanimation."));

        File.WriteAllBytes(_pathToReturn, new byte[] { 1, 2, 3, 4 });
        return Task.FromResult(Result<string>.Success(_pathToReturn));
    }

    public string SplashPathToReturn { get; } = Path.Combine(Path.GetTempPath(), $"fake-splash-{Guid.NewGuid()}.atvs");

    public bool SplashShouldFail { get; set; }

    public Task<Result<string>> GenerateSplashImageAsync(BootAnimationConfig config, string outputDirectory, CancellationToken cancellationToken = default)
    {
        if (SplashShouldFail)
            return Task.FromResult(Result<string>.Failure("Échec simulé de génération du logo de démarrage."));

        File.WriteAllBytes(SplashPathToReturn, "ATVS"u8.ToArray());
        return Task.FromResult(Result<string>.Success(SplashPathToReturn));
    }
}

using AndroidTvPcIsoBuilder.Application.Common;
using AndroidTvPcIsoBuilder.Domain.Entities;

namespace AndroidTvPcIsoBuilder.Application.Interfaces;

public interface IAptoideTvProvider
{
    /// <summary>
    /// Chemin de l'APK Aptoide TV à intégrer : celui du projet, ou la dernière version
    /// téléchargée depuis le serveur officiel d'Aptoide dans <paramref name="workDirectory"/>.
    /// </summary>
    Task<Result<string>> GetApkAsync(AptoideTvConfig config, string workDirectory, CancellationToken cancellationToken = default);
}

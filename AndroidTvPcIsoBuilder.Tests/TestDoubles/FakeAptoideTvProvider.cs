using AndroidTvPcIsoBuilder.Application.Common;
using AndroidTvPcIsoBuilder.Application.Interfaces;
using AndroidTvPcIsoBuilder.Domain.Entities;

namespace AndroidTvPcIsoBuilder.Tests.TestDoubles;

public class FakeAptoideTvProvider : IAptoideTvProvider
{
    public Result<string> ResultToReturn { get; set; } = Result<string>.Success("C:/Temp/AptoideTV.apk");

    public Task<Result<string>> GetApkAsync(AptoideTvConfig config, string workDirectory, CancellationToken cancellationToken = default)
        => Task.FromResult(ResultToReturn);
}

using AndroidTvPcIsoBuilder.Domain.Entities;

namespace AndroidTvPcIsoBuilder.Application.Interfaces;

public interface IProjectRepository
{
    Task<AndroidTvProject?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AndroidTvProject>> GetAllAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AndroidTvProject project, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

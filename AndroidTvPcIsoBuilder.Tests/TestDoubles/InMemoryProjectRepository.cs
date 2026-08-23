using AndroidTvPcIsoBuilder.Application.Interfaces;
using AndroidTvPcIsoBuilder.Domain.Entities;

namespace AndroidTvPcIsoBuilder.Tests.TestDoubles;

public class InMemoryProjectRepository : IProjectRepository
{
    private readonly Dictionary<Guid, AndroidTvProject> _projects = new();

    public Task<AndroidTvProject?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(_projects.GetValueOrDefault(id));

    public Task<IReadOnlyList<AndroidTvProject>> GetAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<AndroidTvProject>>(_projects.Values.ToList());

    public Task SaveAsync(AndroidTvProject project, CancellationToken cancellationToken = default)
    {
        _projects[project.Id] = project;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _projects.Remove(id);
        return Task.CompletedTask;
    }
}

using System.Text.Json;
using AndroidTvPcIsoBuilder.Application.Interfaces;
using AndroidTvPcIsoBuilder.Domain.Entities;

namespace AndroidTvPcIsoBuilder.Infrastructure.Persistence;

public class JsonProjectRepository : IProjectRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _storageDirectory;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public JsonProjectRepository(string? storageDirectory = null)
    {
        _storageDirectory = storageDirectory ?? GetPortableDirectory() ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AndroidTvPcIsoBuilder",
            "Projects");

        Directory.CreateDirectory(_storageDirectory);
    }

    /// <summary>
    /// Version portable : si un dossier "Donnees" est posé à côté de l'exécutable, les projets
    /// y sont enregistrés (clé USB, dossier copié d'un PC à l'autre) au lieu du profil Windows.
    /// </summary>
    private static string? GetPortableDirectory()
    {
        var portableRoot = Path.Combine(AppContext.BaseDirectory, "Donnees");
        return Directory.Exists(portableRoot) ? Path.Combine(portableRoot, "Projects") : null;
    }

    public async Task<AndroidTvProject?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var path = GetProjectPath(id);
        if (!File.Exists(path))
            return null;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<AndroidTvProject>(stream, SerializerOptions, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<AndroidTvProject>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var projects = new List<AndroidTvProject>();
            foreach (var file in Directory.EnumerateFiles(_storageDirectory, "*.json"))
            {
                await using var stream = File.OpenRead(file);
                var project = await JsonSerializer.DeserializeAsync<AndroidTvProject>(stream, SerializerOptions, cancellationToken);
                if (project is not null)
                    projects.Add(project);
            }

            return projects;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(AndroidTvProject project, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var path = GetProjectPath(project.Id);
            var tempPath = path + ".tmp";

            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, project, SerializerOptions, cancellationToken);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var path = GetProjectPath(id);
        if (File.Exists(path))
            File.Delete(path);

        return Task.CompletedTask;
    }

    private string GetProjectPath(Guid id) => Path.Combine(_storageDirectory, $"{id}.json");
}

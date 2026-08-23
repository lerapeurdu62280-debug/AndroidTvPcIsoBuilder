using AndroidTvPcIsoBuilder.Application.Interfaces;

namespace AndroidTvPcIsoBuilder.Tests.TestDoubles;

public class FakeFileSystem : IFileSystem
{
    private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

    public FakeFileSystem AddFile(string path, long size = 1024)
    {
        _files.Add(path);
        _sizes[path] = size;
        return this;
    }

    public FakeFileSystem AddDirectory(string path)
    {
        _directories.Add(path);
        return this;
    }

    private readonly Dictionary<string, long> _sizes = new(StringComparer.OrdinalIgnoreCase);

    public bool FileExists(string path) => _files.Contains(path);

    public bool DirectoryExists(string path) => _directories.Contains(path);

    public long GetFileSize(string path) => _sizes.GetValueOrDefault(path, 0);

    public long AvailableFreeSpace { get; set; } = long.MaxValue;

    public long GetAvailableFreeSpace(string path) => AvailableFreeSpace;
}

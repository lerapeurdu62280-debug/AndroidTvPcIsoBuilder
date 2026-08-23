using AndroidTvPcIsoBuilder.Application.Interfaces;

namespace AndroidTvPcIsoBuilder.Infrastructure.FileSystem;

public class LocalFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public long GetFileSize(string path) => new FileInfo(path).Length;

    public long GetAvailableFreeSpace(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(root))
            return long.MaxValue;

        return new DriveInfo(root).AvailableFreeSpace;
    }
}

namespace AndroidTvPcIsoBuilder.Application.Interfaces;

public interface IFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    long GetFileSize(string path);

    /// <summary>Espace libre (en octets) sur le disque contenant le chemin donné.</summary>
    long GetAvailableFreeSpace(string path);
}

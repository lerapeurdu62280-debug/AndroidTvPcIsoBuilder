using AndroidTvPcIsoBuilder.Application.Interfaces;

namespace AndroidTvPcIsoBuilder.Tests.TestDoubles;

/// <summary>
/// Double de test pour <see cref="IFileDownloader"/> : simule un téléchargement en créant
/// simplement un fichier vide (ou de contenu configurable) au chemin de destination, sans
/// accès réseau réel.
/// </summary>
public class FakeFileDownloader : IFileDownloader
{
    public List<Uri> RequestedUrls { get; } = new();
    public Exception? ExceptionToThrow { get; set; }
    public byte[] ContentToWrite { get; set; } = Array.Empty<byte>();

    public Task DownloadAsync(
        Uri url,
        string destinationPath,
        string? expectedSha256 = null,
        IProgress<FileDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        RequestedUrls.Add(url);

        if (ExceptionToThrow is not null)
            throw ExceptionToThrow;

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllBytes(destinationPath, ContentToWrite);

        progress?.Report(new FileDownloadProgress(ContentToWrite.Length, ContentToWrite.Length, 100));

        return Task.CompletedTask;
    }
}

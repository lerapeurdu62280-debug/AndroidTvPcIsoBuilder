using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using AndroidTvPcIsoBuilder.Application.Interfaces;

namespace AndroidTvPcIsoBuilder.Infrastructure.Download;

/// <summary>
/// Télécharge un fichier avec écriture atomique (fichier .download temporaire renommé à la fin),
/// vérification SHA256 optionnelle, et reprise depuis un fichier .download partiel existant
/// (via l'en-tête HTTP Range, si le serveur le supporte).
/// </summary>
public class HttpFileDownloader : IFileDownloader
{
    private readonly HttpClient _httpClient;

    public HttpFileDownloader(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task DownloadAsync(
        Uri url,
        string destinationPath,
        string? expectedSha256 = null,
        IProgress<FileDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = destinationPath + ".download";
        var resumeOffset = File.Exists(tempPath) ? new FileInfo(tempPath).Length : 0L;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (resumeOffset > 0)
                request.Headers.Range = new RangeHeaderValue(resumeOffset, null);

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            var isResuming = resumeOffset > 0 && response.StatusCode == HttpStatusCode.PartialContent;
            if (resumeOffset > 0 && !isResuming)
            {
                // Le serveur ne supporte pas la reprise (ou l'a refusée) : on repart de zéro.
                resumeOffset = 0;
            }

            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentRange?.Length ?? response.Content.Headers.ContentLength + resumeOffset;

            using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var fileStream = new FileStream(tempPath, isResuming ? FileMode.Append : FileMode.Create, FileAccess.Write))
            {
                using var sha256 = expectedSha256 is not null ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;

                if (isResuming && sha256 is not null)
                {
                    // Le hash doit couvrir tout le fichier : on relit la portion déjà téléchargée pour l'y intégrer.
                    await using var existingStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read);
                    var hashBuffer = new byte[81920];
                    int hashBytesRead;
                    while ((hashBytesRead = await existingStream.ReadAsync(hashBuffer, cancellationToken)) > 0)
                        sha256.AppendData(hashBuffer, 0, hashBytesRead);
                }

                var buffer = new byte[81920];
                long bytesReceived = resumeOffset;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    sha256?.AppendData(buffer, 0, bytesRead);

                    bytesReceived += bytesRead;
                    var percent = totalBytes is > 0
                        ? (int)(bytesReceived * 100 / totalBytes.Value)
                        : (int?)null;
                    progress?.Report(new FileDownloadProgress(bytesReceived, totalBytes, percent));
                }

                if (sha256 is not null)
                {
                    var actualHash = Convert.ToHexString(sha256.GetHashAndReset()).ToLowerInvariant();
                    if (!string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"L'empreinte SHA256 du fichier téléchargé ne correspond pas à celle attendue " +
                            $"(attendu {expectedSha256}, obtenu {actualHash}). Le fichier est peut-être corrompu ou la source a changé.");
                    }
                }
            }

            File.Move(tempPath, destinationPath, overwrite: true);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // Annulation volontaire : on garde le fichier .download partiel pour permettre une reprise ultérieure.
            throw;
        }
        catch
        {
            // Échec réel (erreur réseau, hash invalide, ...) : on ne garde pas un fichier potentiellement corrompu.
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            throw;
        }
    }
}

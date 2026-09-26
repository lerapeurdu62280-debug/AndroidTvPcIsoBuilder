using System.Security.Cryptography;
using System.Text.Json;
using AndroidTvPcIsoBuilder.Application.Common;
using AndroidTvPcIsoBuilder.Application.Interfaces;
using AndroidTvPcIsoBuilder.Domain.Entities;
using AndroidTvPcIsoBuilder.Infrastructure.Iso;

namespace AndroidTvPcIsoBuilder.Infrastructure.Download;

/// <summary>
/// Fournit l'APK Aptoide TV : celui choisi dans le projet, sinon la dernière version publiée
/// sur le serveur officiel d'Aptoide (API publique « getMeta »), vérifiée par son empreinte MD5
/// et par le nom de paquet lu dans son manifeste.
/// </summary>
public class AptoideTvProvider : IAptoideTvProvider
{
    public const string PackageName = "cm.aptoidetv.pt";
    private static readonly Uri MetadataUrl = new($"https://ws75.aptoide.com/api/7/app/getMeta?package_name={PackageName}");

    private readonly HttpClient _httpClient;
    private readonly IFileDownloader _fileDownloader;

    public AptoideTvProvider(HttpClient httpClient, IFileDownloader fileDownloader)
    {
        _httpClient = httpClient;
        _fileDownloader = fileDownloader;
    }

    public async Task<Result<string>> GetApkAsync(AptoideTvConfig config, string workDirectory, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(config.ApkPath))
            return CheckApk(config.ApkPath);

        string downloadUrl, md5, version;
        try
        {
            await using var metadataStream = await _httpClient.GetStreamAsync(MetadataUrl, cancellationToken);
            using var metadata = await JsonDocument.ParseAsync(metadataStream, cancellationToken: cancellationToken);
            var file = metadata.RootElement.GetProperty("data").GetProperty("file");
            downloadUrl = file.GetProperty("path").GetString()!;
            md5 = file.GetProperty("md5sum").GetString()!;
            version = file.GetProperty("vername").GetString()!;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return Result<string>.Failure($"Aptoide TV : impossible d'interroger le serveur d'Aptoide ({ex.Message}). Vérifiez la connexion Internet ou choisissez un APK.");
        }

        var destination = Path.Combine(workDirectory, $"AptoideTV-{version}.apk");
        try
        {
            await _fileDownloader.DownloadAsync(new Uri(downloadUrl), destination, expectedSha256: null, progress: null, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            return Result<string>.Failure($"Aptoide TV : échec du téléchargement ({ex.Message}).");
        }

        await using (var apk = File.OpenRead(destination))
        {
            var actualMd5 = Convert.ToHexStringLower(await MD5.HashDataAsync(apk, cancellationToken));
            if (!actualMd5.Equals(md5, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(destination);
                return Result<string>.Failure("Aptoide TV : le fichier téléchargé est corrompu (empreinte MD5 différente de celle publiée par Aptoide).");
            }
        }

        return CheckApk(destination);
    }

    private static Result<string> CheckApk(string path)
    {
        if (!File.Exists(path))
            return Result<string>.Failure($"Aptoide TV : APK introuvable : {path}");

        try
        {
            var manifest = ApkManifestReader.Read(path);
            return manifest.PackageName == PackageName
                ? Result<string>.Success(path)
                : Result<string>.Failure($"Aptoide TV : {Path.GetFileName(path)} n'est pas Aptoide TV (paquet {manifest.PackageName}).");
        }
        catch (InvalidDataException ex)
        {
            return Result<string>.Failure($"Aptoide TV : APK illisible ({ex.Message}).");
        }
    }
}

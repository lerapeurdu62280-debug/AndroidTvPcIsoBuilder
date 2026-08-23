using System.Text.Json;
using AndroidTvPcIsoBuilder.Application.Interfaces;
using AndroidTvPcIsoBuilder.Domain.Entities;

namespace AndroidTvPcIsoBuilder.Infrastructure.Download;

/// <summary>
/// Catalogue d'applications Android TV courantes, résolues et téléchargées depuis F-Droid
/// (dépôt libre et ouvert, API publique stable : https://f-droid.org/docs/All_our_APIs/).
/// Le code de version change à chaque mise à jour ; il est résolu dynamiquement via l'API
/// avant de construire l'URL de téléchargement, plutôt que codé en dur.
/// </summary>
public class FDroidAppCatalogService : IAppCatalogService
{
    private static readonly IReadOnlyList<AppCatalogEntry> Catalog = new[]
    {
        // ===== Multimédia =====
        new AppCatalogEntry("vlc", "VLC", "Lecteur multimédia universel, lit quasiment tous les formats vidéo et audio.", "org.videolan.vlc", "Multimédia"),
        new AppCatalogEntry("kodi", "Kodi", "Centre multimédia complet : bibliothèque vidéo, musique, extensions.", "org.xbmc.kodi", "Multimédia"),
        new AppCatalogEntry("jellyfin-tv", "Jellyfin pour Android TV", "Client officiel pour serveur multimédia personnel Jellyfin, optimisé télécommande.", "org.jellyfin.androidtv", "Multimédia"),
        new AppCatalogEntry("newpipe", "NewPipe", "Client vidéo léger pour YouTube, sans compte requis.", "org.schabi.newpipe", "Multimédia"),
        new AppCatalogEntry("smarttube", "SmartTube", "Client YouTube optimisé pour la télécommande, sans publicités.", "app.smarttube.fdroid", "Multimédia"),
        new AppCatalogEntry("libretube", "LibreTube", "Alternative à YouTube respectueuse de la vie privée, sans compte ni publicités.", "com.github.libretube", "Multimédia"),
        new AppCatalogEntry("mpv-android", "mpv-android", "Lecteur vidéo léger et puissant, capable de lire presque tous les formats.", "is.xyz.mpv", "Multimédia"),
        new AppCatalogEntry("seal", "Seal", "Téléchargeur de vidéos et musiques depuis de nombreux sites, simple à utiliser.", "com.junkfood.seal", "Multimédia"),
        new AppCatalogEntry("fossify-music-player", "Fossify Music Player", "Lecteur de musique local simple, sans publicité ni traceur.", "org.fossify.musicplayer", "Multimédia"),
        new AppCatalogEntry("transistor", "Transistor", "Application simple pour écouter des radios en direct depuis internet.", "org.y20k.transistor", "Multimédia"),
        new AppCatalogEntry("radiodroid", "RadioDroid", "Lecteur de radios internet avec un vaste catalogue de stations et enregistrement.", "net.programmierecke.radiodroid2", "Multimédia"),
        new AppCatalogEntry("antennapod", "AntennaPod", "Gestionnaire de podcasts complet qui fonctionne directement depuis les flux RSS.", "de.danoeh.antennapod", "Multimédia"),
        new AppCatalogEntry("fossify-gallery", "Fossify Gallery", "Galerie photo et vidéo avec éditeur intégré, sans publicité.", "org.fossify.gallery", "Multimédia"),
        new AppCatalogEntry("aves-libre", "Aves", "Explorateur de photos et vidéos qui affiche aussi leurs métadonnées techniques.", "deckers.thibault.aves.libre", "Multimédia"),

        // ===== Interface =====
        new AppCatalogEntry("couchy-launcher", "Couchy Launcher", "Lanceur d'accueil léger et rapide, pensé pour la télécommande.", "com.conreo.couchytv", "Interface"),
        new AppCatalogEntry("fossify-clock", "Fossify Clock", "Horloge, alarme et chronomètre simple, pensée pour le salon.", "org.fossify.clock", "Interface"),
        new AppCatalogEntry("fossify-notes", "Fossify Notes", "Bloc-notes rapide pour listes de courses et pense-bêtes.", "org.fossify.notes", "Interface"),
        new AppCatalogEntry("fossify-calendar", "Fossify Calendar", "Calendrier simple avec rappels d'événements et widgets personnalisables.", "org.fossify.calendar", "Interface"),

        // ===== Utilitaires =====
        new AppCatalogEntry("file-manager", "Fossify File Manager", "Gestionnaire de fichiers simple, pour parcourir le stockage de l'appareil.", "org.fossify.filemanager", "Utilitaires"),
        new AppCatalogEntry("amaze-filemanager", "Amaze File Manager", "Gestionnaire de fichiers complet avec support FTP, SMB et SFTP.", "com.amaze.filemanager", "Utilitaires"),
        new AppCatalogEntry("aria2-app", "Aria2App", "Client pour piloter des téléchargements aria2 (torrent/HTTP) à distance.", "com.gianlu.aria2app", "Utilitaires"),
        new AppCatalogEntry("k9-mail", "K-9 Mail", "Client email libre et complet.", "com.fsck.k9", "Utilitaires"),
        new AppCatalogEntry("termux", "Termux", "Terminal Linux complet pour exécuter des commandes et scripts sur l'appareil.", "com.termux", "Utilitaires"),
        new AppCatalogEntry("wifi-analyzer", "WiFi Analyzer", "Outil pour analyser la qualité du réseau WiFi et détecter les interférences.", "com.vrem.wifianalyzer", "Utilitaires"),
        new AppCatalogEntry("aegis", "Aegis Authenticator", "Générateur de codes de double authentification, sécurisé et hors ligne.", "com.beemdevelopment.aegis", "Utilitaires"),
        new AppCatalogEntry("kde-connect", "KDE Connect", "Partage de fichiers, notifications et contrôle de l'appareil depuis un ordinateur.", "org.kde.kdeconnect_tp", "Utilitaires"),
        new AppCatalogEntry("libretorrent", "LibreTorrent", "Client de téléchargement torrent complet, compatible avec Android TV.", "org.proninyaroslav.libretorrent", "Utilitaires"),
        new AppCatalogEntry("davx5", "DAVx5", "Synchronisation de contacts, calendriers et fichiers avec un serveur personnel.", "at.bitfire.davdroid", "Utilitaires"),

        // ===== Navigateurs =====
        new AppCatalogEntry("fennec-fdroid", "Fennec F-Droid", "Version de Firefox débarrassée du pistage et des composants propriétaires.", "org.mozilla.fennec_fdroid", "Navigateurs"),

        // ===== Actualités =====
        new AppCatalogEntry("feeder", "Feeder", "Lecteur de flux RSS et Atom simple, sans compte ni suivi.", "com.nononsenseapps.feeder", "Actualités"),
        new AppCatalogEntry("internet-radio", "Internet Radio", "Découvrez et écoutez des milliers de radios du monde entier.", "com.armanmaurya.internetradio", "Actualités"),

        // ===== Domotique =====
        new AppCatalogEntry("home-assistant", "Home Assistant", "Compagnon officiel pour contrôler votre maison connectée Home Assistant.", "io.homeassistant.companion.android.minimal", "Domotique"),

        // ===== Cloud =====
        new AppCatalogEntry("nextcloud", "Nextcloud", "Client officiel pour synchroniser et partager vos fichiers avec un serveur Nextcloud.", "com.nextcloud.client", "Cloud"),
        new AppCatalogEntry("owncloud", "ownCloud", "Client officiel pour accéder et synchroniser vos fichiers stockés sur un serveur ownCloud.", "com.owncloud.android", "Cloud"),
        new AppCatalogEntry("syncthing-fork", "Syncthing", "Synchronisation de fichiers décentralisée entre vos appareils, sans cloud tiers.", "com.github.catfriend1.syncthingfork", "Cloud"),

        // ===== Productivité =====
        new AppCatalogEntry("element", "Element", "Client de messagerie chiffrée basé sur le protocole ouvert Matrix.", "im.vector.app", "Productivité"),
    };

    private readonly IFileDownloader _fileDownloader;
    private readonly HttpClient _httpClient;

    public FDroidAppCatalogService(IFileDownloader fileDownloader, HttpClient httpClient)
    {
        _fileDownloader = fileDownloader;
        _httpClient = httpClient;
    }

    public IReadOnlyList<AppCatalogEntry> GetAvailableApps() => Catalog;

    public async Task DownloadAsync(
        AppCatalogEntry entry,
        string destinationDirectory,
        IProgress<FileDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var versionCode = await ResolveLatestVersionCodeAsync(entry.PackageId, cancellationToken);
        var apkFileName = $"{entry.PackageId}_{versionCode}.apk";
        var downloadUrl = new Uri($"https://f-droid.org/repo/{apkFileName}");
        var destinationPath = Path.Combine(destinationDirectory, $"{entry.Id}.apk");

        await _fileDownloader.DownloadAsync(downloadUrl, destinationPath, expectedSha256: null, progress, cancellationToken);
    }

    private async Task<long> ResolveLatestVersionCodeAsync(string packageId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            new Uri($"https://f-droid.org/api/v1/packages/{packageId}"), cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("suggestedVersionCode", out var versionCodeElement))
        {
            throw new InvalidOperationException(
                $"Impossible de déterminer la dernière version de '{packageId}' depuis F-Droid : réponse inattendue.");
        }

        return versionCodeElement.GetInt64();
    }
}

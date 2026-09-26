namespace AndroidTvPcIsoBuilder.Domain.Entities;

/// <summary>
/// Ajout des services Google TV (Google Play Services TV, Google Services Framework, assistant
/// vocal TV, synthèse vocale, Play Store TV) à une base qui n'en a pas, par exemple LineageOS TV.
/// Les composants sont repris d'une ISO « donneuse » Android TV de même version d'Android.
/// Au démarrage, rien n'est ajouté si le système contient déjà les services Google : mélanger
/// deux jeux de services Google provoquerait des conflits.
/// </summary>
public class GoogleServicesConfig
{
    public bool Enabled { get; set; }

    /// <summary>ISO Android TV qui contient les services Google (ex. une ISO Google TV x86).</summary>
    public string? DonorIsoPath { get; set; }

    /// <summary>APK « Google Play Store (Android TV) » facultatif, absent de certaines ISO donneuses.</summary>
    public string? PlayStoreApkPath { get; set; }
}

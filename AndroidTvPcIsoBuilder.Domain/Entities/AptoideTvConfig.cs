namespace AndroidTvPcIsoBuilder.Domain.Entities;

/// <summary>
/// Ajout du magasin d'applications Aptoide TV (indépendant des services Google), installé
/// comme application utilisateur au premier démarrage.
/// </summary>
public class AptoideTvConfig
{
    public bool Enabled { get; set; }

    /// <summary>
    /// APK Aptoide TV à utiliser. Vide : la dernière version est téléchargée depuis le
    /// serveur officiel d'Aptoide au moment de la génération.
    /// </summary>
    public string? ApkPath { get; set; }
}

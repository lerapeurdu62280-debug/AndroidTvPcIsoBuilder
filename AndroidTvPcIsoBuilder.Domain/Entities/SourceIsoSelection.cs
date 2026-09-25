namespace AndroidTvPcIsoBuilder.Domain.Entities;

/// <summary>
/// Trace la source ISO choisie par l'utilisateur (catalogue de IIsoDownloadService) et le
/// chemin local du fichier une fois téléchargé/importé. Remplace le champ transitoire qui
/// n'existait auparavant que côté ViewModel et n'était jamais persisté sur le projet.
/// </summary>
public class SourceIsoSelection
{
    /// <summary>Id de l'IsoDownloadSource choisie dans le catalogue (IIsoDownloadService.GetAvailableSources()). Null si import manuel direct sans passer par le catalogue.</summary>
    public string? SourceId { get; set; }

    public string? DisplayName { get; set; }

    /// <summary>Chemin local du fichier .iso déjà téléchargé/importé, prêt pour IIsoBuilder.BuildAsync.</summary>
    public string? LocalPath { get; set; }

    public string? Sha256 { get; set; }
}

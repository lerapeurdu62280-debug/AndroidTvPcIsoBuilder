namespace AndroidTvPcIsoBuilder.Domain.Entities;

/// <summary>
/// Configuration de l'écran de démarrage animé (bootanimation.zip) affiché par Android
/// entre le bootloader et le lanceur. Remplace le texte de boot kernel qui défile par
/// défaut par un logo animé, personnalisable via une image fournie par l'utilisateur.
/// </summary>
public class BootAnimationConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>Image fournie par l'utilisateur (PNG/JPG). Null = logo par défaut du projet.</summary>
    public string? SourceImagePath { get; set; }

    public int FrameRate { get; set; } = 30;

    public int DurationSeconds { get; set; } = 4;
}

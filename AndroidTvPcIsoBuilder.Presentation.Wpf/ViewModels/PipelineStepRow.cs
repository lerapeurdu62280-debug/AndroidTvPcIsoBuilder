using AndroidTvPcIsoBuilder.Domain.Enums;

namespace AndroidTvPcIsoBuilder.Presentation.Wpf.ViewModels;

/// <summary>
/// DTO d'affichage représentant une ligne du panneau "pipeline d'étapes" de l'écran
/// Compilation en direct. Chaque ligne référence un <see cref="BuildMilestone"/> sous un
/// libellé lisible par l'utilisateur, à l'image de <see cref="AppPackageRow"/>.
/// </summary>
public sealed class PipelineStepRow
{
    public required string Label { get; init; }

    /// <summary>
    /// Jalon de référence de cette ligne : quand la progression réelle atteint (ou dépasse)
    /// ce jalon dans l'ordre de déclaration de <see cref="BuildMilestone"/>, la ligne est
    /// considérée comme complétée.
    /// </summary>
    public required BuildMilestone Milestone { get; init; }

    public bool IsCompleted { get; set; }

    public bool IsActive { get; set; }

    /// <summary>
    /// Construit la liste ordonnée des lignes par défaut du pipeline, regroupant les jalons
    /// techniques de <see cref="BuildMilestone"/> en étapes lisibles pour l'utilisateur.
    /// </summary>
    public static List<PipelineStepRow> CreateDefaultPipeline() => new()
    {
        new PipelineStepRow { Label = "Téléchargement de l'image ISO", Milestone = BuildMilestone.Downloading },
        new PipelineStepRow { Label = "Vérification d'intégrité", Milestone = BuildMilestone.ChecksumVerification },
        new PipelineStepRow { Label = "Intégration des applications", Milestone = BuildMilestone.AppInjection },
        new PipelineStepRow { Label = "Personnalisation du démarrage", Milestone = BuildMilestone.BootAnimationInjection },
        new PipelineStepRow { Label = "Assemblage ISO bootable", Milestone = BuildMilestone.Done },
    };
}

using System.Windows.Forms;

namespace AndroidTvPcIsoBuilder.Presentation.Wpf.Services;

/// <summary>
/// Notifications système via une icône de notification éphémère (approche NotifyIcon,
/// plus simple et fiable ici que l'API Windows.UI.Notifications qui exige un AppUserModelId
/// enregistré et un packaging MSIX pour fonctionner de façon fiable en Debug/portable).
/// </summary>
public class NotificationService : INotificationService
{
    public void ShowBuildSucceeded(string projectName, string outputIsoPath)
        => ShowBalloon("Génération terminée", $"L'ISO du projet '{projectName}' a été générée avec succès.", ToolTipIcon.Info);

    public void ShowBuildFailed(string projectName, string errorSummary)
        => ShowBalloon("Échec de la génération", $"La génération de l'ISO pour '{projectName}' a échoué : {errorSummary}", ToolTipIcon.Error);

    private static void ShowBalloon(string title, string text, ToolTipIcon icon)
    {
        var notifyIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            BalloonTipTitle = title,
            BalloonTipText = text,
            BalloonTipIcon = icon
        };

        notifyIcon.ShowBalloonTip(6000);

        // L'icône doit rester vivante le temps que Windows affiche la bulle ; on la masque ensuite.
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
        timer.Tick += (_, _) =>
        {
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            timer.Stop();
        };
        timer.Start();
    }
}

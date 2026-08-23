namespace AndroidTvPcIsoBuilder.Presentation.Wpf.Services;

public interface INotificationService
{
    void ShowBuildSucceeded(string projectName, string outputIsoPath);
    void ShowBuildFailed(string projectName, string errorSummary);
}

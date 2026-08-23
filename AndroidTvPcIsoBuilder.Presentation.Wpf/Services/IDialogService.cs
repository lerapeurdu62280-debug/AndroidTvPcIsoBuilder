namespace AndroidTvPcIsoBuilder.Presentation.Wpf.Services;

public interface IDialogService
{
    string? PickFolder(string title);
    string? PickFile(string title, string filter);
    string? PickSaveFile(string title, string filter, string defaultFileName);
    void ShowError(string title, string message);
    void ShowInfo(string title, string message);
    bool Confirm(string title, string message);
    string? ShowDownloadIsoDialog();
    string? ShowAppCatalogDialog();
}

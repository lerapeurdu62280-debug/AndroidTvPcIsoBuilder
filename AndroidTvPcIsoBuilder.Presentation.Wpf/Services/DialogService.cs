using System.Windows;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using OpenFolderDialog = Microsoft.Win32.OpenFolderDialog;

namespace AndroidTvPcIsoBuilder.Presentation.Wpf.Services;

public class DialogService : IDialogService
{
    private readonly IServiceProvider _serviceProvider;

    public DialogService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }


    public string? PickFolder(string title)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public string? PickFile(string title, string filter)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickSaveFile(string title, string filter, string defaultFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = defaultFileName
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public void ShowError(string title, string message)
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public void ShowInfo(string title, string message)
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public bool Confirm(string title, string message)
        => MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public string? ShowDownloadIsoDialog()
    {
        var window = (DownloadIsoWindow)_serviceProvider.GetService(typeof(DownloadIsoWindow))!;
        window.Owner = System.Windows.Application.Current.MainWindow;
        var result = window.ShowDialog();
        return result == true ? window.ViewModel.DestinationPath : null;
    }

    public string? ShowAppCatalogDialog()
    {
        var window = (AppCatalogWindow)_serviceProvider.GetService(typeof(AppCatalogWindow))!;
        window.Owner = System.Windows.Application.Current.MainWindow;
        var result = window.ShowDialog();
        return result == true ? window.ViewModel.DownloadedApkPath : null;
    }

    public void OpenUrlInBrowser(Uri url)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url.ToString())
        {
            UseShellExecute = true
        });
    }

    public void ShowBuildWizard()
    {
        var window = (AndroidTvPcIsoBuilder.Presentation.Wpf.Views.BuildWizardWindow)_serviceProvider.GetService(typeof(AndroidTvPcIsoBuilder.Presentation.Wpf.Views.BuildWizardWindow))!;
        window.Owner = System.Windows.Application.Current.MainWindow;
        window.Show();
    }
}

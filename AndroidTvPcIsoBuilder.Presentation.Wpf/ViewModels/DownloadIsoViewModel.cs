using System.IO;
using AndroidTvPcIsoBuilder.Application.Interfaces;
using AndroidTvPcIsoBuilder.Application.Services;
using AndroidTvPcIsoBuilder.Domain.Entities;
using AndroidTvPcIsoBuilder.Presentation.Wpf.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTvPcIsoBuilder.Presentation.Wpf.ViewModels;

/// <summary>
/// Les sources ISO sont hébergées sur SourceForge, qui protège désormais ses téléchargements
/// par un challenge Cloudflare nécessitant l'exécution de JavaScript : un HttpClient classique
/// reste bloqué indéfiniment ou reçoit une page HTML au lieu du fichier. Le téléchargement est
/// donc délégué au navigateur système (qui passe le challenge normalement), et l'utilisateur
/// importe ensuite le fichier obtenu.
/// </summary>
public partial class DownloadIsoViewModel : ObservableObject
{
    private readonly IsoDownloadOrchestrationService _downloadService;
    private readonly IDialogService _dialogService;

    public IReadOnlyList<IsoDownloadSource> Sources { get; }

    [ObservableProperty]
    private IsoDownloadSource? _selectedSource;

    [ObservableProperty]
    private string _destinationPath = string.Empty;

    [ObservableProperty]
    private bool _completedSuccessfully;

    public DownloadIsoViewModel(IsoDownloadOrchestrationService downloadService, IDialogService dialogService)
    {
        _downloadService = downloadService;
        _dialogService = dialogService;
        Sources = _downloadService.GetAvailableSources();
        SelectedSource = Sources.FirstOrDefault();
    }

    [RelayCommand]
    private void OpenDownloadPage()
    {
        if (SelectedSource is null)
        {
            _dialogService.ShowError("Impossible d'ouvrir la page", "Sélectionnez une source à télécharger.");
            return;
        }

        _dialogService.OpenUrlInBrowser(SelectedSource.DownloadUrl);
    }

    [RelayCommand]
    private void ImportDownloadedFile()
    {
        if (SelectedSource is null)
        {
            _dialogService.ShowError("Import impossible", "Sélectionnez une source à télécharger.");
            return;
        }

        var downloadedPath = _dialogService.PickFile(
            "Sélectionnez le fichier téléchargé depuis le navigateur",
            "Image ISO ou archive (*.iso;*.zip)|*.iso;*.zip|Tous les fichiers (*.*)|*.*");
        if (downloadedPath is null)
            return;

        if (!downloadedPath.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
        {
            _dialogService.ShowError(
                "Fichier non pris en charge",
                "Le fichier sélectionné n'est pas une image ISO. Si l'archive téléchargée est une .zip, extrayez d'abord l'ISO qu'elle contient, puis sélectionnez-la ici.");
            return;
        }

        var suggestedName = $"{SelectedSource.Id}.iso";
        var savePath = _dialogService.PickSaveFile("Enregistrer l'image ISO sous", "Image ISO (*.iso)|*.iso", suggestedName);
        if (savePath is null)
            return;

        try
        {
            File.Copy(downloadedPath, savePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("Import impossible", $"Impossible de copier le fichier vers l'emplacement choisi : {ex.Message}");
            return;
        }

        DestinationPath = savePath;
        CompletedSuccessfully = true;
        _dialogService.ShowInfo("Import terminé", "L'image ISO a bien été importée.");
    }
}

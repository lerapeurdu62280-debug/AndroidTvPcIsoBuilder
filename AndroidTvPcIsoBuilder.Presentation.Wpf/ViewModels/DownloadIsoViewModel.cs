using AndroidTvPcIsoBuilder.Application.Interfaces;
using AndroidTvPcIsoBuilder.Application.Services;
using AndroidTvPcIsoBuilder.Domain.Entities;
using AndroidTvPcIsoBuilder.Presentation.Wpf.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTvPcIsoBuilder.Presentation.Wpf.ViewModels;

public partial class DownloadIsoViewModel : ObservableObject
{
    private readonly IsoDownloadOrchestrationService _downloadService;
    private readonly IDialogService _dialogService;

    private CancellationTokenSource? _downloadCancellation;

    public IReadOnlyList<IsoDownloadSource> Sources { get; }

    [ObservableProperty]
    private IsoDownloadSource? _selectedSource;

    [ObservableProperty]
    private string _destinationPath = string.Empty;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private int _downloadPercent;

    [ObservableProperty]
    private string _downloadStatus = string.Empty;

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
    private void BrowseDestination()
    {
        var fileName = SelectedSource is not null ? $"{SelectedSource.Id}.iso" : "android.iso";
        var path = _dialogService.PickSaveFile("Enregistrer l'image ISO téléchargée sous", "Image ISO (*.iso)|*.iso", fileName);
        if (path is not null)
            DestinationPath = path;
    }

    [RelayCommand]
    private async Task StartDownloadAsync()
    {
        if (SelectedSource is null)
        {
            _dialogService.ShowError("Téléchargement impossible", "Sélectionnez une source à télécharger.");
            return;
        }

        if (string.IsNullOrWhiteSpace(DestinationPath))
        {
            _dialogService.ShowError("Téléchargement impossible", "Choisissez un emplacement de destination.");
            return;
        }

        IsDownloading = true;
        CompletedSuccessfully = false;
        DownloadPercent = 0;
        DownloadStatus = "Connexion...";

        _downloadCancellation = new CancellationTokenSource();
        var progress = new Progress<DownloadProgress>(p =>
        {
            DownloadPercent = p.PercentComplete ?? 0;
            DownloadStatus = p.TotalBytes is long total
                ? $"{FormatBytes(p.BytesReceived)} / {FormatBytes(total)}"
                : FormatBytes(p.BytesReceived);
        });

        try
        {
            var result = await _downloadService.DownloadAsync(SelectedSource, DestinationPath, progress, _downloadCancellation.Token);

            if (!result.IsSuccess)
            {
                _dialogService.ShowError("Échec du téléchargement", string.Join(Environment.NewLine, result.Errors));
                return;
            }

            DownloadStatus = "Téléchargement terminé.";
            CompletedSuccessfully = true;
        }
        finally
        {
            IsDownloading = false;
            _downloadCancellation.Dispose();
            _downloadCancellation = null;
        }
    }

    [RelayCommand]
    private void CancelDownload()
    {
        _downloadCancellation?.Cancel();
    }

    private static string FormatBytes(long bytes)
    {
        double value = bytes;
        string[] units = { "o", "Ko", "Mo", "Go" };
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }
        return $"{value:0.#} {units[unitIndex]}";
    }
}

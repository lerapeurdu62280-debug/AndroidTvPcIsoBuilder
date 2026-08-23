using System.IO;
using AndroidTvPcIsoBuilder.Application.Interfaces;
using AndroidTvPcIsoBuilder.Application.Services;
using AndroidTvPcIsoBuilder.Domain.Entities;
using AndroidTvPcIsoBuilder.Presentation.Wpf.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTvPcIsoBuilder.Presentation.Wpf.ViewModels;

public partial class AppCatalogViewModel : ObservableObject
{
    private readonly AppCatalogOrchestrationService _catalogService;
    private readonly IDialogService _dialogService;

    private CancellationTokenSource? _downloadCancellation;

    public IReadOnlyList<AppCatalogEntry> Apps { get; }

    [ObservableProperty]
    private AppCatalogEntry? _selectedApp;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private int _downloadPercent;

    [ObservableProperty]
    private string _downloadStatus = string.Empty;

    [ObservableProperty]
    private bool _completedSuccessfully;

    public string? DownloadedApkPath { get; private set; }

    public AppCatalogViewModel(AppCatalogOrchestrationService catalogService, IDialogService dialogService)
    {
        _catalogService = catalogService;
        _dialogService = dialogService;
        Apps = _catalogService.GetAvailableApps();
        SelectedApp = Apps.FirstOrDefault();
    }

    [RelayCommand]
    private async Task StartDownloadAsync()
    {
        if (SelectedApp is null)
        {
            _dialogService.ShowError("Téléchargement impossible", "Sélectionnez une application à télécharger.");
            return;
        }

        IsDownloading = true;
        CompletedSuccessfully = false;
        DownloadPercent = 0;
        DownloadStatus = "Résolution de la dernière version...";

        var destinationDirectory = Path.Combine(Path.GetTempPath(), "AndroidTvPcIsoBuilder", "Apps");

        _downloadCancellation = new CancellationTokenSource();
        var progress = new Progress<FileDownloadProgress>(p =>
        {
            DownloadPercent = p.PercentComplete ?? 0;
            DownloadStatus = p.TotalBytes is long total
                ? $"{FormatBytes(p.BytesReceived)} / {FormatBytes(total)}"
                : FormatBytes(p.BytesReceived);
        });

        try
        {
            var result = await _catalogService.DownloadAsync(SelectedApp, destinationDirectory, progress, _downloadCancellation.Token);

            if (!result.IsSuccess)
            {
                _dialogService.ShowError("Échec du téléchargement", string.Join(Environment.NewLine, result.Errors));
                return;
            }

            DownloadedApkPath = result.Value;
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

using AndroidTvPcIsoBuilder.Application.Services;
using AndroidTvPcIsoBuilder.Domain.Entities;
using AndroidTvPcIsoBuilder.Domain.Enums;
using BootMode = AndroidTvPcIsoBuilder.Domain.Enums.BootMode;
using AndroidTvPcIsoBuilder.Presentation.Wpf.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTvPcIsoBuilder.Presentation.Wpf.ViewModels;

/// <summary>
/// Écran "Cible & source ISO" : choix de la source ISO Android TV officielle à
/// télécharger/importer (catalogue IIsoDownloadService), du mode de boot, et du logo
/// de démarrage animé personnalisé (optionnel).
/// </summary>
public partial class TargetSelectionViewModel : ObservableObject
{
    private readonly IsoDownloadOrchestrationService _downloadService;
    private readonly IDialogService _dialogService;

    public IReadOnlyList<IsoDownloadSource> AvailableSources { get; }
    public IReadOnlyList<BootMode> BootModeOptions { get; } = Enum.GetValues<BootMode>();

    [ObservableProperty]
    private IsoDownloadSource? _selectedSource;

    [ObservableProperty]
    private BootMode _bootMode = BootMode.Hybrid;

    [ObservableProperty]
    private string? _customLogoPath;

    /// <summary>Chemin local de l'ISO une fois téléchargée/importée via le sous-dialogue (DownloadIsoWindow).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private string? _importedIsoLocalPath;

    [ObservableProperty]
    private bool _isDownloadStepCompleted;

    public event Action? NextRequested;

    public TargetSelectionViewModel(IsoDownloadOrchestrationService downloadService, IDialogService dialogService)
    {
        _downloadService = downloadService;
        _dialogService = dialogService;
        AvailableSources = _downloadService.GetAvailableSources();
        _selectedSource = AvailableSources.FirstOrDefault();
    }

    public Task LoadAsync() => Task.CompletedTask;

    /// <summary>Construit la sélection de source ISO à partir de l'état courant.</summary>
    public SourceIsoSelection BuildSourceIsoSelection() => new()
    {
        SourceId = SelectedSource?.Id,
        DisplayName = SelectedSource?.DisplayName,
        LocalPath = ImportedIsoLocalPath,
        Sha256 = SelectedSource?.Sha256,
    };

    /// <summary>
    /// Construit la configuration du logo de démarrage animé à partir du chemin d'image
    /// éventuellement choisi par l'utilisateur.
    /// </summary>
    public BootAnimationConfig BuildBootAnimationConfig() => new()
    {
        SourceImagePath = CustomLogoPath,
    };

    [RelayCommand]
    private void BrowseCustomLogo()
    {
        var path = _dialogService.PickFile("Sélectionnez le logo de démarrage", "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg");
        if (path is not null)
            CustomLogoPath = path;
    }

    [RelayCommand]
    private void OpenDownloadDialog()
    {
        var path = _dialogService.ShowDownloadIsoDialog();
        if (path is null)
            return;

        ImportedIsoLocalPath = path;
        IsDownloadStepCompleted = true;
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next() => NextRequested?.Invoke();

    private bool CanGoNext() => !string.IsNullOrEmpty(ImportedIsoLocalPath);
}

using System.Collections.ObjectModel;
using System.IO;
using AndroidTvPcIsoBuilder.Application.Interfaces;
using AndroidTvPcIsoBuilder.Application.Services;
using AndroidTvPcIsoBuilder.Domain.Entities;
using AndroidTvPcIsoBuilder.Domain.Enums;
using BootMode = AndroidTvPcIsoBuilder.Domain.Enums.BootMode;
using AndroidTvPcIsoBuilder.Presentation.Wpf.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTvPcIsoBuilder.Presentation.Wpf.ViewModels;

public partial class ProjectEditorViewModel : ObservableObject
{
    private readonly Guid _projectId;
    private readonly ProjectService _projectService;
    private readonly AppPackageService _appPackageService;
    private readonly BuildOrchestrationService _buildOrchestrationService;
    private readonly IDialogService _dialogService;
    private readonly INotificationService _notificationService;

    private CancellationTokenSource? _buildCancellation;

    public ObservableCollection<AppPackageRow> Apps { get; } = new();
    public ObservableCollection<BuildHistoryEntry> BuildHistory { get; } = new();

    public IReadOnlyList<BootMode> BootModeOptions { get; } = Enum.GetValues<BootMode>();

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _sourceIsoPath = string.Empty;

    [ObservableProperty]
    private string _outputIsoPath = string.Empty;

    [ObservableProperty]
    private string _resolution = "1920x1080";

    [ObservableProperty]
    private string _language = "fr-FR";

    [ObservableProperty]
    private BootMode _bootMode;

    [ObservableProperty]
    private bool _isLoaded;

    [ObservableProperty]
    private bool _isBuilding;

    [ObservableProperty]
    private int _buildPercent;

    [ObservableProperty]
    private string _buildStep = string.Empty;

    [ObservableProperty]
    private string _buildMessage = string.Empty;

    [ObservableProperty]
    private string _previewSummary = string.Empty;

    [ObservableProperty]
    private bool _hasPreviewWarnings;

    public ProjectEditorViewModel(
        Guid projectId,
        ProjectService projectService,
        AppPackageService appPackageService,
        BuildOrchestrationService buildOrchestrationService,
        IDialogService dialogService,
        INotificationService notificationService)
    {
        _projectId = projectId;
        _projectService = projectService;
        _appPackageService = appPackageService;
        _buildOrchestrationService = buildOrchestrationService;
        _dialogService = dialogService;
        _notificationService = notificationService;
    }

    public async Task LoadAsync()
    {
        var result = await _projectService.GetProjectAsync(_projectId);
        if (!result.IsSuccess)
        {
            _dialogService.ShowError("Projet introuvable", string.Join(Environment.NewLine, result.Errors));
            return;
        }

        var project = result.Value;
        Name = project.Name;
        OutputIsoPath = project.OutputIsoPath;
        Resolution = project.Resolution;
        Language = project.Language;
        BootMode = project.BootMode;
        SourceIsoPath = project.SourceIso.LocalPath ?? string.Empty;

        Apps.Clear();
        foreach (var app in project.Apps)
        {
            Apps.Add(new AppPackageRow
            {
                Name = app.Name,
                SourceApkPath = app.SourceApkPath,
                IsPreinstalled = app.IsPreinstalled
            });
        }

        BuildHistory.Clear();
        foreach (var entry in project.BuildHistory)
            BuildHistory.Add(entry);

        IsLoaded = true;

        await RefreshPreviewAsync();
    }

    private async Task RefreshPreviewAsync()
    {
        var previewResult = await _buildOrchestrationService.GetPreviewAsync(_projectId, SourceIsoPath);
        if (!previewResult.IsSuccess)
        {
            PreviewSummary = string.Empty;
            HasPreviewWarnings = false;
            return;
        }

        var preview = previewResult.Value;
        PreviewSummary = $"Taille estimée de l'ISO : {FormatBytes(preview.EstimatedOutputSizeBytes)} " +
                          $"({FormatBytes(preview.SourceSizeBytes)} de base + {preview.AppCount} application(s), {FormatBytes(preview.AppsSizeBytes)})";
        HasPreviewWarnings = preview.Warnings.Count > 0;
        if (HasPreviewWarnings)
            PreviewSummary += Environment.NewLine + string.Join(Environment.NewLine, preview.Warnings.Select(w => $"⚠ {w}"));
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var result = await _projectService.UpdateProjectSettingsAsync(
            _projectId,
            name: Name,
            outputIsoPath: OutputIsoPath,
            resolution: Resolution,
            language: Language,
            bootMode: BootMode,
            sourceIsoLocalPath: string.IsNullOrWhiteSpace(SourceIsoPath) ? null : SourceIsoPath);

        if (!result.IsSuccess)
        {
            _dialogService.ShowError("Enregistrement impossible", string.Join(Environment.NewLine, result.Errors));
            return;
        }

        await RefreshPreviewAsync();
        _dialogService.ShowInfo("Projet enregistré", "Les paramètres du projet ont été enregistrés.");
    }

    [RelayCommand]
    private void BrowseSourceIsoPath()
    {
        var path = _dialogService.PickFile("Sélectionnez l'image ISO source Android TV", "Image ISO (*.iso)|*.iso");
        if (path is not null)
            SourceIsoPath = path;
    }

    [RelayCommand]
    private void BrowseOutputIsoPath()
    {
        var path = _dialogService.PickSaveFile("Emplacement de l'ISO à générer", "Image ISO (*.iso)|*.iso", "AndroidTv.iso");
        if (path is not null)
            OutputIsoPath = path;
    }

    [RelayCommand]
    private async Task AddAppAsync()
    {
        var apkPath = _dialogService.PickFile("Sélectionnez un fichier APK", "Application Android (*.apk)|*.apk");
        if (apkPath is null)
            return;

        await AddAppFromPathAsync(apkPath, Path.GetFileNameWithoutExtension(apkPath));
    }

    [RelayCommand]
    private async Task AddAppFromCatalogAsync()
    {
        var apkPath = _dialogService.ShowAppCatalogDialog();
        if (apkPath is null)
            return;

        await AddAppFromPathAsync(apkPath, Path.GetFileNameWithoutExtension(apkPath));
    }

    public async Task AddAppFromPathAsync(string apkPath, string name)
    {
        var result = await _appPackageService.AddAppAsync(_projectId, name, apkPath);
        if (!result.IsSuccess)
        {
            _dialogService.ShowError("Ajout de l'application impossible", string.Join(Environment.NewLine, result.Errors));
            return;
        }

        Apps.Add(new AppPackageRow
        {
            Name = result.Value.Name,
            SourceApkPath = result.Value.SourceApkPath,
            IsPreinstalled = result.Value.IsPreinstalled
        });

        await RefreshPreviewAsync();
    }

    [RelayCommand]
    private async Task RemoveAppAsync(AppPackageRow? app)
    {
        if (app is null)
            return;

        var result = await _appPackageService.RemoveAppAsync(_projectId, app.SourceApkPath);
        if (!result.IsSuccess)
        {
            _dialogService.ShowError("Suppression impossible", string.Join(Environment.NewLine, result.Errors));
            return;
        }

        Apps.Remove(app);
        await RefreshPreviewAsync();
    }

    [RelayCommand]
    private async Task BuildAsync()
    {
        IsBuilding = true;
        BuildPercent = 0;
        BuildStep = string.Empty;
        BuildMessage = string.Empty;

        _buildCancellation = new CancellationTokenSource();
        var progress = new Progress<BuildProgress>(p =>
        {
            BuildStep = p.Step;
            BuildPercent = p.PercentComplete;
            BuildMessage = p.Message ?? string.Empty;
        });

        try
        {
            var result = await _buildOrchestrationService.BuildAsync(_projectId, SourceIsoPath, resolvedDriverFilePaths: null, progress, _buildCancellation.Token);

            if (!result.IsSuccess)
            {
                _dialogService.ShowError("Échec de la génération", string.Join(Environment.NewLine, result.Errors));
                _notificationService.ShowBuildFailed(Name, string.Join(" ", result.Errors));
                return;
            }

            _dialogService.ShowInfo("Génération terminée", $"L'ISO a été générée : {OutputIsoPath}");
            _notificationService.ShowBuildSucceeded(Name, OutputIsoPath);
        }
        finally
        {
            IsBuilding = false;
            _buildCancellation.Dispose();
            _buildCancellation = null;

            await LoadAsync();
        }
    }

    [RelayCommand]
    private void CancelBuild()
    {
        _buildCancellation?.Cancel();
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

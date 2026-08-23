using System.Collections.ObjectModel;
using System.IO;
using AndroidTvPcIsoBuilder.Application.Services;
using AndroidTvPcIsoBuilder.Domain.Enums;
using AndroidTvPcIsoBuilder.Presentation.Wpf.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTvPcIsoBuilder.Presentation.Wpf.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ProjectService _projectService;
    private readonly IDialogService _dialogService;
    private readonly Func<Guid, ProjectEditorViewModel> _editorFactory;

    private List<ProjectSummary> _allProjects = new();

    public ObservableCollection<ProjectSummary> Projects { get; } = new();

    [ObservableProperty]
    private ProjectSummary? _selectedProject;

    [ObservableProperty]
    private ProjectEditorViewModel? _currentEditor;

    [ObservableProperty]
    private string _searchText = string.Empty;

    public MainViewModel(
        ProjectService projectService,
        IDialogService dialogService,
        Func<Guid, ProjectEditorViewModel> editorFactory)
    {
        _projectService = projectService;
        _dialogService = dialogService;
        _editorFactory = editorFactory;
    }

    public async Task InitializeAsync()
    {
        await ReloadProjectsAsync();
    }

    private async Task ReloadProjectsAsync()
    {
        var projects = await _projectService.GetAllProjectsAsync();

        _allProjects = projects
            .OrderBy(p => p.Name)
            .Select(project => new ProjectSummary
            {
                Id = project.Id,
                Name = project.Name,
                BaseSystem = project.BaseSystem.ToString(),
                OutputIsoPath = project.OutputIsoPath
            })
            .ToList();

        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var previouslySelectedId = SelectedProject?.Id;

        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _allProjects
            : _allProjects.Where(p => p.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToList();

        Projects.Clear();
        foreach (var project in filtered)
            Projects.Add(project);

        if (previouslySelectedId is not null)
            SelectedProject = Projects.FirstOrDefault(p => p.Id == previouslySelectedId);
    }

    [RelayCommand]
    private async Task CreateProjectAsync()
    {
        var sourcePath = _dialogService.PickFile("Sélectionnez l'image ISO source Android TV", "Image ISO (*.iso)|*.iso");
        if (sourcePath is null)
            return;

        var outputPath = _dialogService.PickSaveFile(
            "Emplacement de l'ISO à générer",
            "Image ISO (*.iso)|*.iso",
            "AndroidTv.iso");
        if (outputPath is null)
            return;

        await CreateProjectFromSourceAsync(sourcePath, outputPath);
    }

    private async Task CreateProjectFromSourceAsync(string sourcePath, string outputPath)
    {
        var result = await _projectService.CreateProjectAsync(
            name: Path.GetFileNameWithoutExtension(sourcePath),
            sourcePath: sourcePath,
            outputIsoPath: outputPath,
            baseSystem: BaseSystemType.AndroidTvX86);

        if (!result.IsSuccess)
        {
            _dialogService.ShowError("Création du projet impossible", string.Join(Environment.NewLine, result.Errors));
            return;
        }

        await ReloadProjectsAsync();
        SelectedProject = Projects.FirstOrDefault(p => p.Id == result.Value.Id);
    }

    [RelayCommand]
    private async Task DownloadIsoAsync()
    {
        var downloadedIsoPath = _dialogService.ShowDownloadIsoDialog();
        if (downloadedIsoPath is null)
            return;

        if (!_dialogService.Confirm("Créer un projet", "Créer un nouveau projet à partir de l'image téléchargée ?"))
            return;

        var outputPath = _dialogService.PickSaveFile(
            "Emplacement de l'ISO à générer",
            "Image ISO (*.iso)|*.iso",
            "AndroidTv.iso");
        if (outputPath is null)
            return;

        await CreateProjectFromSourceAsync(downloadedIsoPath, outputPath);
    }

    [RelayCommand]
    private async Task ExportProjectAsync()
    {
        if (SelectedProject is null)
            return;

        var exportPath = _dialogService.PickSaveFile(
            "Exporter le projet vers",
            "Projet ISO Builder (*.json)|*.json",
            $"{SelectedProject.Name}.json");
        if (exportPath is null)
            return;

        var result = await _projectService.ExportProjectAsync(SelectedProject.Id, exportPath);
        if (!result.IsSuccess)
        {
            _dialogService.ShowError("Export impossible", string.Join(Environment.NewLine, result.Errors));
            return;
        }

        _dialogService.ShowInfo("Projet exporté", $"Le projet a été exporté vers {exportPath}");
    }

    [RelayCommand]
    private async Task ImportProjectAsync()
    {
        var importPath = _dialogService.PickFile("Importer un projet", "Projet ISO Builder (*.json)|*.json");
        if (importPath is null)
            return;

        var result = await _projectService.ImportProjectAsync(importPath);
        if (!result.IsSuccess)
        {
            _dialogService.ShowError("Import impossible", string.Join(Environment.NewLine, result.Errors));
            return;
        }

        await ReloadProjectsAsync();
        SelectedProject = Projects.FirstOrDefault(p => p.Id == result.Value.Id);
    }

    [RelayCommand]
    private async Task DeleteProjectAsync()
    {
        if (SelectedProject is null)
            return;

        if (!_dialogService.Confirm("Supprimer le projet", $"Supprimer définitivement le projet '{SelectedProject.Name}' ?"))
            return;

        await _projectService.DeleteProjectAsync(SelectedProject.Id);
        CurrentEditor = null;
        await ReloadProjectsAsync();
    }

    partial void OnSelectedProjectChanged(ProjectSummary? value)
    {
        CurrentEditor = value is null ? null : _editorFactory(value.Id);
        if (CurrentEditor is not null)
        {
            _ = CurrentEditor.LoadAsync();
        }
    }

    public async Task RefreshAfterSaveAsync()
    {
        await ReloadProjectsAsync();
    }
}

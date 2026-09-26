using System.IO;
using AndroidTvPcIsoBuilder.Application.Interfaces;
using AndroidTvPcIsoBuilder.Application.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AndroidTvPcIsoBuilder.Presentation.Wpf.ViewModels;

/// <summary>
/// Orchestre les étapes du wizard de création d'un projet Android TV : Accueil, Cible
/// &amp; source ISO, puis Compilation en direct. Le contenu
/// affiché change via <see cref="CurrentStep"/>, piloté par des DataTemplate par
/// ViewModel (même pattern que MainWindow/CurrentEditor).
/// </summary>
public partial class BuildWizardViewModel : ObservableObject
{
    private readonly HomeViewModel _homeViewModel;
    private readonly TargetSelectionViewModel _targetSelectionViewModel;
    private readonly LiveBuildViewModel _liveBuildViewModel;
    private readonly ProjectService _projectService;
    private readonly IProjectRepository _projectRepository;

    [ObservableProperty]
    private ObservableObject _currentStep;

    public BuildWizardViewModel(
        HomeViewModel homeViewModel,
        TargetSelectionViewModel targetSelectionViewModel,
        LiveBuildViewModel liveBuildViewModel,
        ProjectService projectService,
        IProjectRepository projectRepository)
    {
        _homeViewModel = homeViewModel;
        _targetSelectionViewModel = targetSelectionViewModel;
        _liveBuildViewModel = liveBuildViewModel;
        _projectService = projectService;
        _projectRepository = projectRepository;

        _homeViewModel.StartRequested += OnStartRequested;
        _targetSelectionViewModel.NextRequested += OnTargetSelectionNextRequested;

        _currentStep = _homeViewModel;
    }

    private async void OnStartRequested()
    {
        CurrentStep = _targetSelectionViewModel;
        await _targetSelectionViewModel.LoadAsync();
    }

    /// <summary>
    /// Cible &amp; source ISO validée : crée le projet à partir de la source ISO et de la
    /// bootanimation choisies, puis bascule vers l'écran de compilation en direct. Le projet est créé à la volée (nom générique,
    /// chemin de sortie dans le dossier temporaire utilisateur) puisqu'il n'existe pas
    /// encore d'écran dédié à la saisie du nom de projet/chemin de sortie dans ce wizard.
    /// </summary>
    private async void OnTargetSelectionNextRequested()
    {
        var sourceIsoSelection = _targetSelectionViewModel.BuildSourceIsoSelection();
        var bootAnimationConfig = _targetSelectionViewModel.BuildBootAnimationConfig();
        var bootMode = _targetSelectionViewModel.BootMode;

        var projectName = $"Projet Android TV {DateTime.Now:yyyy-MM-dd HH:mm}";
        // L'ISO produite est rangée à côté de l'ISO source (Documents si la source reste à
        // télécharger), jamais dans le dossier temporaire où l'utilisateur ne la trouverait pas.
        var outputDirectory = Path.GetDirectoryName(sourceIsoSelection.LocalPath);
        if (string.IsNullOrEmpty(outputDirectory))
            outputDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var outputIsoPath = Path.Combine(outputDirectory, $"AndroidTV-{DateTime.Now:yyyyMMdd-HHmmss}.iso");

        var createResult = await _projectService.CreateProjectAsync(projectName, outputIsoPath);
        if (!createResult.IsSuccess)
            return;

        var project = createResult.Value;
        project.SourceIso = sourceIsoSelection;
        project.BootAnimation = bootAnimationConfig;
        project.BootMode = bootMode;
        await _projectRepository.SaveAsync(project);

        _liveBuildViewModel.ProjectId = project.Id;
        _liveBuildViewModel.OutputIsoPath = outputIsoPath;
        CurrentStep = _liveBuildViewModel;
        await _liveBuildViewModel.StartBuildCommand.ExecuteAsync(null);
    }
}

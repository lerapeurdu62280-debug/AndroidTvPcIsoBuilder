using System.Collections.ObjectModel;
using System.Windows;
using AndroidTvPcIsoBuilder.Application.Services;
using AndroidTvPcIsoBuilder.Domain.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTvPcIsoBuilder.Presentation.Wpf.ViewModels;

/// <summary>
/// Écran "Compilation en direct" (3e étape du wizard) : pilote la génération de l'image
/// via <see cref="IsoAssemblyPipelineService"/> (téléchargement/import ISO ->
/// apps -> bootanimation -> assemblage -> vérification), et alimente la visualisation
/// live (pipeline d'étapes, terminal de logs, vitesse de téléchargement, pourcentage
/// global) à partir des <see cref="IsoAssemblyProgress"/> reçus.
/// </summary>
public partial class LiveBuildViewModel : ObservableObject
{
    /// <summary>Nombre maximal de lignes conservées dans le terminal de logs.</summary>
    private const int MaxLogLines = 500;

    private readonly IsoAssemblyPipelineService _pipelineService;

    private CancellationTokenSource? _buildCancellation;
    private DateTimeOffset _startedAt;

    /// <summary>
    /// Identifiant du projet à assembler, assigné par le ViewModel parent (wizard) avant
    /// d'appeler <see cref="StartBuildCommand"/>.
    /// </summary>
    public Guid? ProjectId { get; set; }

    public ObservableCollection<PipelineStepRow> PipelineSteps { get; } = new(PipelineStepRow.CreateDefaultPipeline());

    public ObservableCollection<string> LogLines { get; } = new();

    [ObservableProperty]
    private int _overallPercent;

    [ObservableProperty]
    private string _currentMilestoneLabel = string.Empty;

    [ObservableProperty]
    private bool _isBuildRunning;

    [ObservableProperty]
    private long _bytesReceived;

    [ObservableProperty]
    private long? _totalBytes;

    [ObservableProperty]
    private double _downloadSpeedBytesPerSecond;

    [ObservableProperty]
    private string _etaDisplay = "--:--";

    [ObservableProperty]
    private string _elapsedTimeDisplay = "00:00:00";

    /// <summary>Déclenché à la fin du build, succès ou échec, pour que le parent puisse réagir.</summary>
    public event Action<bool>? BuildCompleted;

    public LiveBuildViewModel(IsoAssemblyPipelineService pipelineService)
    {
        _pipelineService = pipelineService;
    }

    /// <summary>
    /// Hauteur (en étoiles) de la zone "interface Android TV déjà construite" dans la
    /// visualisation live, proportionnelle à <see cref="OverallPercent"/>.
    /// </summary>
    public GridLength BuiltRowHeight => new(OverallPercent, GridUnitType.Star);

    /// <summary>Hauteur complémentaire de la zone "pas encore construite" (wireframe).</summary>
    public GridLength UnbuiltRowHeight => new(100 - OverallPercent, GridUnitType.Star);

    partial void OnOverallPercentChanged(int value)
    {
        OnPropertyChanged(nameof(BuiltRowHeight));
        OnPropertyChanged(nameof(UnbuiltRowHeight));
    }

    [RelayCommand(CanExecute = nameof(CanStartBuild))]
    private async Task StartBuildAsync()
    {
        if (ProjectId is null)
            return;

        IsBuildRunning = true;
        OverallPercent = 0;
        CurrentMilestoneLabel = string.Empty;
        LogLines.Clear();
        _startedAt = DateTimeOffset.Now;
        ElapsedTimeDisplay = "00:00:00";
        EtaDisplay = "--:--";
        BytesReceived = 0;
        TotalBytes = null;
        DownloadSpeedBytesPerSecond = 0;

        foreach (var step in PipelineSteps)
        {
            step.IsCompleted = false;
            step.IsActive = false;
        }

        _buildCancellation = new CancellationTokenSource();

        var progress = new Progress<IsoAssemblyProgress>(OnProgressReported);

        var succeeded = false;
        try
        {
            var result = await _pipelineService.RunAsync(ProjectId.Value, progress, _buildCancellation.Token);
            succeeded = result.IsSuccess;
        }
        finally
        {
            IsBuildRunning = false;
            _buildCancellation?.Dispose();
            _buildCancellation = null;
            BuildCompleted?.Invoke(succeeded);
        }
    }

    private bool CanStartBuild() => !IsBuildRunning && ProjectId is not null;

    [RelayCommand(CanExecute = nameof(CanCancelBuild))]
    private void CancelBuild()
    {
        _buildCancellation?.Cancel();
    }

    private bool CanCancelBuild() => IsBuildRunning;

    partial void OnIsBuildRunningChanged(bool value)
    {
        StartBuildCommand.NotifyCanExecuteChanged();
        CancelBuildCommand.NotifyCanExecuteChanged();
    }

    private void OnProgressReported(IsoAssemblyProgress progress)
    {
        OverallPercent = Math.Clamp(progress.PercentComplete, 0, 100);
        CurrentMilestoneLabel = GetMilestoneLabel(progress.Milestone);

        _startedAt = _startedAt == default ? DateTimeOffset.Now : _startedAt;
        var elapsed = DateTimeOffset.Now - _startedAt;
        ElapsedTimeDisplay = elapsed.ToString(@"hh\:mm\:ss");

        if (!string.IsNullOrEmpty(progress.LogLine))
        {
            LogLines.Add(progress.LogLine);
            while (LogLines.Count > MaxLogLines)
                LogLines.RemoveAt(0);
        }

        UpdatePipelineSteps(progress.Milestone);

        if (progress.BytesReceived is not null)
        {
            BytesReceived = progress.BytesReceived.Value;
            TotalBytes = progress.TotalBytes;

            if (progress.DownloadSpeedBytesPerSecond is { } speed && speed > 0)
            {
                DownloadSpeedBytesPerSecond = speed;
                if (TotalBytes is { } total && total > BytesReceived)
                {
                    var remainingSeconds = (total - BytesReceived) / speed;
                    EtaDisplay = TimeSpan.FromSeconds(remainingSeconds).ToString(@"mm\:ss");
                }
            }
        }
    }

    private void UpdatePipelineSteps(BuildMilestone currentMilestone)
    {
        foreach (var step in PipelineSteps)
        {
            step.IsCompleted = step.Milestone < currentMilestone;
            step.IsActive = step.Milestone == currentMilestone;
        }

        // Si le jalon courant se situe entre deux lignes du pipeline regroupé, on marque
        // active la première ligne dont le jalon n'est pas encore atteint afin que
        // l'utilisateur voie toujours une étape "en cours".
        if (!PipelineSteps.Any(s => s.IsActive))
        {
            var nextPending = PipelineSteps.FirstOrDefault(s => s.Milestone > currentMilestone);
            if (nextPending is not null)
                nextPending.IsActive = true;
        }
    }

    private static string GetMilestoneLabel(BuildMilestone milestone) => milestone switch
    {
        BuildMilestone.Starting => "Démarrage",
        BuildMilestone.Downloading => "Téléchargement de l'image ISO",
        BuildMilestone.ChecksumVerification => "Vérification de l'intégrité (SHA256)",
        BuildMilestone.ReadingSourceImage => "Lecture de l'image source",
        BuildMilestone.AppInjection => "Intégration des applications",
        BuildMilestone.BootAnimationInjection => "Personnalisation de l'écran de démarrage",
        BuildMilestone.IsoAssembly => "Assemblage de l'image ISO",
        BuildMilestone.BootCatalogRestore => "Restauration du catalogue de boot",
        BuildMilestone.Verification => "Vérification finale",
        BuildMilestone.Done => "Terminé",
        _ => milestone.ToString(),
    };
}

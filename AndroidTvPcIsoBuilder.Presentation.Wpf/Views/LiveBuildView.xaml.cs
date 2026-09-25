using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AndroidTvPcIsoBuilder.Presentation.Wpf.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace AndroidTvPcIsoBuilder.Presentation.Wpf.Views;

/// <summary>
/// Code-behind de l'écran "Compilation en direct". Contient uniquement de la mécanique
/// UI pure, volontairement gardée hors du ViewModel :
/// - Démarrage/arrêt de la Storyboard de la ligne de scan en fonction de
///   <see cref="LiveBuildViewModel.IsBuildRunning"/> : on écoute PropertyChanged sur le
///   DataContext plutôt que d'utiliser un DataTrigger/EventTrigger XAML, car piloter
///   Begin()/Stop() manuellement est plus simple à lire et évite les subtilités de cycle
///   de vie des Storyboard déclenchées par trigger (portée, réentrance) pour une
///   animation aussi simple qu'une boucle infinie sur une TranslateTransform.
/// - Auto-scroll du terminal de logs : on écoute CollectionChanged sur LogLines et on
///   scrolle vers le dernier élément ajouté, ce qui est la façon la plus simple et
///   robuste d'obtenir un auto-scroll avec un ListBox WPF standard.
/// </summary>
public partial class LiveBuildView : UserControl
{
    private Storyboard? _scanLineStoryboard;

    public LiveBuildView()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _scanLineStoryboard ??= BuildScanLineStoryboard();

        if (DataContext is LiveBuildViewModel viewModel)
        {
            viewModel.LogLines.CollectionChanged += OnLogLinesChanged;
            if (viewModel.IsBuildRunning)
                _scanLineStoryboard.Begin(this, isControllable: true);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _scanLineStoryboard?.Stop(this);

        if (DataContext is LiveBuildViewModel viewModel)
            viewModel.LogLines.CollectionChanged -= OnLogLinesChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is LiveBuildViewModel oldViewModel)
        {
            oldViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            oldViewModel.LogLines.CollectionChanged -= OnLogLinesChanged;
        }

        if (e.NewValue is LiveBuildViewModel newViewModel)
        {
            newViewModel.PropertyChanged += OnViewModelPropertyChanged;
            newViewModel.LogLines.CollectionChanged += OnLogLinesChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LiveBuildViewModel.IsBuildRunning) || sender is not LiveBuildViewModel viewModel)
            return;

        _scanLineStoryboard ??= BuildScanLineStoryboard();

        if (viewModel.IsBuildRunning)
            _scanLineStoryboard.Begin(this, isControllable: true);
        else
            _scanLineStoryboard.Stop(this);
    }

    private void OnLogLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add)
            return;

        // ScrollIntoView suffit pour un auto-scroll fiable avec un ListBox standard, sans
        // avoir besoin d'accéder au ScrollViewer interne.
        if (LogListBox.Items.Count > 0)
            LogListBox.ScrollIntoView(LogListBox.Items[^1]);
    }

    /// <summary>
    /// Construit la Storyboard de la ligne de scan : translation verticale en boucle
    /// infinie sur toute la hauteur de l'écran de device simulé.
    /// </summary>
    private Storyboard BuildScanLineStoryboard()
    {
        var animation = new DoubleAnimation
        {
            From = 0,
            To = DeviceScreenBorder.MaxHeight,
            Duration = new Duration(TimeSpan.FromSeconds(2.4)),
            RepeatBehavior = RepeatBehavior.Forever,
        };

        Storyboard.SetTarget(animation, ScanLineTransform);
        Storyboard.SetTargetProperty(animation, new PropertyPath(TranslateTransform.YProperty));

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        return storyboard;
    }
}

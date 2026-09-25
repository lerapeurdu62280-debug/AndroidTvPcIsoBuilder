using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTvPcIsoBuilder.Presentation.Wpf.ViewModels;

/// <summary>
/// Écran d'accueil du wizard : présente le produit et les 4 étapes du parcours de
/// création d'une image Android TV compilée depuis les sources AOSP officielles.
/// </summary>
public partial class HomeViewModel : ObservableObject
{
    public event Action? StartRequested;

    [RelayCommand]
    private void Start() => StartRequested?.Invoke();
}

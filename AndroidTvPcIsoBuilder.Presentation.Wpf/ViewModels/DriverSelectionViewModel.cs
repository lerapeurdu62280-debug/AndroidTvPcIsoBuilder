using System.Collections.ObjectModel;
using AndroidTvPcIsoBuilder.Application.Interfaces;
using AndroidTvPcIsoBuilder.Domain.Entities;
using AndroidTvPcIsoBuilder.Infrastructure.Drivers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTvPcIsoBuilder.Presentation.Wpf.ViewModels;

/// <summary>
/// Écran "Pilotes Wi-Fi &amp; Bluetooth" du wizard : choix entre pack de pilotes embarqué
/// (téléchargé à la demande depuis le catalogue) et détection automatique au premier boot
/// (script shell généré). Les deux mécanismes sont complémentaires et peuvent être activés
/// simultanément (voir <see cref="WifiBluetoothDriverSelection"/>).
/// </summary>
public partial class DriverSelectionViewModel : ObservableObject
{
    private readonly IDriverCatalogService _driverCatalogService;
    private readonly FirstBootScriptGenerator _firstBootScriptGenerator;

    /// <summary>
    /// Catalogue dédupliqué par VendorId pour l'affichage : le catalogue complet contient
    /// plusieurs entrées par vendor (une par version de kernel supportée), mais l'UI ne
    /// présente qu'une seule case à cocher par vendor. On garde le DisplayName/Description
    /// de la première entrée rencontrée pour chaque vendor.
    /// </summary>
    public IReadOnlyList<DriverCatalogEntry> AvailableDrivers { get; }

    public ObservableCollection<string> SelectedVendorIds { get; } = new();

    [ObservableProperty]
    private bool _embeddedDriverPackEnabled = true;

    [ObservableProperty]
    private bool _autoDetectFirstBootEnabled = true;

    [ObservableProperty]
    private double _estimatedCoveragePercent;

    [ObservableProperty]
    private string _generatedScriptPreview = string.Empty;

    public event Action? NextRequested;

    public DriverSelectionViewModel(IDriverCatalogService driverCatalogService, FirstBootScriptGenerator firstBootScriptGenerator)
    {
        _driverCatalogService = driverCatalogService;
        _firstBootScriptGenerator = firstBootScriptGenerator;

        AvailableDrivers = _driverCatalogService.GetAvailableDrivers()
            .GroupBy(entry => entry.VendorId)
            .Select(group => group.First())
            .ToList();

        RefreshDerivedState();
    }

    /// <summary>Construit la sélection de pilotes Wi-Fi/Bluetooth à partir de l'état courant.</summary>
    public WifiBluetoothDriverSelection BuildSelection() => new()
    {
        EmbeddedDriverPackEnabled = EmbeddedDriverPackEnabled,
        AutoDetectFirstBootEnabled = AutoDetectFirstBootEnabled,
        SelectedChipsetVendorIds = SelectedVendorIds.ToList(),
    };

    [RelayCommand]
    private void ToggleVendor(string vendorId)
    {
        if (SelectedVendorIds.Contains(vendorId))
            SelectedVendorIds.Remove(vendorId);
        else
            SelectedVendorIds.Add(vendorId);

        // Le MultiBinding qui pilote IsChecked de chaque ToggleButton (voir DriverSelectionView.xaml)
        // observe un instantané de SelectedVendorIds via un converter, sans s'abonner à son
        // CollectionChanged : un simple Add/Remove sur cette ObservableCollection ne déclenche donc
        // pas de re-coche visuelle. On force le réévaluation de ce binding en notifiant un
        // changement sur SelectedVendorIds elle-même (la référence ne change pas, mais WPF
        // réévalue le MultiBinding qui la lit à chaque notification de ce nom de propriété).
        OnPropertyChanged(nameof(SelectedVendorIds));

        RefreshDerivedState();
    }

    [RelayCommand]
    private void Next() => NextRequested?.Invoke();

    partial void OnEmbeddedDriverPackEnabledChanged(bool value) => RefreshDerivedState();

    partial void OnAutoDetectFirstBootEnabledChanged(bool value) => RefreshDerivedState();

    private void RefreshDerivedState()
    {
        EstimatedCoveragePercent = _driverCatalogService.EstimateCoveragePercent(SelectedVendorIds.ToList());
        GeneratedScriptPreview = _firstBootScriptGenerator.Generate(BuildSelection());
    }
}

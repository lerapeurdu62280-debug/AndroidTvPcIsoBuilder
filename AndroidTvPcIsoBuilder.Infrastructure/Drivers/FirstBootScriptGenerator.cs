using System.Reflection;
using AndroidTvPcIsoBuilder.Domain.Entities;

namespace AndroidTvPcIsoBuilder.Infrastructure.Drivers;

/// <summary>
/// Génère le script shell de détection automatique des chipsets Wi-Fi/Bluetooth au premier
/// démarrage, à partir du template embarqué <c>first-boot-detect.sh.template</c> et de la
/// sélection de vendors du projet. Classe pure : aucune dépendance externe, aucune écriture
/// disque — uniquement une transformation de texte, testable unitairement sans mock.
/// </summary>
public class FirstBootScriptGenerator
{
    private const string SelectedVendorsToken = "{{SELECTED_VENDORS}}";
    private const string TemplateResourceName =
        "AndroidTvPcIsoBuilder.Infrastructure.Assets.Scripts.first-boot-detect.sh.template";

    /// <summary>
    /// Produit le script final en remplaçant le token <c>{{SELECTED_VENDORS}}</c> par la liste
    /// bash des vendor ids sélectionnés, au format <c>"realtek" "broadcom"</c> (éléments d'un
    /// tableau bash, sans les parenthèses englobantes déjà présentes dans le template).
    /// </summary>
    public string Generate(WifiBluetoothDriverSelection selection)
    {
        var template = ReadEmbeddedTemplate();
        var vendorsLiteral = string.Join(' ', selection.SelectedChipsetVendorIds.Select(id => $"\"{id}\""));

        return template.Replace(SelectedVendorsToken, vendorsLiteral);
    }

    private static string ReadEmbeddedTemplate()
    {
        var assembly = typeof(FirstBootScriptGenerator).Assembly;
        using var stream = assembly.GetManifestResourceStream(TemplateResourceName)
            ?? throw new InvalidOperationException(
                $"La ressource embarquée '{TemplateResourceName}' est introuvable dans l'assembly. " +
                "Vérifiez que le fichier est bien référencé en <EmbeddedResource> dans le .csproj.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

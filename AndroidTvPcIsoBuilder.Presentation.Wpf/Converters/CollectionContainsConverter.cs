using System.Collections;
using System.Globalization;
using System.Windows.Data;

namespace AndroidTvPcIsoBuilder.Presentation.Wpf.Converters;

/// <summary>
/// MultiValueConverter à deux entrées (collection, élément) : retourne vrai si l'élément est
/// présent dans la collection. Utilisé pour lier l'état coché d'un ToggleButton par élément de
/// liste à une collection observable de sélection (ex. <c>DriverSelectionViewModel.SelectedVendorIds</c>),
/// sans commande côté ConvertBack : la bascule elle-même est faite par la commande liée au clic.
/// </summary>
public sealed class CollectionContainsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not [IEnumerable collection, { } item])
            return false;

        foreach (var candidate in collection)
        {
            if (Equals(candidate, item))
                return true;
        }

        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("La bascule de sélection passe par la commande liée, pas par ConvertBack.");
}

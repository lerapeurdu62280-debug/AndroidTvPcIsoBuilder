using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Binding = System.Windows.Data.Binding;

namespace AndroidTvPcIsoBuilder.Presentation.Wpf.Converters;

/// <summary>
/// MultiBinding à deux valeurs (l'élément courant, la valeur sélectionnée) : retourne le
/// premier brush si les deux valeurs sont égales, le second sinon. Utilisé pour surligner la
/// carte sélectionnée dans une liste (WPF interdit un Binding dynamique sur DataTrigger.Value,
/// donc on ne peut pas comparer directement deux valeurs de binding via un DataTrigger).
/// </summary>
public sealed class EqualityToBrushConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 4)
            return values.Length > 2 ? values[3] : Binding.DoNothing;

        var isEqual = Equals(values[0], values[1]);
        return isEqual ? values[2] : values[3];
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

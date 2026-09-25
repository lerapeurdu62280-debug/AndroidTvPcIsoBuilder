using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AndroidTvPcIsoBuilder.Presentation.Wpf.Converters;

/// <summary>Affiche l'élément quand la valeur liée égale le paramètre, le masque sinon.</summary>
public sealed class EnumToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null && parameter is not null && value.Equals(parameter) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

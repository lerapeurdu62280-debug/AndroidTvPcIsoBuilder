using System.Globalization;
using System.Windows.Data;
using Binding = System.Windows.Data.Binding;

namespace AndroidTvPcIsoBuilder.Presentation.Wpf.Converters;

/// <summary>Lie un RadioButton à une valeur d'enum : coché quand la valeur liée égale le paramètre.</summary>
public sealed class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null && parameter is not null && value.Equals(parameter);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? parameter : Binding.DoNothing;
}

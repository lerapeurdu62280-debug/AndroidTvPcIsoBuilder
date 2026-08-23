using System.Globalization;
using System.Windows.Data;

namespace AndroidTvPcIsoBuilder.Presentation.Wpf.Converters;

public sealed class NullToBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

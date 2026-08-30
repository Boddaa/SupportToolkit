using System.Globalization;
using System.Windows.Data;

namespace NetworkDiscoveryTool.UI;

[ValueConversion(typeof(string), typeof(double))]
public sealed class PercentStringToDoubleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && s.EndsWith("%") && double.TryParse(s.TrimEnd('%'), NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
            return result;
        return 0.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

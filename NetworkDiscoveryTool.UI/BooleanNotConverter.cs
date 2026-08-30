using System.Globalization;
using System.Windows.Data;

namespace NetworkDiscoveryTool.UI;

[ValueConversion(typeof(bool), typeof(bool))]
public sealed class BooleanNotConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b ? !b : true;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b ? !b : true;
    }
}

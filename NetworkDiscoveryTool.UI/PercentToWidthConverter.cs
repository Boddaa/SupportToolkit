using System.Globalization;
using System.Windows.Data;

namespace NetworkDiscoveryTool.UI;

[ValueConversion(typeof(double), typeof(double))]
public sealed class PercentToWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double percent && parameter is string maxWidthStr && double.TryParse(maxWidthStr, out var maxWidth))
        {
            return Math.Max(0, Math.Min(maxWidth, percent / 100.0 * maxWidth));
        }
        return 0.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

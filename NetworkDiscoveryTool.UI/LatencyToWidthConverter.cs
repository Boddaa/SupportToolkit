using System.Globalization;
using System.Windows.Data;

namespace NetworkDiscoveryTool.UI;

[ValueConversion(typeof(long), typeof(double))]
public sealed class LatencyToWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is long latency && latency > 0 && parameter is string maxStr && double.TryParse(maxStr, out var maxWidth))
        {
            var ratio = Math.Min(1.0, latency / 200.0);
            return Math.Max(4.0, ratio * maxWidth);
        }
        return 4.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

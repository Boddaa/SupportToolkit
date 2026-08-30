using System.Globalization;
using System.Windows.Data;

namespace NetworkDiscoveryTool.UI;

[ValueConversion(typeof(int), typeof(double))]
public sealed class CountToWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int count && parameter is string maxPixelsStr && double.TryParse(maxPixelsStr, out var maxPixels))
        {
            var maxCount = 50.0;
            var ratio = Math.Min(1.0, count / maxCount);
            return Math.Max(4.0, ratio * maxPixels);
        }
        return 4.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

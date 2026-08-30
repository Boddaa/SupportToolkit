using System.Globalization;
using System.Windows.Data;

namespace NetworkDiscoveryTool.UI;

[ValueConversion(typeof(string), typeof(System.Windows.Media.Color))]
public sealed class StatusToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = value?.ToString() ?? "";
        return status switch
        {
            "Connected" or "Running" or "Online" => System.Windows.Media.Color.FromRgb(0x10, 0xB9, 0x81),
            "Unavailable" or "Stopped" or "Offline" => System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44),
            "Checking..." => System.Windows.Media.Color.FromRgb(0xF5, 0x9E, 0x0B),
            _ => System.Windows.Media.Color.FromRgb(0x9C, 0xA3, 0xAF),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

using System.Globalization;
using System.Windows.Data;

namespace NetworkDiscoveryTool.UI;

[ValueConversion(typeof(Enum), typeof(int))]
public sealed class EnumToIntConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Enum e)
            return System.Convert.ToInt32(e);
        return 0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int i && targetType.IsEnum)
            return Enum.ToObject(targetType, i);
        return 0;
    }
}

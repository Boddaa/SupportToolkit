using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace NetworkDiscoveryTool.UI.Views.ProcessManager;

public sealed class IndentToMarginConverter : MarkupExtension, IValueConverter
{
    public override object ProvideValue(IServiceProvider serviceProvider) => this;
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int indent && indent > 0)
            return new Thickness(indent * 18, 0, 0, 0);
        return new Thickness(0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw freshNotSupportedException();
    }

    private static NotSupportedException freshNotSupportedException() => new();
}

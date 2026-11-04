using System.Windows;
using System.Windows.Controls;

namespace NetworkDiscoveryTool.UI.Views.ProcessManager;

public partial class ProcessDetailRow : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(ProcessDetailRow),
            new PropertyMetadata(string.Empty, (d, e) => ((ProcessDetailRow)d).TxtLabel.Text = (string)e.NewValue));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(object), typeof(ProcessDetailRow),
            new PropertyMetadata(null, (d, e) => ((ProcessDetailRow)d).TxtValue.Text = e.NewValue?.ToString() ?? "N/A"));

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public object? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public ProcessDetailRow()
    {
        InitializeComponent();
    }
}

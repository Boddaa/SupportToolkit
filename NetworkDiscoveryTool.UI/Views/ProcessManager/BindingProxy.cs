using System.Windows;

namespace NetworkDiscoveryTool.UI.Views.ProcessManager;

/// <summary>
/// A Freezable proxy object that inherits DataContext across non-visual trees (such as ContextMenus and Popups).
/// </summary>
public class BindingProxy : Freezable
{
    protected override Freezable CreateInstanceCore()
    {
        return new BindingProxy();
    }

    public object Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy), new UIPropertyMetadata(null));
}

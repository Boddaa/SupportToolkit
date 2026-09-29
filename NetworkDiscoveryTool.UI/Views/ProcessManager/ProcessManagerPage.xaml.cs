using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfButton = System.Windows.Controls.Button;
using NetworkDiscoveryTool.UI.Models;
using NetworkDiscoveryTool.UI.ViewModels;

namespace NetworkDiscoveryTool.UI.Views.ProcessManager;

public partial class ProcessManagerPage : Page
{
    private readonly ProcessManagerViewModel _vm;

    public ProcessManagerPage(ProcessManagerViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _vm.OnNavigatedTo();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _vm.OnNavigatedFrom();
    }

    private void ProcessGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProcessGrid.SelectedItem is ProcessItemModel selected)
        {
            _vm.SelectProcess(selected);
        }
    }

    private void ProcessGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var dep = e.OriginalSource as DependencyObject;
        while (dep != null && dep != ProcessGrid && dep is not DataGridRow)
        {
            dep = VisualTreeHelper.GetParent(dep);
        }

        if (dep is DataGridRow row && row.Item is ProcessItemModel model)
        {
            _vm.SelectProcess(model);
        }
    }

    private void RowActionBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton btn && btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.IsOpen = true;
        }
    }

    private void MoreActionsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton btn && btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.IsOpen = true;
        }
    }
}

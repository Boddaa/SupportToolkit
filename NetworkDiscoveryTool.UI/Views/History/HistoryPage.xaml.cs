using System.Windows.Controls;
using NetworkDiscoveryTool.UI.ViewModels;

namespace NetworkDiscoveryTool.UI.Views.History;

public partial class HistoryPage : Page
{
    private readonly HistoryViewModel _vm;

    public HistoryPage(HistoryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _vm = viewModel;
        Loaded += async (_, _) => await _vm.LoadHistoryCommand.ExecuteAsync(null);
    }

    private async void HistoryGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (((System.Windows.Controls.DataGrid)sender).SelectedItem is Core.Models.Scan scan)
        {
            await _vm.SelectScanCommand.ExecuteAsync(scan);
        }
    }

    private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {

    }
}

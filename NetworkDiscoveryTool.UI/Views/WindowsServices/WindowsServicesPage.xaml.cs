using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using NetworkDiscoveryTool.UI.Models;
using NetworkDiscoveryTool.UI.ViewModels;

namespace NetworkDiscoveryTool.UI.Views.WindowsServices;

public partial class WindowsServicesPage
{
    private readonly WindowsServicesViewModel _vm;

    public WindowsServicesPage(WindowsServicesViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        _vm = vm;
    }

    private void Page_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_vm.FilteredServices.Count == 0)
            _vm.LoadServicesCommand.Execute(null);
    }

    private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ServicesGrid.SelectedItem is ServiceItemModel item)
            _vm.ShowDetailCommand.Execute(item);
    }

    private void ColumnHeader_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridColumnHeader header)
        {
            var path = header.Column.SortMemberPath;
            if (!string.IsNullOrEmpty(path))
                _vm.SortByCommand.Execute(path);
        }
    }
}

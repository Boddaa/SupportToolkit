using System.Windows.Controls;
using NetworkDiscoveryTool.UI.ViewModels;

namespace NetworkDiscoveryTool.UI.Views.Dashboard;

public partial class DashboardPage : Page
{
    private readonly DashboardViewModel _vm;
    private bool _hasLoadedOnce;

    public DashboardPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _vm = viewModel;
        Loaded += async (_, _) =>
        {
            if (!_hasLoadedOnce)
            {
                _hasLoadedOnce = true;
                await _vm.LoadAllDataCommand.ExecuteAsync(null);
            }
        };
    }
}

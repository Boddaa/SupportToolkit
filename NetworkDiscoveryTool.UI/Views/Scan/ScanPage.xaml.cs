using System.Windows.Controls;
using NetworkDiscoveryTool.UI.ViewModels;

namespace NetworkDiscoveryTool.UI.Views.Scan;

public partial class ScanPage : Page
{
    public ScanPage(ScanViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.LoadInitialDataAsync();
    }
}


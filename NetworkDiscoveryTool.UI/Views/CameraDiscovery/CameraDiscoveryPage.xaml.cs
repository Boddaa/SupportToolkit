using System.Windows.Controls;
using NetworkDiscoveryTool.UI.ViewModels;

namespace NetworkDiscoveryTool.UI.Views.CameraDiscovery;

public partial class CameraDiscoveryPage : Page
{
    public CameraDiscoveryPage(CameraDiscoveryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

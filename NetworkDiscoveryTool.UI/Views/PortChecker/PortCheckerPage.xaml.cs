namespace NetworkDiscoveryTool.UI.Views.PortChecker;

public partial class PortCheckerPage
{
    public PortCheckerPage(NetworkDiscoveryTool.UI.ViewModels.PortCheckerViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}

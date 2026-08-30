namespace NetworkDiscoveryTool.UI.Views.PingTool;

public partial class PingToolPage
{
    public PingToolPage(NetworkDiscoveryTool.UI.ViewModels.PingToolViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}

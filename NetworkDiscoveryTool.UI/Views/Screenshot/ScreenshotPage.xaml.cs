using NetworkDiscoveryTool.UI.ViewModels;

namespace NetworkDiscoveryTool.UI.Views.Screenshot;

public partial class ScreenshotPage
{
    private readonly ScreenshotViewModel _vm;

    public ScreenshotPage(ScreenshotViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        _vm = vm;
    }

    private void Page_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        _vm.RefreshHistory();
    }

    private void Button_Click(object sender, System.Windows.RoutedEventArgs e)
    {

    }

    private void DataGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {

    }
}

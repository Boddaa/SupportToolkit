using NetworkDiscoveryTool.UI.ViewModels;

namespace NetworkDiscoveryTool.UI.Views.OperationHistory;

public partial class OperationHistoryPage
{
    private readonly OperationHistoryViewModel _vm;

    public OperationHistoryPage(OperationHistoryViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        _vm = vm;
    }

    private async void Page_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        await _vm.LoadHistoryCommand.ExecuteAsync(null);
    }
}

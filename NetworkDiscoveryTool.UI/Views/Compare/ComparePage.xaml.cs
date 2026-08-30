using System.Windows.Controls;
using NetworkDiscoveryTool.UI.ViewModels;

namespace NetworkDiscoveryTool.UI.Views.Compare;

public partial class ComparePage : Page
{
    public ComparePage(CompareViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.LoadScansCommand.ExecuteAsync(null);
    }
}

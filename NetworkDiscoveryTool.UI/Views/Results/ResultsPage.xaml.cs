using System.Windows.Controls;
using NetworkDiscoveryTool.UI.ViewModels;

namespace NetworkDiscoveryTool.UI.Views.Results;

public partial class ResultsPage : Page
{
    public ResultsPage(ResultsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.LoadLatestResultsCommand.ExecuteAsync(null);
    }
}

using System;
using System.Windows;
using NetworkDiscoveryTool.UI.ViewModels;

namespace NetworkDiscoveryTool.UI.Views.IisMonitor;

public partial class IisMonitorPage
{
    private readonly IisMonitorViewModel _vm;

    public IisMonitorPage(IisMonitorViewModel vm)
    {
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Page init error: {ex.Message}");
            throw;
        }
        DataContext = vm;
        _vm = vm;
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_vm.Sites.Count == 0)
                _vm.LoadIisInfoCommand.Execute(null);
        }
        catch { /* suppress */ }
    }

    private void Backdrop_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SitesGrid.SelectedItem = null;
        PoolsGrid.SelectedItem = null;
    }

    private void CloseDrawer_Click(object sender, RoutedEventArgs e)
    {
        SitesGrid.SelectedItem = null;
        PoolsGrid.SelectedItem = null;
    }
}

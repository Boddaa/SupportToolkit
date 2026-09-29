using System;
using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using NetworkDiscoveryTool.UI.Views.Dashboard;
using NetworkDiscoveryTool.UI.Views.IisMonitor;
using NetworkDiscoveryTool.UI.Views.LogCollector;
using NetworkDiscoveryTool.UI.Views.OperationHistory;
using NetworkDiscoveryTool.UI.Views.PingTool;
using NetworkDiscoveryTool.UI.Views.PortChecker;
using NetworkDiscoveryTool.UI.Views.Scan;
using NetworkDiscoveryTool.UI.Views.Settings;
using NetworkDiscoveryTool.UI.Views.ProcessManager;
using NetworkDiscoveryTool.UI.Views.SqlTester;
using NetworkDiscoveryTool.UI.Views.SystemInfo;
using NetworkDiscoveryTool.UI.ViewModels;
using NetworkDiscoveryTool.UI.Views.DeviceDetails;
using NetworkDiscoveryTool.UI.Views.Topology;
using NetworkDiscoveryTool.UI.Views.WindowsServices;

namespace NetworkDiscoveryTool.UI.Services;

public class NavigationService : INavigationService
{
    private Frame? _frame;
    private readonly IServiceProvider _serviceProvider;
    private readonly CurrentUserService _currentUser;
    private readonly ConcurrentDictionary<Type, Page> _pageCache = new();

    public NavigationService(IServiceProvider serviceProvider, CurrentUserService currentUser)
    {
        _serviceProvider = serviceProvider;
        _currentUser = currentUser;
        _currentUser.UserChanged += () => _pageCache.Clear();
    }

    public void ClearCache() => _pageCache.Clear();

    public void SetFrame(Frame frame) => _frame = frame;

    private T GetPage<T>() where T : Page
    {
        return (T)_pageCache.GetOrAdd(typeof(T), _ => _serviceProvider.GetRequiredService<T>());
    }

    public void NavigateToDashboard() => Navigate(GetPage<DashboardPage>());
    public void NavigateToNetworkDiscovery() => Navigate(GetPage<ScanPage>());
    public void NavigateToLiveTopology() => Navigate(GetPage<TopologyPage>());
    public void NavigateToPingTool() => Navigate(GetPage<PingToolPage>());
    public void NavigateToPortChecker() => Navigate(GetPage<PortCheckerPage>());
    public void NavigateToSystemInfo() => Navigate(GetPage<SystemInfoPage>());
    public void NavigateToWindowsServices() => Navigate(GetPage<WindowsServicesPage>());
    public void NavigateToSqlTester() => Navigate(_serviceProvider.GetRequiredService<SqlTesterPage>());
    public void NavigateToIisMonitor() => Navigate(GetPage<IisMonitorPage>());
    public void NavigateToProcessManager() => Navigate(GetPage<ProcessManagerPage>());
    public void NavigateToLogCollector() => Navigate(GetPage<LogCollectorPage>());
    public void NavigateToOperationHistory() => Navigate(GetPage<OperationHistoryPage>());
    public void NavigateToSettings() => Navigate(GetPage<SettingsPage>());

    public void NavigateToDeviceDetails(int deviceId)
    {
        var vm = _serviceProvider.GetRequiredService<DeviceDetailsViewModel>();
        var page = new DeviceDetailsPage(vm, deviceId);
        Navigate(page);
    }

    public void NavigateToDeviceDetails(string ip)
    {
        var vm = _serviceProvider.GetRequiredService<DeviceDetailsViewModel>();
        var page = new DeviceDetailsPage(vm, ip);
        Navigate(page);
    }

    public void NavigateTo(string pageName)
    {
        switch (pageName?.Trim())
        {
            case "NetworkDiscovery":
            case "Scan":
                NavigateToNetworkDiscovery();
                break;
            case "LiveTopology":
            case "Topology":
                NavigateToLiveTopology();
                break;
            case "PingTool":
                NavigateToPingTool();
                break;
            case "PortChecker":
                NavigateToPortChecker();
                break;
            case "SystemInfo":
                NavigateToSystemInfo();
                break;
            case "WindowsServices":
            case "Services":
                NavigateToWindowsServices();
                break;
            case "SqlTester":
                NavigateToSqlTester();
                break;
            case "IisMonitor":
                NavigateToIisMonitor();
                break;
            case "ProcessManager":
                NavigateToProcessManager();
                break;
            case "LogCollector":
                NavigateToLogCollector();
                break;
            case "OperationHistory":
                NavigateToOperationHistory();
                break;
            case "Settings":
                NavigateToSettings();
                break;
            case "Dashboard":
            default:
                NavigateToDashboard();
                break;
        }
    }

    private void Navigate(Page page)
    {
        try
        {
            if (_frame == null)
                throw new InvalidOperationException("Frame not set. Call SetFrame first.");
            _frame.Content = page;
        }
        catch (Exception ex)
        {
            var msg = $"Navigation error: {ex.Message}\n\n{ex}";
            try { System.IO.File.AppendAllText("crash.log", $"{DateTime.Now}: {msg}\r\n"); } catch { }
            System.Windows.MessageBox.Show(msg, "Navigation Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

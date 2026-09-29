using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using Microsoft.Extensions.DependencyInjection;
using NetworkDiscoveryTool.UI.Services;
using NetworkDiscoveryTool.UI.ViewModels;
using NetworkDiscoveryTool.UI.Views.Dashboard;
using NetworkDiscoveryTool.UI.Views.DeviceDetails;
using NetworkDiscoveryTool.UI.Views.IisMonitor;
using NetworkDiscoveryTool.UI.Views.LogCollector;
using NetworkDiscoveryTool.UI.Views.Login;
using NetworkDiscoveryTool.UI.Views.OperationHistory;
using NetworkDiscoveryTool.UI.Views.PingTool;
using NetworkDiscoveryTool.UI.Views.PortChecker;
using NetworkDiscoveryTool.UI.Views.ProcessManager;
using NetworkDiscoveryTool.UI.Views.Scan;
using NetworkDiscoveryTool.UI.Views.Screenshot;
using NetworkDiscoveryTool.UI.Views.Settings;
using NetworkDiscoveryTool.UI.Views.SqlTester;
using NetworkDiscoveryTool.UI.Views.SystemInfo;
using NetworkDiscoveryTool.UI.Views.WindowsServices;

namespace NetworkDiscoveryTool.UI;

public partial class MainWindow : Window
{
    private readonly INavigationService _navigation;
    private readonly DashboardViewModel _dashboardVm;
    private readonly CurrentUserService _currentUser;
    private readonly ISettingsService _settings;
    private bool _isSidebarCollapsed;

    public MainWindow(
        INavigationService navigation,
        DashboardViewModel dashboardVm,
        CurrentUserService currentUser,
        ISettingsService settings)
    {
        InitializeComponent();

        _navigation = navigation;
        _dashboardVm = dashboardVm;
        _currentUser = currentUser;
        _settings = settings;

        DataContext = dashboardVm;
        navigation.SetFrame(MainFrame);

        MainFrame.Navigated += OnMainFrameNavigated;

        UpdateUserDisplay(currentUser.Username, currentUser.Role);
        _currentUser.UserChanged += () => UpdateUserDisplay(_currentUser.Username, _currentUser.Role);

        MainFrame.Content = new DashboardPage(dashboardVm);
        Closed += (_, _) => System.Windows.Application.Current.Shutdown();

        SourceInitialized += (s, e) =>
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var source = System.Windows.Interop.HwndSource.FromHwnd(handle);
            source?.AddHook(WndProc);
        };

        Loaded += async (_, _) =>
        {
            var workArea = SystemParameters.WorkArea;
            // Adapt window size dynamically to the user's monitor work area
            if (workArea.Width > 0 && workArea.Height > 0)
            {
                if (workArea.Width < Width || workArea.Height < Height)
                {
                    Width = Math.Max(MinWidth, Math.Min(1280, workArea.Width * 0.94));
                    Height = Math.Max(MinHeight, Math.Min(780, workArea.Height * 0.92));
                    Left = Math.Max(0, (workArea.Width - Width) / 2 + workArea.Left);
                    Top = Math.Max(0, (workArea.Height - Height) / 2 + workArea.Top);
                }

                _prevWidth = Width;
                _prevHeight = Height;
                _prevLeft = Left;
                _prevTop = Top;

                // Automatically collapse sidebar on smaller displays to maximize working space
                if (workArea.Width < 1280 && !_isSidebarCollapsed)
                {
                    ToggleSidebar(this, new RoutedEventArgs());
                }
            }

            // Restore user's previous session state (active page, sidebar, etc.)
            await RestoreLastStateAsync();
        };
    }

    private double _prevWidth = 1280;
    private double _prevHeight = 780;
    private double _prevLeft = 80;
    private double _prevTop = 80;
    private bool _isExplicitMaximized = false;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_NCHITTEST = 0x0084;
        if (msg == WM_NCHITTEST)
        {
            if (WindowState == WindowState.Maximized || _isExplicitMaximized)
                return IntPtr.Zero;

            int resizeBorder = 8;
            int x = (short)(lParam.ToInt32() & 0xFFFF);
            int y = (short)((lParam.ToInt32() >> 16) & 0xFFFF);
            var pt = PointFromScreen(new System.Windows.Point(x, y));

            bool left = pt.X <= resizeBorder;
            bool right = pt.X >= ActualWidth - resizeBorder;
            bool top = pt.Y <= resizeBorder;
            bool bottom = pt.Y >= ActualHeight - resizeBorder;

            const int HTLEFT = 10;
            const int HTRIGHT = 11;
            const int HTTOP = 12;
            const int HTTOPLEFT = 13;
            const int HTTOPRIGHT = 14;
            const int HTBOTTOM = 15;
            const int HTBOTTOMLEFT = 16;
            const int HTBOTTOMRIGHT = 17;

            if (top && left) { handled = true; return (IntPtr)HTTOPLEFT; }
            if (top && right) { handled = true; return (IntPtr)HTTOPRIGHT; }
            if (bottom && left) { handled = true; return (IntPtr)HTBOTTOMLEFT; }
            if (bottom && right) { handled = true; return (IntPtr)HTBOTTOMRIGHT; }
            if (left) { handled = true; return (IntPtr)HTLEFT; }
            if (right) { handled = true; return (IntPtr)HTRIGHT; }
            if (top) { handled = true; return (IntPtr)HTTOP; }
            if (bottom) { handled = true; return (IntPtr)HTBOTTOM; }
        }
        return IntPtr.Zero;
    }

    private void OnMainFrameNavigated(object sender, NavigationEventArgs e)
    {
        var activeStyle = (Style)FindResource("NetScopeNavBtnActive");
        var defaultStyle = (Style)FindResource("NetScopeNavBtn");

        BtnDashboard.Style = defaultStyle;
        BtnNetworkDiscovery.Style = defaultStyle;
        BtnLiveTopology.Style = defaultStyle;
        BtnPortChecker.Style = defaultStyle;
        BtnPingTool.Style = defaultStyle;
        BtnSystemInfo.Style = defaultStyle;
        BtnWindowsServices.Style = defaultStyle;
        BtnIisMonitor.Style = defaultStyle;
        BtnProcessManager.Style = defaultStyle;
        BtnLogCollector.Style = defaultStyle;
        BtnSettings.Style = defaultStyle;

        var content = e.Content;
        string? pageName = null;

        if (content is DashboardPage)
        {
            BtnDashboard.Style = activeStyle;
            pageName = "Dashboard";
        }
        else if (content is ScanPage)
        {
            BtnNetworkDiscovery.Style = activeStyle;
            pageName = "NetworkDiscovery";
        }
        else if (content is NetworkDiscoveryTool.UI.Views.Topology.TopologyPage)
        {
            BtnLiveTopology.Style = activeStyle;
            pageName = "LiveTopology";
        }
        else if (content is PortCheckerPage)
        {
            BtnPortChecker.Style = activeStyle;
            pageName = "PortChecker";
        }
        else if (content is PingToolPage)
        {
            BtnPingTool.Style = activeStyle;
            pageName = "PingTool";
        }
        else if (content is SystemInfoPage || content is DeviceDetailsPage)
        {
            BtnSystemInfo.Style = activeStyle;
            pageName = "SystemInfo";
        }
        else if (content is WindowsServicesPage)
        {
            BtnWindowsServices.Style = activeStyle;
            pageName = "WindowsServices";
        }
        else if (content is SqlTesterPage)
        {
            pageName = "SqlTester";
        }
        else if (content is IisMonitorPage)
        {
            BtnIisMonitor.Style = activeStyle;
            pageName = "IisMonitor";
        }
        else if (content is ProcessManagerPage)
        {
            BtnProcessManager.Style = activeStyle;
            pageName = "ProcessManager";
        }
        else if (content is LogCollectorPage)
        {
            BtnLogCollector.Style = activeStyle;
            pageName = "LogCollector";
        }
        else if (content is ScreenshotPage)
        {
            pageName = "Screenshot";
        }
        else if (content is OperationHistoryPage)
        {
            pageName = "OperationHistory";
        }
        else if (content is SettingsPage)
        {
            BtnSettings.Style = activeStyle;
            pageName = "Settings";
        }

        if (pageName != null)
        {
            _ = _settings.SetAsync("LastActivePage", pageName);
        }
    }

    public void RestoreSession()
    {
        UpdateUserDisplay(_currentUser.Username, _currentUser.Role);
        _ = RestoreLastStateAsync();
    }

    private async Task RestoreLastStateAsync()
    {
        try
        {
            // 1. Restore Sidebar State
            var sidebarStr = await _settings.GetAsync("SidebarCollapsed");
            if (bool.TryParse(sidebarStr, out var wasCollapsed) && wasCollapsed != _isSidebarCollapsed)
            {
                ToggleSidebar(this, new RoutedEventArgs());
            }

            // 2. Restore Last Active Page
            var lastPage = await _settings.GetAsync("LastActivePage");
            if (!string.IsNullOrWhiteSpace(lastPage) && lastPage != "Dashboard")
            {
                _navigation.NavigateTo(lastPage);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to restore last application state.");
        }
    }

    public void UpdateUserDisplay(string username, string role)
    {
        UserDisplay.Text = username;
        RoleDisplay.Text = role;
        UserAvatar.Text = username.FirstOrDefault('U').ToString();
    }

    private void NavigateToDashboard(object sender, RoutedEventArgs e) => _navigation.NavigateToDashboard();
    private void NavigateToNetworkDiscovery(object sender, RoutedEventArgs e) => _navigation.NavigateToNetworkDiscovery();
    private void NavigateToLiveTopology(object sender, RoutedEventArgs e) => _navigation.NavigateToLiveTopology();
    private void NavigateToPingTool(object sender, RoutedEventArgs e) => _navigation.NavigateToPingTool();
    private void NavigateToPortChecker(object sender, RoutedEventArgs e) => _navigation.NavigateToPortChecker();
    private void NavigateToSystemInfo(object sender, RoutedEventArgs e) => _navigation.NavigateToSystemInfo();
    private void NavigateToWindowsServices(object sender, RoutedEventArgs e) => _navigation.NavigateToWindowsServices();
    private void NavigateToSqlTester(object sender, RoutedEventArgs e) => _navigation.NavigateToSqlTester();
    private void NavigateToIisMonitor(object sender, RoutedEventArgs e) => _navigation.NavigateToIisMonitor();
    private void NavigateToProcessManager(object sender, RoutedEventArgs e) => _navigation.NavigateToProcessManager();
    private void NavigateToLogCollector(object sender, RoutedEventArgs e) => _navigation.NavigateToLogCollector();
    private void NavigateToScreenshot(object sender, RoutedEventArgs e) => _navigation.NavigateToScreenshot();
    private void NavigateToOperationHistory(object sender, RoutedEventArgs e) => _navigation.NavigateToOperationHistory();
    private void NavigateToSettings(object sender, RoutedEventArgs e) => _navigation.NavigateToSettings();

    private void Logout(object sender, RoutedEventArgs e)
    {
        Hide();
        _currentUser.Clear();
        _navigation.NavigateToDashboard();
        var loginWindow = App.ServiceProvider.GetRequiredService<LoginWindow>();
        loginWindow.Owner = this;
        loginWindow.ShowDialog();
    }

    private void ToggleSidebar(object sender, RoutedEventArgs e)
    {
        _isSidebarCollapsed = !_isSidebarCollapsed;
        _ = _settings.SetAsync("SidebarCollapsed", _isSidebarCollapsed.ToString());
        double targetWidth = _isSidebarCollapsed ? 64 : 230;

        try
        {
            SidebarColumn.Width = new GridLength(targetWidth);
            SidebarPanel.Width = targetWidth;

            var labels = new[] {
                NavLabel, LblDashboard, LblNetworkDiscovery, LblLiveTopology, LblPingTool, LblPortChecker,
                LblSystemInfo, LblWindowsServices, LblSqlTester, LblIisMonitor, LblProcessManager,
                LblLogCollector, LblScreenshot, LblSettings, LblLogout
            };
            foreach (var lbl in labels)
            {
                if (lbl != null)
                    lbl.Visibility = _isSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
            }

            if (SidebarProfile != null)
                SidebarProfile.Visibility = _isSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
            if (SidebarBrandText != null)
                SidebarBrandText.Visibility = _isSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        }
        catch { }
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        var text = SearchBox.Text?.Trim().ToLower();
        var navButtons = new[] {
            (BtnDashboard, "Dashboard"),
            (BtnNetworkDiscovery, "Network Discovery"),
            (BtnLiveTopology, "Live Topology"),
            (BtnPingTool, "Ping Tool"),
            (BtnPortChecker, "Port Checker"),
            (BtnSystemInfo, "System Info"),
            (BtnWindowsServices, "Services"),
            (BtnSqlTester, "SQL Tester"),
            (BtnIisMonitor, "IIS Monitor"),
            (BtnProcessManager, "Process Manager"),
            (BtnLogCollector, "Log Collector"),
            (BtnScreenshot, "Screenshot"),
            (BtnSettings, "Settings"),
        };

        foreach (var (btn, name) in navButtons)
        {
            if (string.IsNullOrEmpty(text))
                btn.Visibility = Visibility.Visible;
            else
                btn.Visibility = name.ToLower().Contains(text) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnNotificationClick(object sender, RoutedEventArgs e)
    {
        System.Windows.MessageBox.Show("No new notifications.", "Notifications",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnProfileClick(object sender, RoutedEventArgs e)
    {
        SignOutModalOverlay.Visibility = Visibility.Visible;
    }

    private void CancelSignOut_Click(object sender, RoutedEventArgs e)
    {
        SignOutModalOverlay.Visibility = Visibility.Collapsed;
    }

    private void ConfirmSignOut_Click(object sender, RoutedEventArgs e)
    {
        SignOutModalOverlay.Visibility = Visibility.Collapsed;
        Logout(sender, e);
    }

    private void ToggleTheme(object sender, RoutedEventArgs e)
    {
        _navigation.NavigateToSettings();
    }

    private void MinimizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeWindow(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized || _isExplicitMaximized)
        {
            WindowState = WindowState.Normal;
            _isExplicitMaximized = false;
            Width = _prevWidth > 200 ? _prevWidth : 1280;
            Height = _prevHeight > 200 ? _prevHeight : 780;
            Left = Math.Max(0, _prevLeft);
            Top = Math.Max(0, _prevTop);
            MaxIcon.Text = "\uE922";
        }
        else
        {
            _prevWidth = ActualWidth;
            _prevHeight = ActualHeight;
            _prevLeft = Left;
            _prevTop = Top;
            _isExplicitMaximized = true;
            WindowState = WindowState.Maximized;
            MaxIcon.Text = "\uE923";
        }
    }

    private void CloseWindow(object sender, RoutedEventArgs e) => Close();

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            MaximizeWindow(sender, e);
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                if (WindowState == WindowState.Maximized || _isExplicitMaximized)
                {
                    var mousePos = PointToScreen(e.GetPosition(this));
                    WindowState = WindowState.Normal;
                    _isExplicitMaximized = false;
                    Width = _prevWidth > 200 ? _prevWidth : 1280;
                    Height = _prevHeight > 200 ? _prevHeight : 780;
                    Left = mousePos.X - (Width / 2);
                    Top = mousePos.Y - 20;
                    MaxIcon.Text = "\uE922";
                }
                DragMove();
            }
            catch { }
        }
    }
}

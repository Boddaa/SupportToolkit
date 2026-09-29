using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
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
using NetworkDiscoveryTool.UI.Views.Settings;
using NetworkDiscoveryTool.UI.Views.SqlTester;
using NetworkDiscoveryTool.UI.Views.SystemInfo;
using NetworkDiscoveryTool.UI.Views.WindowsServices;
using Microsoft.EntityFrameworkCore;
using NetworkDiscoveryTool.Data;
using NetworkDiscoveryTool.Core.Models;
using NetworkDiscoveryTool.UI.Models;
using System.Threading;

namespace NetworkDiscoveryTool.UI;

public partial class MainWindow : Window
{
    private readonly INavigationService _navigation;
    private readonly DashboardViewModel _dashboardVm;
    private readonly CurrentUserService _currentUser;
    private readonly ISettingsService _settings;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private bool _isSidebarCollapsed;
    private CancellationTokenSource? _searchCts;

    public MainWindow(
        INavigationService navigation,
        DashboardViewModel dashboardVm,
        CurrentUserService currentUser,
        ISettingsService settings,
        IDbContextFactory<AppDbContext> contextFactory)
    {
        InitializeComponent();

        _navigation = navigation;
        _dashboardVm = dashboardVm;
        _currentUser = currentUser;
        _settings = settings;
        _contextFactory = contextFactory;

        DataContext = dashboardVm;
        navigation.SetFrame(MainFrame);

        PreviewKeyDown += MainWindow_PreviewKeyDown;

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
                LblLogCollector, LblSettings, LblLogout
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
            if (SidebarFooterLogo != null)
                SidebarFooterLogo.Visibility = _isSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        }
        catch { }
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl + / or Ctrl + F to focus universal search
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
            (e.Key == Key.Oem2 || e.Key == Key.Divide || e.Key == Key.F))
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    private async void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        var text = SearchBox.Text?.Trim();
        SearchClearBtn.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

        if (string.IsNullOrWhiteSpace(text) || text.Length < 1)
        {
            _searchCts?.Cancel();
            SearchPopup.IsOpen = false;
            SearchResultsList.ItemsSource = null;
            return;
        }

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        try
        {
            await Task.Delay(120, ct);
            if (ct.IsCancellationRequested) return;

            var results = await PerformUniversalSearchAsync(text, ct);
            if (ct.IsCancellationRequested) return;

            SearchResultsList.ItemsSource = results;
            if (results.Count > 0)
            {
                SearchResultsList.SelectedIndex = 0;
                SearchEmptyNotice.Visibility = Visibility.Collapsed;
                SearchResultsSummary.Text = $"{results.Count} match(es) found";
            }
            else
            {
                SearchEmptyNotice.Visibility = Visibility.Visible;
                SearchEmptyText.Text = $"No devices or tools matching \"{text}\"";
                SearchResultsSummary.Text = "No results found";
            }

            SearchPopup.IsOpen = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Universal search error");
        }
    }

    private async Task<List<UniversalSearchResultItem>> PerformUniversalSearchAsync(string term, CancellationToken ct)
    {
        var list = new List<UniversalSearchResultItem>();
        var lower = term.ToLowerInvariant();
        var converter = new System.Windows.Media.BrushConverter();

        // 1. Navigation & Tools matches
        var allTools = new (string Name, string Desc, string Glyph, Action Act)[]
        {
            ("Dashboard", "Overview & real-time network topology", "\uE80F", () => _navigation.NavigateToDashboard()),
            ("Network Discovery", "Subnet ARP & ICMP discovery scanner", "\uEC27", () => _navigation.NavigateToNetworkDiscovery()),
            ("Live Topology", "Interactive network map & device nodes", "\uE701", () => _navigation.NavigateToLiveTopology()),
            ("Ping Tool", "High-precision latency & continuous reachability ping", "\uE754", () => _navigation.NavigateToPingTool()),
            ("Port Checker", "TCP socket port auditing & banner grabber", "\uE749", () => _navigation.NavigateToPortChecker()),
            ("System Information", "OS, hardware, CPU, RAM & network adapters", "\uE770", () => _navigation.NavigateToSystemInfo()),
            ("Windows Services", "Local & remote service control manager", "\uE713", () => _navigation.NavigateToWindowsServices()),
            ("SQL Tester", "Database query runner & connection diagnostics", "\uE943", () => _navigation.NavigateToSqlTester()),
            ("IIS Monitor", "Web server sites & application pools supervisor", "\uE774", () => _navigation.NavigateToIisMonitor()),
            ("Process Manager", "Real-time task supervisor, threads & performance", "\uE9D9", () => _navigation.NavigateToProcessManager()),
            ("Log Collector", "Windows event logs & IIS log forensics", "\uE8A5", () => _navigation.NavigateToLogCollector()),
            ("Operation History", "Audit logs & diagnostic activity trail", "\uE81C", () => _navigation.NavigateToOperationHistory()),
            ("Settings", "Telegram alerts, theme, credentials & scan config", "\uE713", () => _navigation.NavigateToSettings())
        };

        foreach (var tool in allTools)
        {
            if (tool.Name.ToLowerInvariant().Contains(lower) || tool.Desc.ToLowerInvariant().Contains(lower))
            {
                list.Add(new UniversalSearchResultItem
                {
                    Title = tool.Name,
                    Subtitle = tool.Desc,
                    Category = "TOOL",
                    IconGlyph = tool.Glyph,
                    IconColor = (System.Windows.Media.Brush)converter.ConvertFrom("#38BDF8")!,
                    IconBg = (System.Windows.Media.Brush)converter.ConvertFrom("#150EA5E9")!,
                    IconBorder = (System.Windows.Media.Brush)converter.ConvertFrom("#300EA5E9")!,
                    BadgeColor = (System.Windows.Media.Brush)converter.ConvertFrom("#A855F7")!,
                    BadgeBg = (System.Windows.Media.Brush)converter.ConvertFrom("#15A855F7")!,
                    BadgeBorder = (System.Windows.Media.Brush)converter.ConvertFrom("#30A855F7")!,
                    OnClick = () =>
                    {
                        SearchPopup.IsOpen = false;
                        SearchBox.Text = string.Empty;
                        tool.Act();
                    }
                });
            }
        }

        // 2. Query Devices from Database
        try
        {
            using var ctx = await _contextFactory.CreateDbContextAsync(ct);
            var devices = await ctx.Devices
                .AsNoTracking()
                .Where(d => EF.Functions.Like(d.IP, $"%{term}%") ||
                            (d.Hostname != null && EF.Functions.Like(d.Hostname, $"%{term}%")) ||
                            (d.MAC != null && EF.Functions.Like(d.MAC, $"%{term}%")) ||
                            (d.Vendor != null && EF.Functions.Like(d.Vendor, $"%{term}%")) ||
                            (d.OS != null && EF.Functions.Like(d.OS, $"%{term}%")) ||
                            (d.DeviceType != null && EF.Functions.Like(d.DeviceType, $"%{term}%")))
                .OrderByDescending(d => d.Status == "Online")
                .ThenBy(d => d.IP)
                .Take(12)
                .ToListAsync(ct);

            foreach (var d in devices)
            {
                var isOnline = string.Equals(d.Status, "Online", StringComparison.OrdinalIgnoreCase);
                var glyph = GetDeviceIconGlyph(d.DeviceType, d.OS);
                var subtitleParts = new List<string>();
                if (!string.IsNullOrWhiteSpace(d.Hostname)) subtitleParts.Add(d.Hostname);
                if (!string.IsNullOrWhiteSpace(d.Vendor)) subtitleParts.Add(d.Vendor);
                if (!string.IsNullOrWhiteSpace(d.MAC)) subtitleParts.Add(d.MAC);
                if (!string.IsNullOrWhiteSpace(d.OS)) subtitleParts.Add(d.OS);
                var sub = subtitleParts.Count > 0 ? string.Join("  •  ", subtitleParts) : "Discovered Network Asset";

                var targetIp = d.IP;
                var targetId = d.Id;

                list.Add(new UniversalSearchResultItem
                {
                    Title = d.IP,
                    Subtitle = sub,
                    Category = !string.IsNullOrWhiteSpace(d.DeviceType) ? d.DeviceType.ToUpperInvariant() : "DEVICE",
                    Status = isOnline ? "Online" : "Offline",
                    ExtraInfo = d.LatencyMs > 0 ? $"{d.LatencyMs} ms" : string.Empty,
                    IconGlyph = glyph,
                    IconColor = isOnline ? (System.Windows.Media.Brush)converter.ConvertFrom("#10B981")! : (System.Windows.Media.Brush)converter.ConvertFrom("#94A3B8")!,
                    IconBg = isOnline ? (System.Windows.Media.Brush)converter.ConvertFrom("#1510B981")! : (System.Windows.Media.Brush)converter.ConvertFrom("#1594A3B8")!,
                    IconBorder = isOnline ? (System.Windows.Media.Brush)converter.ConvertFrom("#3010B981")! : (System.Windows.Media.Brush)converter.ConvertFrom("#2094A3B8")!,
                    BadgeColor = (System.Windows.Media.Brush)converter.ConvertFrom("#38BDF8")!,
                    BadgeBg = (System.Windows.Media.Brush)converter.ConvertFrom("#150EA5E9")!,
                    BadgeBorder = (System.Windows.Media.Brush)converter.ConvertFrom("#300EA5E9")!,
                    StatusDotColor = isOnline ? (System.Windows.Media.Brush)converter.ConvertFrom("#10B981")! : (System.Windows.Media.Brush)converter.ConvertFrom("#F43F5E")!,
                    StatusTextColor = isOnline ? (System.Windows.Media.Brush)converter.ConvertFrom("#10B981")! : (System.Windows.Media.Brush)converter.ConvertFrom("#F43F5E")!,
                    StatusBg = isOnline ? (System.Windows.Media.Brush)converter.ConvertFrom("#1510B981")! : (System.Windows.Media.Brush)converter.ConvertFrom("#15F43F5E")!,
                    StatusBorder = isOnline ? (System.Windows.Media.Brush)converter.ConvertFrom("#3010B981")! : (System.Windows.Media.Brush)converter.ConvertFrom("#30F43F5E")!,
                    OnClick = () =>
                    {
                        SearchPopup.IsOpen = false;
                        SearchBox.Text = string.Empty;
                        if (targetId > 0)
                            _navigation.NavigateToDeviceDetails(targetId);
                        else
                            _navigation.NavigateToDeviceDetails(targetIp);
                    }
                });
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Serilog.Log.Warning(ex, "Error querying devices for universal search");
        }

        // 3. Direct IP inspection quick action
        var cleanTerm = term.Trim();
        if (System.Net.IPAddress.TryParse(cleanTerm, out _) ||
            (cleanTerm.Length >= 4 && cleanTerm.Any(char.IsDigit) && cleanTerm.Contains('.')))
        {
            if (!list.Any(x => x.Title.Equals(cleanTerm, StringComparison.OrdinalIgnoreCase)))
            {
                list.Insert(0, new UniversalSearchResultItem
                {
                    Title = $"Inspect {cleanTerm}",
                    Subtitle = "Open forensic device inspection & port sweep for this IP",
                    Category = "QUICK INSPECT",
                    IconGlyph = "\uE7F8",
                    IconColor = (System.Windows.Media.Brush)converter.ConvertFrom("#F59E0B")!,
                    IconBg = (System.Windows.Media.Brush)converter.ConvertFrom("#15F59E0B")!,
                    IconBorder = (System.Windows.Media.Brush)converter.ConvertFrom("#30F59E0B")!,
                    BadgeColor = (System.Windows.Media.Brush)converter.ConvertFrom("#F59E0B")!,
                    BadgeBg = (System.Windows.Media.Brush)converter.ConvertFrom("#15F59E0B")!,
                    BadgeBorder = (System.Windows.Media.Brush)converter.ConvertFrom("#30F59E0B")!,
                    OnClick = () =>
                    {
                        SearchPopup.IsOpen = false;
                        SearchBox.Text = string.Empty;
                        _navigation.NavigateToDeviceDetails(cleanTerm);
                    }
                });
            }
        }

        return list;
    }

    private static string GetDeviceIconGlyph(string? type, string? os)
    {
        var t = (type ?? "").ToLowerInvariant();
        var o = (os ?? "").ToLowerInvariant();

        if (t.Contains("server") || o.Contains("server")) return "\uE839";
        if (t.Contains("switch") || t.Contains("router") || t.Contains("gateway")) return "\uE81B";
        if (t.Contains("printer")) return "\uE749";
        if (t.Contains("mobile") || t.Contains("phone") || t.Contains("android") || t.Contains("ios")) return "\uE8EA";
        return "\uE7F8";
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && SearchPopup.IsOpen && SearchResultsList.Items.Count > 0)
        {
            SearchResultsList.Focus();
            if (SearchResultsList.SelectedIndex < 0)
                SearchResultsList.SelectedIndex = 0;
            var item = (ListBoxItem)SearchResultsList.ItemContainerGenerator.ContainerFromIndex(SearchResultsList.SelectedIndex);
            item?.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            ExecuteCurrentSearchResult();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            SearchPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void SearchResultsList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ExecuteCurrentSearchResult();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            SearchPopup.IsOpen = false;
            SearchBox.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Up && SearchResultsList.SelectedIndex == 0)
        {
            SearchBox.Focus();
            e.Handled = true;
        }
    }

    private void SearchResultsList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ExecuteCurrentSearchResult();
    }

    private void ExecuteCurrentSearchResult()
    {
        if (SearchResultsList.SelectedItem is UniversalSearchResultItem item)
        {
            item.OnClick?.Invoke();
        }
        else if (SearchResultsList.Items.Count > 0 && SearchResultsList.Items[0] is UniversalSearchResultItem firstItem)
        {
            firstItem.OnClick?.Invoke();
        }
        else
        {
            var term = SearchBox.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(term))
            {
                SearchPopup.IsOpen = false;
                SearchBox.Text = string.Empty;
                _navigation.NavigateToDeviceDetails(term);
            }
        }
    }

    private void OnSearchClearClick(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        SearchPopup.IsOpen = false;
        SearchBox.Focus();
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

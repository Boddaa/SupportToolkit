using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.ServiceProcess;
using System.Windows;
using System.Windows.Input;
using System.Runtime.InteropServices;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NetworkDiscoveryTool.Core.Models;
using NetworkDiscoveryTool.Data;

namespace NetworkDiscoveryTool.UI.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly Services.CurrentUserService _currentUser;
    private readonly Services.INavigationService _navigation;

    private readonly List<DashboardDeviceItem> _allDevicesMaster = [];

    public DashboardViewModel(
        IDbContextFactory<AppDbContext> contextFactory,
        Services.CurrentUserService currentUser,
        Services.INavigationService navigation)
    {
        _contextFactory = contextFactory;
        _currentUser = currentUser;
        _navigation = navigation;

        DetectActiveNetworkAdapters();
        InitializeQuickActions();
        _currentUser.UserChanged += () => { _ = LoadAllDataAsync(); };
        _ = LoadAllDataAsync();
    }

    // === Active Network Adapter Detection ===
    [ObservableProperty] private string _activeInterfaceName = "Ethernet";
    [ObservableProperty] private string _activeInterfaceIP = "192.168.1.1/24";
    [ObservableProperty] private int _activeScanPercent = 100;
    [ObservableProperty] private string _activeScanSubnet = "192.168.1.0/24";
    [ObservableProperty] private NetworkAdapterItem? _selectedNetworkAdapter;
    public ObservableCollection<NetworkAdapterItem> NetworkAdapters { get; } = [];

    partial void OnSelectedNetworkAdapterChanged(NetworkAdapterItem? value)
    {
        if (value != null)
        {
            ActiveInterfaceName = value.Description.Length > 24 ? value.Description.Substring(0, 24) + "..." : value.Description;
            ActiveInterfaceIP = $"{value.IPAddress}/24";
            ActiveScanSubnet = value.Subnet;
        }
    }

    private void DetectActiveNetworkAdapters()
    {
        try
        {
            NetworkAdapters.Clear();
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                         && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .ToList();

            var candidateList = new List<(NetworkAdapterItem Item, int Score)>();

            foreach (var adapter in interfaces)
            {
                var ipProp = adapter.GetIPProperties();
                var ipv4 = ipProp.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);

                if (ipv4 != null)
                {
                    var ipStr = ipv4.Address.ToString();
                    var nameLower = adapter.Name.ToLowerInvariant();
                    var descLower = adapter.Description.ToLowerInvariant();

                    // Filter out non-routable APIPA addresses
                    if (ipStr.StartsWith("169.254.")) continue;

                    var isVirtualOrTunnel =
                        nameLower.Contains("tailscale") || descLower.Contains("tailscale") ||
                        nameLower.Contains("vethernet") || descLower.Contains("hyper-v") ||
                        nameLower.Contains("virtual") || descLower.Contains("virtual") ||
                        nameLower.Contains("vmware") || descLower.Contains("vmware") ||
                        nameLower.Contains("zerotier") || descLower.Contains("zerotier") ||
                        nameLower.Contains("wireguard") || descLower.Contains("wireguard") ||
                        nameLower.Contains("nordvpn") || descLower.Contains("nordlynx") ||
                        nameLower.Contains("openvpn") || descLower.Contains("tap-") || descLower.Contains("tun-");

                    int score = 0;
                    if (ipStr.StartsWith("192.168.")) score += 100;
                    else if (ipStr.StartsWith("10.")) score += 80;
                    else if (ipStr.StartsWith("172.")) score += 70;
                    else score += 20;

                    if (adapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet) score += 30;
                    else if (adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) score += 25;

                    if (isVirtualOrTunnel) score -= 200; // Deprioritize virtual adapters/tunnels

                    var subnet = $"{ipStr.Substring(0, ipStr.LastIndexOf('.') + 1)}0/24";
                    var displayDesc = string.IsNullOrWhiteSpace(adapter.Description) ? adapter.Name : adapter.Description;

                    var item = new NetworkAdapterItem
                    {
                        Name = adapter.Name,
                        Description = displayDesc,
                        IPAddress = ipStr,
                        Subnet = subnet
                    };

                    candidateList.Add((item, score));
                }
            }

            foreach (var (item, _) in candidateList.OrderByDescending(c => c.Score))
            {
                NetworkAdapters.Add(item);
            }

            if (NetworkAdapters.Count > 0)
            {
                SelectedNetworkAdapter = NetworkAdapters[0];
            }
            else
            {
                ActiveInterfaceName = "Local Network";
                ActiveInterfaceIP = "192.168.1.1/24";
                ActiveScanSubnet = "192.168.1.0/24";
            }
        }
        catch
        {
            ActiveInterfaceName = "Local Network";
            ActiveInterfaceIP = "192.168.1.1/24";
            ActiveScanSubnet = "192.168.1.0/24";
        }
    }

    // === Quick Actions ===
    public ObservableCollection<QuickActionItem> QuickActions { get; } = [];

    private void InitializeQuickActions()
    {
        QuickActions.Add(new() { Name = "Scan Network", Description = "Discover devices on your network", Icon = "🌐", Command = new RelayCommand(() => _navigation.NavigateToNetworkDiscovery()) });
        QuickActions.Add(new() { Name = "Ping Tool", Description = "Test reachability of hosts", Icon = "📡", Command = new RelayCommand(() => _navigation.NavigateToPingTool()) });
        QuickActions.Add(new() { Name = "Port Checker", Description = "Check open TCP ports", Icon = "🔌", Command = new RelayCommand(() => _navigation.NavigateToPortChecker()) });
        QuickActions.Add(new() { Name = "Services", Description = "Manage Windows services", Icon = "⚙", Command = new RelayCommand(() => _navigation.NavigateToWindowsServices()) });
        QuickActions.Add(new() { Name = "SQL Tester", Description = "Run SQL queries", Icon = "🗄", Command = new RelayCommand(() => _navigation.NavigateToSqlTester()) });
        QuickActions.Add(new() { Name = "IIS Monitor", Description = "Monitor IIS sites & pools", Icon = "🌍", Command = new RelayCommand(() => _navigation.NavigateToIisMonitor()) });
        QuickActions.Add(new() { Name = "Log Collector", Description = "Browse and view log files", Icon = "📄", Command = new RelayCommand(() => _navigation.NavigateToLogCollector()) });
        QuickActions.Add(new() { Name = "Process Manager", Description = "Inspect processes & system performance", Icon = "⚡", Command = new RelayCommand(() => _navigation.NavigateToProcessManager()) });
    }

    // === Real Computed Scan KPIs ===
    [ObservableProperty] private int _totalDevices;
    [ObservableProperty] private int _onlineDevices;
    [ObservableProperty] private int _offlineDevices;
    [ObservableProperty] private double _onlinePercent;
    [ObservableProperty] private int _averageLatencyMs;
    [ObservableProperty] private int _openPortsCount;
    [ObservableProperty] private int _vulnerabilitiesCount;

    // === Real OS Distribution Counts & Percentages ===
    [ObservableProperty] private int _windowsCount;
    [ObservableProperty] private double _windowsPercent;
    [ObservableProperty] private string _windowsPercentText = "0 (0%)";
    [ObservableProperty] private int _linuxCount;
    [ObservableProperty] private double _linuxPercent;
    [ObservableProperty] private string _linuxPercentText = "0 (0%)";
    [ObservableProperty] private int _iotCount;
    [ObservableProperty] private double _iotPercent;
    [ObservableProperty] private string _iotPercentText = "0 (0%)";
    [ObservableProperty] private int _networkDevicesCount;
    [ObservableProperty] private double _networkDevicesPercent;
    [ObservableProperty] private string _networkDevicesPercentText = "0 (0%)";

    [ObservableProperty] private string _lastScanTime = "Never";
    [ObservableProperty] private long _lastScanDurationMs;
    [ObservableProperty] private string _lastScanRange = "-";
    [ObservableProperty] private int _lastScanDeviceCount;

    // === Search & Filtering for Discovered Devices Table ===
    [ObservableProperty] private string _searchFilterText = string.Empty;
    partial void OnSearchFilterTextChanged(string value) => ApplyDeviceFilter();

    [ObservableProperty] private string _selectedFilterMode = "All"; // All, Online, Offline, Warning
    [ObservableProperty] private string _selectedTimeRange = "Last 60 Seconds";

    public ObservableCollection<DashboardDeviceItem> DiscoveredDevices { get; } = [];

    // === Real Device Types KPIs (Cameras, Printers, Servers) ===
    [ObservableProperty] private int _cameraCount;
    [ObservableProperty] private int _activeCameraCount;
    [ObservableProperty] private int _printerCount;
    [ObservableProperty] private int _activePrinterCount;
    [ObservableProperty] private int _serverCount;
    [ObservableProperty] private int _activeServerCount;

    // === System Stats ===
    [ObservableProperty] private string _cpuUsage = "0%";
    [ObservableProperty] private double _cpuPercent = 0;
    [ObservableProperty] private string _ramUsage = "0%";
    [ObservableProperty] private double _ramPercent = 0;
    [ObservableProperty] private string _ramUsed = "0 GB";
    [ObservableProperty] private string _ramTotal = "0 GB";
    [ObservableProperty] private string _diskUsage = "0%";
    [ObservableProperty] private double _diskPercent = 0;
    [ObservableProperty] private string _diskFree = "0 GB";
    [ObservableProperty] private string _diskTotal = "0 GB";

    // === Support Stats ===
    [ObservableProperty] private int _runningServices;
    [ObservableProperty] private int _stoppedServices;
    [ObservableProperty] private string _sqlStatus = "Checking...";
    [ObservableProperty] private string _iisStatus = "Checking...";

    // === Recent Operations ===
    public ObservableCollection<RecentScanItem> RecentScans { get; } = [];

    [ObservableProperty] private bool _isLoading;

    [RelayCommand]
    public async Task LoadAllDataAsync()
    {
        IsLoading = true;

        await Task.WhenAll(
            LoadNetworkStatsAsync(),
            LoadSystemStatsAsync(),
            LoadSupportStatsAsync(),
            LoadRecentOpsAsync());

        IsLoading = false;
    }

    private async Task LoadNetworkStatsAsync()
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var query = context.Scans
                .Include(s => s.Devices)
                .ThenInclude(d => d.Ports)
                .AsQueryable();

            if (_currentUser.UserId > 0)
            {
                query = query.Where(s => s.UserId == _currentUser.UserId);
            }
            else if (_currentUser.IsAdmin)
            {
                // Admin sees scans assigned to admin (null or 0)
                query = query.Where(s => s.UserId == null || s.UserId == 0);
            }
            else
            {
                // Regular user with no user id has no scans
                query = query.Where(s => false);
            }

            var latest = await query.OrderByDescending(s => s.Date).FirstOrDefaultAsync();

            if (latest is null || latest.Devices.Count == 0)
            {
                // If no scan exists yet, initialize with clean 0 state
                TotalDevices = 0;
                OnlineDevices = 0;
                OfflineDevices = 0;
                OnlinePercent = 0;
                AverageLatencyMs = 0;
                OpenPortsCount = 0;
                VulnerabilitiesCount = 0;
                CameraCount = 0;
                ActiveCameraCount = 0;
                PrinterCount = 0;
                ActivePrinterCount = 0;
                ServerCount = 0;
                ActiveServerCount = 0;
                WindowsCount = 0;
                WindowsPercent = 0;
                LinuxCount = 0;
                LinuxPercent = 0;
                IotCount = 0;
                IotPercent = 0;
                NetworkDevicesCount = 0;
                NetworkDevicesPercent = 0;
                WindowsPercentText = "0 (0%)";
                LinuxPercentText = "0 (0%)";
                IotPercentText = "0 (0%)";
                NetworkDevicesPercentText = "0 (0%)";
                _allDevicesMaster.Clear();
                DiscoveredDevices.Clear();
                return;
            }

            var devices = latest.Devices.ToList();
            TotalDevices = devices.Count;
            OnlineDevices = devices.Count(d => string.Equals(d.Status, "Online", StringComparison.OrdinalIgnoreCase));
            OfflineDevices = devices.Count(d => string.Equals(d.Status, "Offline", StringComparison.OrdinalIgnoreCase));
            OnlinePercent = TotalDevices > 0 ? ((double)OnlineDevices / TotalDevices * 100) : 0;

            var latencies = devices.Where(d => d.LatencyMs > 0).Select(d => (double)d.LatencyMs).ToList();
            AverageLatencyMs = latencies.Count > 0 ? (int)Math.Round(latencies.Average()) : 0;

            OpenPortsCount = devices.Sum(d => d.Ports?.Count(p => string.Equals(p.State, "Open", StringComparison.OrdinalIgnoreCase)) ?? 0);
            VulnerabilitiesCount = devices.Count(d => d.Ports != null && d.Ports.Any(p => string.Equals(p.State, "Open", StringComparison.OrdinalIgnoreCase) && p.PortNumber is 21 or 23 or 445 or 3389 or 8080));

            // Classify Specialized Device Types (IP Cameras, Printers, Servers) strictly on OPEN ports & valid identifiers
            var cameraList = devices.Where(d =>
                (d.DeviceType != null && (
                    d.DeviceType.Contains("Camera", StringComparison.OrdinalIgnoreCase) ||
                    d.DeviceType.Contains("IP Cam", StringComparison.OrdinalIgnoreCase) ||
                    d.DeviceType.Contains("CCTV", StringComparison.OrdinalIgnoreCase))) ||
                (d.Hostname != null && (
                    d.Hostname.Contains("CAM", StringComparison.OrdinalIgnoreCase) ||
                    d.Hostname.Contains("IPCAM", StringComparison.OrdinalIgnoreCase) ||
                    d.Hostname.Contains("NVR", StringComparison.OrdinalIgnoreCase) ||
                    d.Hostname.Contains("DVR", StringComparison.OrdinalIgnoreCase))) ||
                (d.Vendor != null && (
                    d.Vendor.Contains("Hikvision", StringComparison.OrdinalIgnoreCase) ||
                    d.Vendor.Contains("Dahua", StringComparison.OrdinalIgnoreCase) ||
                    d.Vendor.Contains("Axis", StringComparison.OrdinalIgnoreCase) ||
                    d.Vendor.Contains("Uniview", StringComparison.OrdinalIgnoreCase) ||
                    d.Vendor.Contains("Amcrest", StringComparison.OrdinalIgnoreCase) ||
                    d.Vendor.Contains("Foscam", StringComparison.OrdinalIgnoreCase) ||
                    d.Vendor.Contains("Vivotek", StringComparison.OrdinalIgnoreCase))) ||
                (d.Ports != null && d.Ports.Any(p => string.Equals(p.State, "Open", StringComparison.OrdinalIgnoreCase) && (p.PortNumber is 554 or 8554 or 37777 or 8899)))
            ).ToList();

            CameraCount = cameraList.Count;
            ActiveCameraCount = cameraList.Count(d => string.Equals(d.Status, "Online", StringComparison.OrdinalIgnoreCase));

            var printerList = devices.Where(d =>
                (d.DeviceType != null && (
                    d.DeviceType.Contains("Printer", StringComparison.OrdinalIgnoreCase) ||
                    d.DeviceType.Contains("Print", StringComparison.OrdinalIgnoreCase))) ||
                (d.Hostname != null && (
                    d.Hostname.Contains("PRINT", StringComparison.OrdinalIgnoreCase) ||
                    d.Hostname.Contains("HP_", StringComparison.OrdinalIgnoreCase) ||
                    d.Hostname.Contains("EPSON", StringComparison.OrdinalIgnoreCase) ||
                    d.Hostname.Contains("CANON", StringComparison.OrdinalIgnoreCase) ||
                    d.Hostname.Contains("KYOCERA", StringComparison.OrdinalIgnoreCase) ||
                    d.Hostname.Contains("BROTHER", StringComparison.OrdinalIgnoreCase) ||
                    d.Hostname.Contains("XEROX", StringComparison.OrdinalIgnoreCase))) ||
                (d.Vendor != null && (
                    d.Vendor.Contains("Epson", StringComparison.OrdinalIgnoreCase) ||
                    d.Vendor.Contains("Canon", StringComparison.OrdinalIgnoreCase) ||
                    d.Vendor.Contains("Kyocera", StringComparison.OrdinalIgnoreCase) ||
                    d.Vendor.Contains("Brother", StringComparison.OrdinalIgnoreCase) ||
                    d.Vendor.Contains("Xerox", StringComparison.OrdinalIgnoreCase) ||
                    d.Vendor.Contains("Ricoh", StringComparison.OrdinalIgnoreCase) ||
                    d.Vendor.Contains("Konica", StringComparison.OrdinalIgnoreCase))) ||
                (d.Ports != null && d.Ports.Any(p => string.Equals(p.State, "Open", StringComparison.OrdinalIgnoreCase) && (p.PortNumber is 515 or 631 or 9100)))
            ).ToList();

            PrinterCount = printerList.Count;
            ActivePrinterCount = printerList.Count(d => string.Equals(d.Status, "Online", StringComparison.OrdinalIgnoreCase));

            // Exclude already identified cameras and printers from servers & network devices
            var cameraIdSet = cameraList.Select(c => c.IP).ToHashSet();
            var printerIdSet = printerList.Select(p => p.IP).ToHashSet();

            // Specialized Server & Node Detection (Strictly true servers & nodes)
            var serverList = devices.Where(d =>
                !cameraIdSet.Contains(d.IP) &&
                !printerIdSet.Contains(d.IP) &&
                (
                    // 1. Explicit Server OS
                    (d.OS != null && (
                        d.OS.Contains("Windows Server", StringComparison.OrdinalIgnoreCase) ||
                        d.OS.Contains("Ubuntu Server", StringComparison.OrdinalIgnoreCase) ||
                        d.OS.Contains("CentOS", StringComparison.OrdinalIgnoreCase) ||
                        d.OS.Contains("RHEL", StringComparison.OrdinalIgnoreCase) ||
                        d.OS.Contains("Debian", StringComparison.OrdinalIgnoreCase) ||
                        d.OS.Contains("FreeBSD", StringComparison.OrdinalIgnoreCase) ||
                        d.OS.Contains("ESXi", StringComparison.OrdinalIgnoreCase) ||
                        d.OS.Contains("Proxmox", StringComparison.OrdinalIgnoreCase)
                    )) ||
                    // 2. Explicit Server Hostname
                    (d.Hostname != null && (
                        d.Hostname.StartsWith("SRV", StringComparison.OrdinalIgnoreCase) ||
                        d.Hostname.Contains("SERVER", StringComparison.OrdinalIgnoreCase) ||
                        d.Hostname.Contains("DC0", StringComparison.OrdinalIgnoreCase) ||
                        d.Hostname.Contains("DOMAIN", StringComparison.OrdinalIgnoreCase) ||
                        d.Hostname.Contains("ESXI", StringComparison.OrdinalIgnoreCase) ||
                        d.Hostname.Contains("PROXMOX", StringComparison.OrdinalIgnoreCase) ||
                        d.Hostname.Contains("STORAGE", StringComparison.OrdinalIgnoreCase) ||
                        d.Hostname.Contains("HYPERV", StringComparison.OrdinalIgnoreCase)
                    )) ||
                    // 3. Explicit Database or Enterprise Server Services (MSSQL 1433, MySQL 3306, Postgres 5432, LDAP 389, Kerberos 88, etc.)
                    (d.Ports != null && d.Ports.Any(p => string.Equals(p.State, "Open", StringComparison.OrdinalIgnoreCase) && (p.PortNumber is 1433 or 3306 or 5432 or 1521 or 27017 or 389 or 88))) ||
                    // 4. Multiple Enterprise Service Ports open simultaneously on server host
                    (d.Ports != null &&
                     d.Ports.Count(p => string.Equals(p.State, "Open", StringComparison.OrdinalIgnoreCase)) >= 3 &&
                     d.Ports.Any(p => string.Equals(p.State, "Open", StringComparison.OrdinalIgnoreCase) && p.PortNumber is 3389 or 22 or 445))
                )
            ).ToList();

            ServerCount = serverList.Count;
            ActiveServerCount = serverList.Count(d => string.Equals(d.Status, "Online", StringComparison.OrdinalIgnoreCase));

            // Classify OS Distribution based on actual scan data
            WindowsCount = devices.Count(d =>
                (d.OS?.Contains("Windows", StringComparison.OrdinalIgnoreCase) == true) ||
                (d.DeviceType?.Contains("Windows", StringComparison.OrdinalIgnoreCase) == true) ||
                (d.Hostname?.StartsWith("WIN-", StringComparison.OrdinalIgnoreCase) == true) ||
                (d.Hostname?.StartsWith("DESKTOP-", StringComparison.OrdinalIgnoreCase) == true));

            LinuxCount = devices.Count(d =>
                (d.OS?.Contains("Linux", StringComparison.OrdinalIgnoreCase) == true) ||
                (d.DeviceType?.Contains("Linux", StringComparison.OrdinalIgnoreCase) == true) ||
                (d.Hostname?.Contains("UBUNTU", StringComparison.OrdinalIgnoreCase) == true) ||
                (d.Hostname?.Contains("DEBIAN", StringComparison.OrdinalIgnoreCase) == true) ||
                (d.Hostname?.Contains("CENTOS", StringComparison.OrdinalIgnoreCase) == true));

            IotCount = cameraList.Count + devices.Count(d =>
                (d.DeviceType != null && (d.DeviceType.Contains("IoT", StringComparison.OrdinalIgnoreCase) || d.DeviceType.Contains("Embedded", StringComparison.OrdinalIgnoreCase))) ||
                (d.Hostname?.Contains("RASPBERRY", StringComparison.OrdinalIgnoreCase) == true) ||
                (d.Vendor?.Contains("Raspberry", StringComparison.OrdinalIgnoreCase) == true));

            NetworkDevicesCount = printerList.Count + devices.Count(d =>
                (d.DeviceType != null && (d.DeviceType is "Router" or "Switch" or "Gateway" or "Access Point")) ||
                (d.Vendor?.Contains("Cisco", StringComparison.OrdinalIgnoreCase) == true) ||
                (d.Vendor?.Contains("MikroTik", StringComparison.OrdinalIgnoreCase) == true) ||
                (d.Vendor?.Contains("TP-Link", StringComparison.OrdinalIgnoreCase) == true) ||
                (d.Vendor?.Contains("Ubiquiti", StringComparison.OrdinalIgnoreCase) == true) ||
                (d.Hostname?.Contains("ROUTER", StringComparison.OrdinalIgnoreCase) == true) ||
                (d.Hostname?.Contains("GATEWAY", StringComparison.OrdinalIgnoreCase) == true));

            WindowsPercent = TotalDevices > 0 ? (WindowsCount * 100.0 / TotalDevices) : 0;
            LinuxPercent = TotalDevices > 0 ? (LinuxCount * 100.0 / TotalDevices) : 0;
            IotPercent = TotalDevices > 0 ? (IotCount * 100.0 / TotalDevices) : 0;
            NetworkDevicesPercent = TotalDevices > 0 ? (NetworkDevicesCount * 100.0 / TotalDevices) : 0;

            WindowsPercentText = $"{WindowsCount} ({WindowsPercent:F1}%)";
            LinuxPercentText = $"{LinuxCount} ({LinuxPercent:F1}%)";
            IotPercentText = $"{IotCount} ({IotPercent:F1}%)";
            NetworkDevicesPercentText = $"{NetworkDevicesCount} ({NetworkDevicesPercent:F1}%)";

            LastScanTime = latest.Date.ToString("yyyy-MM-dd HH:mm:ss");
            LastScanDurationMs = latest.DurationMs;
            LastScanRange = $"{latest.StartIP} - {latest.EndIP}";
            LastScanDeviceCount = devices.Count;

            // Populate Master List from Real Devices
            _allDevicesMaster.Clear();
            foreach (var d in devices)
            {
                var isOnline = string.Equals(d.Status, "Online", StringComparison.OrdinalIgnoreCase);
                var isWarn = isOnline && d.LatencyMs > 20;
                var status = isWarn ? "Warning" : (isOnline ? "Online" : "Offline");
                var color = isWarn ? "#F59E0B" : (isOnline ? "#10B981" : "#EF4444");

                _allDevicesMaster.Add(new DashboardDeviceItem
                {
                    Id = d.Id,
                    Status = status,
                    IPAddress = d.IP,
                    MACAddress = string.IsNullOrWhiteSpace(d.MAC) ? "-" : d.MAC,
                    Hostname = string.IsNullOrWhiteSpace(d.Hostname) ? "-" : d.Hostname,
                    Manufacturer = string.IsNullOrWhiteSpace(d.Vendor) ? "Unknown" : d.Vendor,
                    Latency = isOnline ? $"{d.LatencyMs}ms" : "-",
                    LatencyMs = (int)d.LatencyMs,
                    OpenPorts = d.Ports?.Count ?? 0,
                    StatusColor = color
                });
            }

            ApplyDeviceFilter();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading network stats: {ex.Message}");
        }
    }

    private void ApplyDeviceFilter()
    {
        DiscoveredDevices.Clear();
        var filter = SearchFilterText?.Trim().ToLower() ?? "";

        var filtered = _allDevicesMaster.AsEnumerable();

        if (!string.IsNullOrEmpty(filter))
        {
            filtered = filtered.Where(d =>
                d.IPAddress.ToLower().Contains(filter) ||
                d.Hostname.ToLower().Contains(filter) ||
                d.MACAddress.ToLower().Contains(filter) ||
                d.Manufacturer.ToLower().Contains(filter) ||
                d.Status.ToLower().Contains(filter));
        }

        if (SelectedFilterMode == "Online")
            filtered = filtered.Where(d => d.Status == "Online");
        else if (SelectedFilterMode == "Offline")
            filtered = filtered.Where(d => d.Status == "Offline");
        else if (SelectedFilterMode == "Warning")
            filtered = filtered.Where(d => d.Status == "Warning");

        foreach (var item in filtered)
            DiscoveredDevices.Add(item);
    }

    [RelayCommand]
    private void ToggleFilter()
    {
        SelectedFilterMode = SelectedFilterMode switch
        {
            "All" => "Online",
            "Online" => "Warning",
            "Warning" => "Offline",
            _ => "All"
        };
        ApplyDeviceFilter();
    }

    [RelayCommand]
    private void ViewAllOS()
    {
        _navigation.NavigateToNetworkDiscovery();
    }

    [RelayCommand]
    private void ViewDevice(DashboardDeviceItem? item)
    {
        if (item != null)
            _navigation.NavigateToDeviceDetails(item.Id);
        else
            _navigation.NavigateToNetworkDiscovery();
    }

    [RelayCommand]
    private void QuickPing(DashboardDeviceItem? item)
    {
        _navigation.NavigateToPingTool();
    }

    [RelayCommand]
    private void CopyIp(DashboardDeviceItem? item)
    {
        if (item != null && !string.IsNullOrEmpty(item.IPAddress))
        {
            System.Windows.Clipboard.SetText(item.IPAddress);
            System.Windows.MessageBox.Show($"IP Address {item.IPAddress} copied to clipboard.", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    [RelayCommand]
    private void CopyMac(DashboardDeviceItem? item)
    {
        if (item != null && !string.IsNullOrEmpty(item.MACAddress))
        {
            System.Windows.Clipboard.SetText(item.MACAddress);
            System.Windows.MessageBox.Show($"MAC Address {item.MACAddress} copied to clipboard.", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    [RelayCommand]
    private async Task ExportDevicesAsync()
    {
        try
        {
            if (DiscoveredDevices.Count == 0)
            {
                System.Windows.MessageBox.Show("No devices available to export.", "Export", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var filePath = Path.Combine(desktop, $"Discovered_Devices_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            var lines = new List<string> { "Status,IP Address,MAC Address,Hostname,Manufacturer,Latency,Open Ports" };
            foreach (var d in DiscoveredDevices)
            {
                lines.Add($"\"{d.Status}\",\"{d.IPAddress}\",\"{d.MACAddress}\",\"{d.Hostname}\",\"{d.Manufacturer}\",\"{d.Latency}\",{d.OpenPorts}");
            }

            await File.WriteAllLinesAsync(filePath, lines);
            System.Windows.MessageBox.Show($"Exported {DiscoveredDevices.Count} devices successfully to:\n{filePath}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to export: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
        public MEMORYSTATUSEX() { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)); }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    private async Task LoadSystemStatsAsync()
    {
        // 1. CPU (fast non-blocking sample)
        try
        {
            using var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            cpuCounter.NextValue();
            await Task.Delay(100);
            var cpu = cpuCounter.NextValue();
            CpuUsage = $"{cpu:F1}%";
            CpuPercent = Math.Clamp(cpu, 0, 100);
        }
        catch { CpuUsage = "N/A"; CpuPercent = 0; }

        // 2. RAM (instant Win32 API < 0.01ms)
        try
        {
            var mem = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(mem))
            {
                var total = mem.ullTotalPhys / 1073741824.0;
                var free = mem.ullAvailPhys / 1073741824.0;
                var used = total - free;
                RamTotal = $"{total:F1} GB";
                RamUsed = $"{used:F1} GB";
                var pct = total > 0 ? (used / total * 100) : 0;
                RamUsage = $"{pct:F1}%";
                RamPercent = Math.Clamp(pct, 0, 100);
            }
        }
        catch { RamUsage = "N/A"; RamPercent = 0; }

        // 3. Disk
        try
        {
            var drives = DriveInfo.GetDrives().Where(d => d.IsReady).ToList();
            if (drives.Count > 0)
            {
                var total = drives.Sum(d => d.TotalSize);
                var free = drives.Sum(d => d.AvailableFreeSpace);
                DiskTotal = $"{total / 1073741824.0:F1} GB";
                DiskFree = $"{free / 1073741824.0:F1} GB";
                var pct = total > 0 ? (100.0 - free / (double)total * 100.0) : 0;
                DiskUsage = $"{pct:F1}%";
                DiskPercent = Math.Clamp(pct, 0, 100);
            }
        }
        catch { DiskUsage = "N/A"; DiskPercent = 0; }
    }

    private async Task LoadSupportStatsAsync()
    {
        try
        {
            var running = 0;
            var stopped = 0;
            foreach (var svc in ServiceController.GetServices())
            {
                if (svc.Status == ServiceControllerStatus.Running) running++;
                else if (svc.Status == ServiceControllerStatus.Stopped) stopped++;
            }
            RunningServices = running;
            StoppedServices = stopped;
        }
        catch { }

        try
        {
            using var conn = new SqlConnection("Server=.;Database=master;Trusted_Connection=True;TrustServerCertificate=True;Connection Timeout=1;");
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            await conn.OpenAsync(cts.Token);
            SqlStatus = "Connected";
        }
        catch { SqlStatus = "Unavailable"; }

        try
        {
            var services = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == "W3SVC");
            IisStatus = services?.Status.ToString() ?? "Not Installed";
        }
        catch { IisStatus = "N/A"; }
    }

    private async Task LoadRecentOpsAsync()
    {
        RecentScans.Clear();

        await using var context = await _contextFactory.CreateDbContextAsync();
        var query = context.Scans.AsQueryable();

        if (_currentUser.UserId > 0)
        {
            query = query.Where(s => s.UserId == _currentUser.UserId);
        }
        else if (_currentUser.IsAdmin)
        {
            query = query.Where(s => s.UserId == null || s.UserId == 0);
        }
        else
        {
            query = query.Where(s => false);
        }

        var scans = await query.OrderByDescending(s => s.Date).Take(5).ToListAsync();
        foreach (var s in scans)
            RecentScans.Add(new RecentScanItem
            {
                Id = s.Id,
                Date = s.Date.ToString("yyyy-MM-dd HH:mm"),
                Range = $"{s.StartIP} - {s.EndIP}",
                Duration = $"{s.DurationMs}ms",
                Description = s.Description ?? "-",
            });
    }
}

public sealed class NetworkAdapterItem
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string IPAddress { get; set; } = "";
    public string Subnet { get; set; } = "";
}

public sealed record RecentScanItem
{
    public int Id { get; init; }
    public string Date { get; init; } = "";
    public string Range { get; init; } = "";
    public string Duration { get; init; } = "";
    public string Description { get; init; } = "";
}

public sealed class QuickActionItem
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Icon { get; init; } = "";
    public ICommand Command { get; init; } = null!;
}

public sealed class DashboardDeviceItem
{
    public int Id { get; init; }
    public string Status { get; init; } = "Online";
    public string IPAddress { get; init; } = "";
    public string MACAddress { get; init; } = "";
    public string Hostname { get; init; } = "";
    public string Manufacturer { get; init; } = "";
    public string Latency { get; init; } = "10ms";
    public int LatencyMs { get; init; } = 10;
    public int OpenPorts { get; init; } = 0;
    public string StatusColor { get; init; } = "#10B981";
}

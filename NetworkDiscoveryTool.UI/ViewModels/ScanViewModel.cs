using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using NetworkDiscoveryTool.Core.Interfaces;
using NetworkDiscoveryTool.Core.Models;
using NetworkDiscoveryTool.Data;
using NetworkDiscoveryTool.Helpers;
using NetworkDiscoveryTool.UI.Services;

namespace NetworkDiscoveryTool.UI.ViewModels;

public sealed partial class ScanViewModel : ObservableObject
{
    private readonly INetworkScanner _scanner;
    private readonly DashboardViewModel _dashboardVm;
    private readonly CurrentUserService _currentUser;
    private readonly IOperationHistoryService _history;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly INavigationService _nav;
    private CancellationTokenSource? _cts;

    // === Scan Configuration ===
    [ObservableProperty] private string _startIP = "192.168.1.1";
    [ObservableProperty] private string _endIP = "192.168.1.254";
    [ObservableProperty] private int _maxThreads = 250;
    [ObservableProperty] private int _pingTimeout = 2000;
    [ObservableProperty] private int _portTimeout = 500;
    [ObservableProperty] private string _scanDescription = string.Empty;
    [ObservableProperty] private string _scanNotes = string.Empty;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private int _progress;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private bool _showSaveDialog;

    // === Presets ===
    public ObservableCollection<string> ScanPresets { get; } =
    [
        "Full Scan (Ports + OS)",
        "Quick Ping Scan",
        "Intense Vulnerability Scan",
        "Custom Port Scan"
    ];

    [ObservableProperty] private string _selectedPreset = "Full Scan (Ports + OS)";

    // === Stat Cards (5 KPI Metrics) ===
    [ObservableProperty] private int _onlineCount;
    [ObservableProperty] private string _onlinePercentText = "0.0%";
    [ObservableProperty] private int _offlineCount;
    [ObservableProperty] private string _offlinePercentText = "0.0%";
    [ObservableProperty] private int _serverCount;
    [ObservableProperty] private string _serverPercentText = "0.0%";
    [ObservableProperty] private int _switchCount;
    [ObservableProperty] private string _switchPercentText = "0.0%";
    [ObservableProperty] private int _printerCount;
    [ObservableProperty] private string _printerPercentText = "0.0%";
    [ObservableProperty] private int _totalCount;

    // === Search & Filtering ===
    public ObservableCollection<string> FilterModes { get; } =
    [
        "All Devices",
        "Online",
        "Offline",
        "Servers",
        "Cameras",
        "Printers",
        "Switches & Routers"
    ];

    [ObservableProperty] private string _searchFilterText = string.Empty;
    partial void OnSearchFilterTextChanged(string value) => ApplyFilter();

    [ObservableProperty] private string _selectedFilterMode = "All Devices";
    partial void OnSelectedFilterModeChanged(string value) => ApplyFilter();

    // === Column Sorting ===
    [ObservableProperty] private string _currentSortColumn = "IPAddress";
    [ObservableProperty] private bool _isSortAscending = true;

    [RelayCommand]
    public void SortByColumn(string columnName)
    {
        if (string.Equals(CurrentSortColumn, columnName, StringComparison.OrdinalIgnoreCase))
        {
            IsSortAscending = !IsSortAscending;
        }
        else
        {
            CurrentSortColumn = columnName;
            IsSortAscending = true;
        }
        ApplyFilter();
    }

    [ObservableProperty] private bool _hasResults;

    public List<Device>? LastScanResults { get; private set; }
    private readonly List<ScannerDeviceItem> _allMasterDevices = [];
    public ObservableCollection<ScannerDeviceItem> DiscoveredDevices { get; } = [];
    public ObservableCollection<Device> ScanResults { get; } = [];

    private readonly ISettingsService _settings;

    public ScanViewModel(
        INetworkScanner scanner,
        DashboardViewModel dashboardVm,
        CurrentUserService currentUser,
        IOperationHistoryService history,
        IDbContextFactory<AppDbContext> contextFactory,
        INavigationService nav,
        ISettingsService settings)
    {
        _scanner = scanner;
        _dashboardVm = dashboardVm;
        _currentUser = currentUser;
        _history = history;
        _contextFactory = contextFactory;
        _nav = nav;
        _settings = settings;

        HasResults = false;
        _currentUser.UserChanged += () =>
        {
            _allMasterDevices.Clear();
            DiscoveredDevices.Clear();
            ScanResults.Clear();
            HasResults = false;
            _ = LoadInitialDataAsync();
        };
    }

    [RelayCommand]
    public async Task LoadInitialDataAsync()
    {
        try
        {
            var allSettings = await _settings.GetAllAsync();
            if (allSettings.TryGetValue("StartIP", out var sip) && !string.IsNullOrWhiteSpace(sip)) StartIP = sip;
            if (allSettings.TryGetValue("EndIP", out var eip) && !string.IsNullOrWhiteSpace(eip)) EndIP = eip;
            if (allSettings.TryGetValue("MaxThreads", out var mt) && int.TryParse(mt, out var mtv)) MaxThreads = mtv;
            if (allSettings.TryGetValue("PingTimeout", out var pt) && int.TryParse(pt, out var ptv)) PingTimeout = ptv;
            if (allSettings.TryGetValue("PortTimeout", out var pot) && int.TryParse(pot, out var potv)) PortTimeout = potv;

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
                query = query.Where(s => s.UserId == null || s.UserId == 0);
            }
            else
            {
                query = query.Where(s => false);
            }

            var latestScan = await query
                .OrderByDescending(s => s.Date)
                .FirstOrDefaultAsync();

            if (latestScan != null && latestScan.Devices.Count > 0)
            {
                PopulateDevicesList(latestScan.Devices.ToList());
                if (string.IsNullOrWhiteSpace(sip)) StartIP = latestScan.StartIP;
                if (string.IsNullOrWhiteSpace(eip)) EndIP = latestScan.EndIP;
            }
            else
            {
                _allMasterDevices.Clear();
                DiscoveredDevices.Clear();
                ScanResults.Clear();
                HasResults = false;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ScanViewModel] LoadInitialData error: {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartScanAsync()
    {
        var targets = NetworkDiscoveryTool.Services.NetworkScannerService.ParseIpTargets(StartIP, EndIP);
        if (targets.Count == 0)
        {
            System.Windows.MessageBox.Show("Please enter valid Start and End IP addresses (e.g. 192.168.1.1 to 192.168.2.254, or separated by commas).", "Invalid IP Range", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _cts = new CancellationTokenSource();
        IsScanning = true;
        Progress = 0;
        StatusText = "Scanning Range...";
        ShowSaveDialog = false;
        var sw = Stopwatch.StartNew();

        var progress = new Progress<int>(value =>
        {
            Progress = value;
            StatusText = $"Scanning... {value}%";
        });

        int portTimeout = SelectedPreset == "Quick Ping Scan" ? 100 : PortTimeout;

        try
        {
            var devices = await _scanner.ScanRangeAsync(
                StartIP, EndIP,
                maxThreads: MaxThreads,
                pingTimeoutMs: PingTimeout,
                portTimeoutMs: portTimeout,
                description: string.IsNullOrWhiteSpace(ScanDescription) ? SelectedPreset : ScanDescription,
                notes: ScanNotes,
                userId: _currentUser.UserId > 0 ? _currentUser.UserId : null,
                progress: progress,
                ct: _cts.Token);

            sw.Stop();
            LastScanResults = devices;
            PopulateDevicesList(devices);

            StatusText = $"Completed - Found {devices.Count} devices in {sw.Elapsed.TotalSeconds:F1}s";
            ShowSaveDialog = true;
            await _dashboardVm.LoadAllDataCommand.ExecuteAsync(null);
            await _history.LogAsync("Network Scan", $"{StartIP} → {EndIP} | {devices.Count} devices", "Success", sw.ElapsedMilliseconds, _currentUser.Username);

            // Auto-Save snapshot if enabled in Settings
            try
            {
                var allSettings = await _settings.GetAllAsync();
                if (allSettings.TryGetValue("AutoSave", out var auto) && auto == "True")
                {
                    var exportDir = allSettings.TryGetValue("DefaultSaveFolder", out var dsf) && !string.IsNullOrWhiteSpace(dsf)
                        ? dsf
                        : (allSettings.TryGetValue("ExportFolder", out var ef) && !string.IsNullOrWhiteSpace(ef) ? ef : "Exports");
                    
                    Directory.CreateDirectory(exportDir);
                    var autoFilePath = Path.Combine(exportDir, $"AutoScan_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                    var sb = new StringBuilder();
                    sb.AppendLine("Status,IP Address,MAC Address,Hostname,Vendor,Latency (ms),Open Ports,Services");
                    foreach (var d in _allMasterDevices)
                    {
                        sb.AppendLine($"\"{d.Status}\",\"{d.IPAddress}\",\"{d.MACAddress}\",\"{d.Hostname}\",\"{d.Vendor}\",\"{d.LatencyMs}\",\"{d.OpenPortsCount}\",\"{d.ServicesText}\"");
                    }
                    await File.WriteAllTextAsync(autoFilePath, sb.ToString());
                }
            }
            catch (Exception autoEx)
            {
                Debug.WriteLine($"[Scan AutoSave Error] {autoEx.Message}");
            }
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            StatusText = "Scan cancelled";
            await _history.LogAsync("Network Scan", $"{StartIP} → {EndIP} | Cancelled", "Cancelled", sw.ElapsedMilliseconds, _currentUser.Username);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var msg = ex.InnerException?.Message ?? ex.Message;
            StatusText = $"Error: {msg}";
            Debug.WriteLine($"[Scan ERROR] {ex}");
            await _history.LogAsync("Network Scan", $"{StartIP} → {EndIP} | {msg}", "Failed", sw.ElapsedMilliseconds, _currentUser.Username);
        }
        finally
        {
            IsScanning = false;
            Progress = 100;
        }
    }

    private void PopulateDevicesList(List<Device> devices)
    {
        ScanResults.Clear();
        _allMasterDevices.Clear();

        foreach (var device in devices)
        {
            ScanResults.Add(device);

            var openPortsList = device.Ports?
                .Where(p => string.Equals(p.State, "Open", StringComparison.OrdinalIgnoreCase))
                .ToList() ?? [];

            var servicesList = openPortsList
                .Select(p => p.Service ?? p.PortNumber.ToString())
                .Distinct()
                .Take(3)
                .ToList();

            string servicesText = servicesList.Count > 0
                ? string.Join(", ", servicesList) + (openPortsList.Count > 3 ? $" (+{openPortsList.Count - 3})" : "")
                : "-";

            string vendor = !string.IsNullOrWhiteSpace(device.Vendor) && device.Vendor != "Unknown" && device.Vendor != "-"
                ? device.Vendor
                : OuiLookup.Lookup(device.MAC, device.Hostname, openPortsList.Select(p => p.PortNumber));
            string vendorInitial = GetVendorBadgeText(vendor);

            string devType = device.DeviceType ?? "";
            string v = vendor;
            string h = device.Hostname ?? "";

            bool isRouter = device.IP.EndsWith(".1") || devType.Equals("Router", StringComparison.OrdinalIgnoreCase);
            bool isPrinterDev = devType.Contains("Printer", StringComparison.OrdinalIgnoreCase) ||
                             openPortsList.Any(p => p.PortNumber is 9100 or 515 or 631) ||
                             v.Contains("Canon", StringComparison.OrdinalIgnoreCase) || v.Contains("Epson", StringComparison.OrdinalIgnoreCase) || v.Contains("Xerox", StringComparison.OrdinalIgnoreCase) || v.Contains("Brother", StringComparison.OrdinalIgnoreCase) || v.Contains("Kyocera", StringComparison.OrdinalIgnoreCase) || v.Contains("Ricoh", StringComparison.OrdinalIgnoreCase) || (v.Contains("HP", StringComparison.OrdinalIgnoreCase) && openPortsList.Any(p => p.PortNumber == 9100));

            // Server classification: evaluated before camera so media/streaming servers (with RTSP 554) are recognized as servers
            bool isServerCandidate = devType.Contains("Server", StringComparison.OrdinalIgnoreCase) ||
                            h.Contains("SERVER", StringComparison.OrdinalIgnoreCase) ||
                            h.Contains("SRV", StringComparison.OrdinalIgnoreCase) ||
                            h.Contains("CLUSTER", StringComparison.OrdinalIgnoreCase) ||
                            h.Contains("ACTIVE", StringComparison.OrdinalIgnoreCase) ||
                            h.Contains("GET-UP", StringComparison.OrdinalIgnoreCase) ||
                            h.Contains("EXCHANGE", StringComparison.OrdinalIgnoreCase) ||
                            h.Contains("ESXI", StringComparison.OrdinalIgnoreCase) ||
                            h.Contains("VMWARE", StringComparison.OrdinalIgnoreCase) ||
                            h.Contains("PROXMOX", StringComparison.OrdinalIgnoreCase) ||
                            openPortsList.Any(p => p.PortNumber is 1433 or 3306 or 5432 or 1521 or 8006);

            bool isServer = isServerCandidate && !isRouter && !isPrinterDev;

            bool isCamera = !isServer && (devType.Contains("Camera", StringComparison.OrdinalIgnoreCase) ||
                            openPortsList.Any(p => p.PortNumber is 554 or 37777) ||
                            v.Contains("Hikvision", StringComparison.OrdinalIgnoreCase) || v.Contains("Dahua", StringComparison.OrdinalIgnoreCase) || v.Contains("Uniview", StringComparison.OrdinalIgnoreCase) || v.Contains("Axis", StringComparison.OrdinalIgnoreCase) || v.Contains("Ezviz", StringComparison.OrdinalIgnoreCase) || v.Contains("Imou", StringComparison.OrdinalIgnoreCase) || h.Contains("CAM", StringComparison.OrdinalIgnoreCase) || h.Contains("DVR", StringComparison.OrdinalIgnoreCase) || h.Contains("NVR", StringComparison.OrdinalIgnoreCase) ||
                            (openPortsList.Any(p => p.PortNumber == 8000) && (v.Contains("Hikvision", StringComparison.OrdinalIgnoreCase) || v.Contains("Dahua", StringComparison.OrdinalIgnoreCase) || v.Contains("Uniview", StringComparison.OrdinalIgnoreCase) || v.Contains("Axis", StringComparison.OrdinalIgnoreCase) || v.Contains("Ezviz", StringComparison.OrdinalIgnoreCase) || v.Contains("Imou", StringComparison.OrdinalIgnoreCase))));
            bool isSwitch = !isServer && !isCamera && (devType.Contains("Switch", StringComparison.OrdinalIgnoreCase) ||
                             devType.Contains("Router / Switch", StringComparison.OrdinalIgnoreCase) ||
                             (openPortsList.Any(p => p.PortNumber == 161) && (v.Contains("Cisco", StringComparison.OrdinalIgnoreCase) || v.Contains("TP-Link", StringComparison.OrdinalIgnoreCase) || v.Contains("D-Link", StringComparison.OrdinalIgnoreCase) || v.Contains("MikroTik", StringComparison.OrdinalIgnoreCase) || v.Contains("Ubiquiti", StringComparison.OrdinalIgnoreCase) || v.Contains("Netgear", StringComparison.OrdinalIgnoreCase) || v.Contains("Huawei", StringComparison.OrdinalIgnoreCase)))) && !isRouter;

            var item = new ScannerDeviceItem
            {
                Id = device.Id,
                Status = string.Equals(device.Status, "Online", StringComparison.OrdinalIgnoreCase) ? "Online" : "Offline",
                IPAddress = device.IP,
                MACAddress = !string.IsNullOrWhiteSpace(device.MAC) ? device.MAC : "-",
                Hostname = !string.IsNullOrWhiteSpace(device.Hostname) ? device.Hostname : "-",
                Vendor = vendor,
                VendorInitial = vendorInitial,
                LatencyText = device.LatencyMs > 0 ? $"{device.LatencyMs}ms" : "-",
                LatencyMs = (int)device.LatencyMs,
                OpenPortsCount = openPortsList.Count,
                ServicesText = servicesText,
                DeviceType = device.DeviceType ?? "Unknown",
                IsServer = isServer,
                IsSwitch = isSwitch,
                IsPrinter = isPrinterDev,
                RawDevice = device
            };

            _allMasterDevices.Add(item);
        }

        RecomputeStats(devices);
        SelectedFilterMode = "All Devices";
        SearchFilterText = string.Empty;
        ApplyFilter();
        HasResults = _allMasterDevices.Count > 0;
    }

    private void RecomputeStats(List<Device> devices)
    {
        TotalCount = devices.Count;
        OnlineCount = devices.Count(d => string.Equals(d.Status, "Online", StringComparison.OrdinalIgnoreCase));
        OfflineCount = TotalCount - OnlineCount;

        ServerCount = _allMasterDevices.Count(d => d.IsServer && d.Status == "Online");
        SwitchCount = _allMasterDevices.Count(d => d.IsSwitch && d.Status == "Online");
        PrinterCount = _allMasterDevices.Count(d => d.IsPrinter && d.Status == "Online");

        double total = TotalCount > 0 ? TotalCount : 1;
        OnlinePercentText = $"{(OnlineCount / total * 100):F1}%";
        OfflinePercentText = $"{(OfflineCount / total * 100):F1}%";
        ServerPercentText = $"{(ServerCount / total * 100):F1}%";
        SwitchPercentText = $"{(SwitchCount / total * 100):F1}%";
        PrinterPercentText = $"{(PrinterCount / total * 100):F1}%";
    }

    private void ApplyFilter()
    {
        var query = _allMasterDevices.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchFilterText))
        {
            var term = SearchFilterText.Trim();
            query = query.Where(d =>
                d.IPAddress.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                d.Hostname.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                d.MACAddress.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                d.Vendor.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                d.ServicesText.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        if (SelectedFilterMode == "Online")
            query = query.Where(d => d.Status == "Online");
        else if (SelectedFilterMode == "Offline")
            query = query.Where(d => d.Status == "Offline");
        else if (SelectedFilterMode == "Servers")
            query = query.Where(d => d.IsServer);
        else if (SelectedFilterMode == "Cameras")
            query = query.Where(d => d.DeviceType.Contains("Camera", StringComparison.OrdinalIgnoreCase) ||
                                     d.Vendor.Contains("Hikvision", StringComparison.OrdinalIgnoreCase) ||
                                     d.Vendor.Contains("Dahua", StringComparison.OrdinalIgnoreCase) ||
                                     d.ServicesText.Contains("rtsp", StringComparison.OrdinalIgnoreCase));
        else if (SelectedFilterMode == "Printers")
            query = query.Where(d => d.IsPrinter);
        else if (SelectedFilterMode == "Switches & Routers")
            query = query.Where(d => d.IsSwitch || d.DeviceType.Contains("Switch", StringComparison.OrdinalIgnoreCase) || d.DeviceType.Contains("Router", StringComparison.OrdinalIgnoreCase) || d.IPAddress.EndsWith(".1"));

        // Dynamic multi-column sorting (Alphabetical, Numeric, Vendor grouping)
        List<ScannerDeviceItem> sorted;
        switch (CurrentSortColumn)
        {
            case "Vendor":
                sorted = IsSortAscending
                    ? query.OrderBy(d => string.IsNullOrWhiteSpace(d.Vendor) || d.Vendor == "Unknown" ? 1 : 0)
                           .ThenBy(d => d.Vendor, StringComparer.OrdinalIgnoreCase)
                           .ThenBy(d => ParseIpToUInt(d.IPAddress))
                           .ToList()
                    : query.OrderBy(d => string.IsNullOrWhiteSpace(d.Vendor) || d.Vendor == "Unknown" ? 1 : 0)
                           .ThenByDescending(d => d.Vendor, StringComparer.OrdinalIgnoreCase)
                           .ThenBy(d => ParseIpToUInt(d.IPAddress))
                           .ToList();
                break;

            case "Hostname":
                sorted = IsSortAscending
                    ? query.OrderBy(d => string.IsNullOrWhiteSpace(d.Hostname) ? 1 : 0)
                           .ThenBy(d => d.Hostname, StringComparer.OrdinalIgnoreCase)
                           .ThenBy(d => ParseIpToUInt(d.IPAddress))
                           .ToList()
                    : query.OrderBy(d => string.IsNullOrWhiteSpace(d.Hostname) ? 1 : 0)
                           .ThenByDescending(d => d.Hostname, StringComparer.OrdinalIgnoreCase)
                           .ThenBy(d => ParseIpToUInt(d.IPAddress))
                           .ToList();
                break;

            case "Status":
                sorted = IsSortAscending
                    ? query.OrderBy(d => d.Status == "Online" ? 0 : 1)
                           .ThenBy(d => ParseIpToUInt(d.IPAddress))
                           .ToList()
                    : query.OrderBy(d => d.Status == "Offline" ? 0 : 1)
                           .ThenBy(d => ParseIpToUInt(d.IPAddress))
                           .ToList();
                break;

            case "MACAddress":
                sorted = IsSortAscending
                    ? query.OrderBy(d => d.MACAddress, StringComparer.OrdinalIgnoreCase)
                           .ThenBy(d => ParseIpToUInt(d.IPAddress))
                           .ToList()
                    : query.OrderByDescending(d => d.MACAddress, StringComparer.OrdinalIgnoreCase)
                           .ThenBy(d => ParseIpToUInt(d.IPAddress))
                           .ToList();
                break;

            case "Latency":
                sorted = IsSortAscending
                    ? query.OrderBy(d => d.LatencyMs)
                           .ThenBy(d => ParseIpToUInt(d.IPAddress))
                           .ToList()
                    : query.OrderByDescending(d => d.LatencyMs)
                           .ThenBy(d => ParseIpToUInt(d.IPAddress))
                           .ToList();
                break;

            case "Ports":
                sorted = IsSortAscending
                    ? query.OrderBy(d => d.OpenPortsCount)
                           .ThenBy(d => ParseIpToUInt(d.IPAddress))
                           .ToList()
                    : query.OrderByDescending(d => d.OpenPortsCount)
                           .ThenBy(d => ParseIpToUInt(d.IPAddress))
                           .ToList();
                break;

            case "Services":
                sorted = IsSortAscending
                    ? query.OrderBy(d => string.IsNullOrWhiteSpace(d.ServicesText) ? 1 : 0)
                           .ThenBy(d => d.ServicesText, StringComparer.OrdinalIgnoreCase)
                           .ThenBy(d => ParseIpToUInt(d.IPAddress))
                           .ToList()
                    : query.OrderBy(d => string.IsNullOrWhiteSpace(d.ServicesText) ? 1 : 0)
                           .ThenByDescending(d => d.ServicesText, StringComparer.OrdinalIgnoreCase)
                           .ThenBy(d => ParseIpToUInt(d.IPAddress))
                           .ToList();
                break;

            case "IPAddress":
            default:
                sorted = IsSortAscending
                    ? query.OrderBy(d => ParseIpToUInt(d.IPAddress)).ToList()
                    : query.OrderByDescending(d => ParseIpToUInt(d.IPAddress)).ToList();
                break;
        }

        DiscoveredDevices.Clear();
        foreach (var item in sorted)
            DiscoveredDevices.Add(item);
    }

    private static uint ParseIpToUInt(string ipStr)
    {
        if (System.Net.IPAddress.TryParse(ipStr, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
        }
        return uint.MaxValue;
    }

    [RelayCommand]
    private void StopScan()
    {
        _cts?.Cancel();
        StatusText = "Stopping...";
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        if (_allMasterDevices.Count == 0) return;

        var saveDialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"Scan_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };

        if (saveDialog.ShowDialog() == true)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Status,IP Address,MAC Address,Hostname,Vendor,Latency (ms),Open Ports,Services");
            foreach (var d in _allMasterDevices)
            {
                sb.AppendLine($"\"{d.Status}\",\"{d.IPAddress}\",\"{d.MACAddress}\",\"{d.Hostname}\",\"{d.Vendor}\",\"{d.LatencyMs}\",\"{d.OpenPortsCount}\",\"{d.ServicesText}\"");
            }
            await File.WriteAllTextAsync(saveDialog.FileName, sb.ToString());
            StatusText = $"Exported CSV to {Path.GetFileName(saveDialog.FileName)}";
        }
    }

    [RelayCommand]
    private async Task ExportExcelAsync()
    {
        if (LastScanResults is null || LastScanResults.Count == 0)
        {
            if (_allMasterDevices.Count > 0)
                await ExportCsvAsync();
            return;
        }

        var saveDialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Excel files (*.xlsx)|*.xlsx",
            FileName = $"Scan_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
        };

        if (saveDialog.ShowDialog() == true)
        {
            await Services.ExcelExportService.ExportScanAsync(LastScanResults, saveDialog.FileName);
            StatusText = $"Exported to {Path.GetFileName(saveDialog.FileName)}";
        }
    }

    [RelayCommand]
    private void QuickPing(ScannerDeviceItem item)
    {
        if (item is null) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k ping {item.IPAddress} -t",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Could not launch ping: {ex.Message}");
        }
    }

    [RelayCommand]
    private void QuickSsh(ScannerDeviceItem item)
    {
        if (item is null) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k ssh {item.IPAddress}",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Could not launch SSH client: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ShowDeviceDetails(ScannerDeviceItem item)
    {
        if (item is null) return;
        if (!string.IsNullOrWhiteSpace(item.IPAddress) && item.IPAddress != "-")
            _nav.NavigateToDeviceDetails(item.IPAddress);
        else if (item.Id > 0)
            _nav.NavigateToDeviceDetails(item.Id);
    }

    [RelayCommand]
    private void DismissSaveDialog() => ShowSaveDialog = false;

    [RelayCommand]
    private void FilterByCard(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return;

        if (SelectedFilterMode.Equals(mode, StringComparison.OrdinalIgnoreCase))
        {
            SelectedFilterMode = "All Devices";
        }
        else
        {
            SelectedFilterMode = mode;
        }
    }

    private static string GetVendorBadgeText(string vendor)
    {
        if (string.IsNullOrWhiteSpace(vendor) || vendor == "Unknown") return "?";
        if (vendor.Contains("Dell", StringComparison.OrdinalIgnoreCase)) return "DELL";
        if (vendor.Contains("HP", StringComparison.OrdinalIgnoreCase) || vendor.Contains("Hewlett", StringComparison.OrdinalIgnoreCase)) return "HP";
        if (vendor.Contains("Apple", StringComparison.OrdinalIgnoreCase)) return "APL";
        if (vendor.Contains("Hikvision", StringComparison.OrdinalIgnoreCase)) return "HIK";
        if (vendor.Contains("Dahua", StringComparison.OrdinalIgnoreCase)) return "DH";
        if (vendor.Contains("Cisco", StringComparison.OrdinalIgnoreCase)) return "CS";
        if (vendor.Contains("Intel", StringComparison.OrdinalIgnoreCase)) return "INT";
        if (vendor.Contains("Huawei", StringComparison.OrdinalIgnoreCase)) return "HW";
        if (vendor.Contains("Xiaomi", StringComparison.OrdinalIgnoreCase)) return "MI";
        if (vendor.Contains("ZTE", StringComparison.OrdinalIgnoreCase)) return "ZTE";
        if (vendor.Contains("TP-Link", StringComparison.OrdinalIgnoreCase)) return "TP";
        if (vendor.Contains("Samsung", StringComparison.OrdinalIgnoreCase)) return "SAM";
        if (vendor.Contains("Lenovo", StringComparison.OrdinalIgnoreCase)) return "LN";
        if (vendor.Contains("VMware", StringComparison.OrdinalIgnoreCase)) return "VM";
        if (vendor.Contains("Espressif", StringComparison.OrdinalIgnoreCase)) return "ESP";
        if (vendor.Contains("Ubiquiti", StringComparison.OrdinalIgnoreCase)) return "UB";
        if (vendor.Contains("MikroTik", StringComparison.OrdinalIgnoreCase)) return "MT";
        if (vendor.Contains("Raspberry", StringComparison.OrdinalIgnoreCase)) return "Pi";
        if (vendor.Contains("Canon", StringComparison.OrdinalIgnoreCase)) return "CAN";
        if (vendor.Contains("Epson", StringComparison.OrdinalIgnoreCase)) return "EPS";
        if (vendor.Contains("Brother", StringComparison.OrdinalIgnoreCase)) return "BRO";
        if (vendor.Contains("D-Link", StringComparison.OrdinalIgnoreCase)) return "DL";
        if (vendor.Contains("Netgear", StringComparison.OrdinalIgnoreCase)) return "NET";
        if (vendor.Contains("Sony", StringComparison.OrdinalIgnoreCase)) return "SNY";
        if (vendor.Contains("Asus", StringComparison.OrdinalIgnoreCase)) return "ASUS";
        if (vendor.Contains("LG", StringComparison.OrdinalIgnoreCase)) return "LG";
        if (vendor.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)) return "MS";
        if (vendor.Contains("Camera", StringComparison.OrdinalIgnoreCase)) return "CAM";
        if (vendor.Contains("Printer", StringComparison.OrdinalIgnoreCase)) return "PRN";
        if (vendor.Contains("Mobile", StringComparison.OrdinalIgnoreCase)) return "MOB";
        return vendor.Length <= 4 ? vendor.ToUpper() : vendor[..3].ToUpper();
    }

    private bool CanStart() => !IsScanning;

    partial void OnIsScanningChanged(bool value) => StartScanCommand.NotifyCanExecuteChanged();
}

public sealed class ScannerDeviceItem
{
    public int Id { get; init; }
    public string Status { get; init; } = "Online";
    public string IPAddress { get; init; } = "";
    public string MACAddress { get; init; } = "";
    public string Hostname { get; init; } = "";
    public string Vendor { get; init; } = "Unknown";
    public string VendorInitial { get; init; } = "?";
    public string LatencyText { get; init; } = "1ms";
    public int LatencyMs { get; init; }
    public int OpenPortsCount { get; init; }
    public string ServicesText { get; init; } = "-";
    public string DeviceType { get; init; } = "Unknown";
    public bool IsServer { get; init; }
    public bool IsSwitch { get; init; }
    public bool IsPrinter { get; init; }
    public Device? RawDevice { get; init; }
}


using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using NetworkDiscoveryTool.Core.Models;
using NetworkDiscoveryTool.Data;
using NetworkDiscoveryTool.Helpers;

namespace NetworkDiscoveryTool.UI.ViewModels;

public sealed partial class DeviceDetailsViewModel : ObservableObject
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly Services.INavigationService _navigation;

    [ObservableProperty] private string _hostname = "-";
    [ObservableProperty] private string _vendor = "-";
    [ObservableProperty] private string _deviceType = "Workstation";
    [ObservableProperty] private string _deviceIcon = "\uE7F8";
    [ObservableProperty] private string _deviceIconBg = "#150EA5E9";
    [ObservableProperty] private string _deviceIconColor = "#0EA5E9";
    [ObservableProperty] private string _status = "Online";
    [ObservableProperty] private string _latency = "-";
    [ObservableProperty] private string _ttlText = "-";
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private string _lastSeen = "-";
    [ObservableProperty] private string _scanDescription = "-";
    [ObservableProperty] private string _httpBanner = "No Web Server detected";
    [ObservableProperty] private string _smbInfo = "-";
    [ObservableProperty] private bool _hasHttp;
    [ObservableProperty] private bool _hasHttps;
    [ObservableProperty] private bool _hasSsh;
    [ObservableProperty] private bool _hasRdp;
    [ObservableProperty] private bool _hasNoPorts;
    [ObservableProperty] private bool _hasNoHistory;
    [ObservableProperty] private bool _isLoading;

    // Tabs
    [ObservableProperty] private bool _isTabOverviewSelected = true;
    [ObservableProperty] private bool _isTabPortsSelected;
    [ObservableProperty] private bool _isTabLogsSelected;

    private string _ip = "";
    public string IP { get => _ip; set => SetProperty(ref _ip, value); }

    private string _mac = "";
    public string MAC { get => _mac; set => SetProperty(ref _mac, value); }

    private string _os = "";
    public string OS { get => _os; set => SetProperty(ref _os, value); }

    public ObservableCollection<PortDisplay> Ports { get; } = [];
    public ObservableCollection<HistoryEntry> History { get; } = [];

    public DeviceDetailsViewModel(
        IDbContextFactory<AppDbContext> contextFactory,
        Services.INavigationService navigation)
    {
        _contextFactory = contextFactory;
        _navigation = navigation;
    }

    public async Task LoadDeviceAsync(int deviceId)
    {
        IsLoading = true;
        try
        {
            await using var ctx = await _contextFactory.CreateDbContextAsync();

            var device = await ctx.Devices
                .AsNoTracking()
                .Include(d => d.Scan)
                .Include(d => d.Ports)
                .FirstOrDefaultAsync(d => d.Id == deviceId);

            if (device is not null)
            {
                PopulateFromDevice(device);
                await LoadHistoryAsync(device.IP);
                _ = ProbeLiveServicesAsync(device.IP);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DeviceDetails] Error: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task LoadDeviceByIpAsync(string ip)
    {
        IsLoading = true;
        IP = ip;
        try
        {
            await using var ctx = await _contextFactory.CreateDbContextAsync();

            var device = await ctx.Devices
                .AsNoTracking()
                .Include(d => d.Scan)
                .Include(d => d.Ports)
                .Where(d => d.IP == ip)
                .OrderByDescending(d => d.Id)
                .FirstOrDefaultAsync();

            if (device is not null)
            {
                PopulateFromDevice(device);
            }
            else
            {
                Hostname = ip;
                MAC = await MacResolver.ResolveAsync(ip);
                Vendor = OuiLookup.Lookup(MAC, ip);
                Status = "Probing...";
            }

            await LoadHistoryAsync(ip);
            await ProbeLiveServicesAsync(ip);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DeviceDetails] Error: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void PopulateFromDevice(Device device)
    {
        IP = device.IP;
        Hostname = !string.IsNullOrWhiteSpace(device.Hostname) ? device.Hostname : "-";
        MAC = !string.IsNullOrWhiteSpace(device.MAC) ? device.MAC : "-";
        Vendor = !string.IsNullOrWhiteSpace(device.Vendor) && device.Vendor != "Unknown" ? device.Vendor : OuiLookup.Lookup(device.MAC, device.Hostname, device.Ports.Select(p => p.PortNumber));
        DeviceType = !string.IsNullOrWhiteSpace(device.DeviceType) ? device.DeviceType : "Workstation";
        Status = device.Status;
        Latency = device.LatencyMs > 0 ? $"{device.LatencyMs} ms" : "< 1 ms";
        OS = !string.IsNullOrWhiteSpace(device.OS) ? device.OS : "-";
        Notes = device.Notes ?? "";
        LastSeen = device.Scan?.Date.ToString("yyyy-MM-dd HH:mm:ss") ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        ScanDescription = device.Scan?.Description ?? "Network Discovery Scan";

        Ports.Clear();
        HasHttp = false; HasHttps = false; HasSsh = false; HasRdp = false;
        foreach (var p in device.Ports.OrderBy(x => x.PortNumber))
        {
            Ports.Add(new PortDisplay { Port = p.PortNumber, State = p.State, Service = p.Service ?? "-" });
            CheckServiceFlags(p.PortNumber);
        }
        HasNoPorts = Ports.Count == 0;

        UpdateDeviceIcon(DeviceType, Hostname, Vendor, device.Ports.Select(p => p.PortNumber));
    }

    private void UpdateDeviceIcon(string deviceType, string hostname, string vendor, IEnumerable<int> openPorts)
    {
        var portsSet = openPorts.ToHashSet();
        string typeLower = (deviceType ?? "").ToLowerInvariant();
        string hostLower = (hostname ?? "").ToLowerInvariant();
        string vendorLower = (vendor ?? "").ToLowerInvariant();

        if (typeLower.Contains("router") || typeLower.Contains("gateway") || IP.EndsWith(".1") || vendorLower.Contains("cisco") || vendorLower.Contains("mikrotik"))
        {
            DeviceIcon = "\uE809"; // Router
            DeviceIconBg = "#150EA5E9";
            DeviceIconColor = "#0EA5E9";
        }
        else if (typeLower.Contains("camera") || portsSet.Contains(554) || portsSet.Contains(8000) || portsSet.Contains(37777) || vendorLower.Contains("hikvision") || vendorLower.Contains("dahua"))
        {
            DeviceIcon = "\uE714"; // Camera
            DeviceIconBg = "#1510B981";
            DeviceIconColor = "#10B981";
        }
        else if (typeLower.Contains("printer") || portsSet.Contains(9100) || portsSet.Contains(515) || portsSet.Contains(631) || vendorLower.Contains("epson") || vendorLower.Contains("canon"))
        {
            DeviceIcon = "\uE749"; // Printer
            DeviceIconBg = "#15F59E0B";
            DeviceIconColor = "#F59E0B";
        }
        else if (typeLower.Contains("server") || hostLower.Contains("srv") || hostLower.Contains("server") || hostLower.Contains("dc") || portsSet.Contains(1433) || portsSet.Contains(3306) || portsSet.Contains(53))
        {
            DeviceIcon = "\uE80F"; // Server
            DeviceIconBg = "#158B5CF6";
            DeviceIconColor = "#A78BFA";
        }
        else if (typeLower.Contains("switch") || vendorLower.Contains("switch"))
        {
            DeviceIcon = "\uE7F4"; // Switch
            DeviceIconBg = "#150EA5E9";
            DeviceIconColor = "#38BDF8";
        }
        else
        {
            DeviceIcon = "\uE7F8"; // Workstation PC
            DeviceIconBg = "#150EA5E9";
            DeviceIconColor = "#0EA5E9";
        }
    }

    private void CheckServiceFlags(int port)
    {
        if (port is 80 or 8080 or 8000) HasHttp = true;
        if (port is 443 or 8443) HasHttps = true;
        if (port is 22) HasSsh = true;
        if (port is 3389) HasRdp = true;
    }

    private async Task ProbeLiveServicesAsync(string ip)
    {
        try
        {
            // 1. Live Ping & TTL
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ip, 1200);
            if (reply.Status == IPStatus.Success)
            {
                Status = "Online";
                Latency = $"{reply.RoundtripTime} ms";
                int ttl = reply.Options?.Ttl ?? 0;
                if (ttl > 0)
                {
                    string osGuess = ttl > 64 && ttl <= 128 ? "Windows (TTL ~128)" : (ttl <= 64 ? "Linux / macOS / Android (TTL ~64)" : "Cisco / Network Appliance (TTL ~255)");
                    TtlText = $"{ttl} ({osGuess})";
                    if (OS == "-" || string.IsNullOrWhiteSpace(OS))
                        OS = ttl > 64 && ttl <= 128 ? "Windows" : (ttl <= 64 ? "Linux / Unix / macOS" : "Embedded Network OS");
                }
            }

            // 2. HTTP Banner Probe
            if (HasHttp || HasHttps)
            {
                try
                {
                    using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
                    using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
                    var scheme = HasHttp ? "http" : "https";
                    var url = $"{scheme}://{ip}";
                    var res = await client.GetAsync(url);
                    var serverHeader = res.Headers.Server.ToString();
                    HttpBanner = !string.IsNullOrWhiteSpace(serverHeader) ? $"Server: {serverHeader} (HTTP {(int)res.StatusCode})" : $"HTTP Response: {(int)res.StatusCode} {res.StatusCode}";
                }
                catch { }
            }
        }
        catch { }
    }

    private async Task LoadHistoryAsync(string ip)
    {
        History.Clear();
        try
        {
            await using var ctx = await _contextFactory.CreateDbContextAsync();
            var logs = await ctx.OperationLogs
                .AsNoTracking()
                .Where(o => o.Description.Contains(ip))
                .OrderByDescending(o => o.Timestamp)
                .Take(20)
                .ToListAsync();

            foreach (var log in logs)
                History.Add(new HistoryEntry
                {
                    Operation = log.OperationName,
                    Description = log.Description,
                    Date = log.Timestamp.ToString("yyyy-MM-dd HH:mm"),
                    Result = log.Result,
                    Duration = $"{log.DurationMs}ms",
                });
        }
        catch { }
        finally
        {
            HasNoHistory = History.Count == 0;
        }
    }

    [RelayCommand]
    private void GoBack() => _navigation.NavigateToNetworkDiscovery();

    [RelayCommand]
    private void PingHost()
    {
        if (string.IsNullOrWhiteSpace(IP)) return;
        try { Process.Start(new ProcessStartInfo { FileName = "cmd.exe", Arguments = $"/k ping {IP} -t", UseShellExecute = true }); } catch { }
    }

    [RelayCommand]
    private void TracerouteHost()
    {
        if (string.IsNullOrWhiteSpace(IP)) return;
        try { Process.Start(new ProcessStartInfo { FileName = "cmd.exe", Arguments = $"/k tracert {IP}", UseShellExecute = true }); } catch { }
    }

    [RelayCommand]
    private void PortAudit() => _navigation.NavigateToPortChecker();

    [RelayCommand]
    private void OpenHttp()
    {
        if (string.IsNullOrWhiteSpace(IP)) return;
        try { Process.Start(new ProcessStartInfo { FileName = $"http://{IP}", UseShellExecute = true }); } catch { }
    }

    [RelayCommand]
    private void OpenHttps()
    {
        if (string.IsNullOrWhiteSpace(IP)) return;
        try { Process.Start(new ProcessStartInfo { FileName = $"https://{IP}", UseShellExecute = true }); } catch { }
    }

    [RelayCommand]
    private void OpenSsh()
    {
        if (string.IsNullOrWhiteSpace(IP)) return;
        try { Process.Start(new ProcessStartInfo { FileName = "cmd.exe", Arguments = $"/k ssh {IP}", UseShellExecute = true }); } catch { }
    }

    [RelayCommand]
    private void OpenRdp()
    {
        if (string.IsNullOrWhiteSpace(IP)) return;
        try { Process.Start(new ProcessStartInfo { FileName = "mstsc.exe", Arguments = $"/v:{IP}", UseShellExecute = true }); } catch { }
    }

    [RelayCommand]
    private void SetTab(string tab)
    {
        IsTabOverviewSelected = tab == "Overview";
        IsTabPortsSelected = tab == "Ports";
        IsTabLogsSelected = tab == "Logs";
    }

    [RelayCommand]
    private async Task RefreshDevice()
    {
        if (string.IsNullOrWhiteSpace(IP)) return;
        IsLoading = true;
        try
        {
            await ProbeLiveServicesAsync(IP);
            await LoadHistoryAsync(IP);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void CopyField(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "-") return;
        try
        {
            System.Windows.Clipboard.SetText(value);
        }
        catch { }
    }

    [RelayCommand]
    private void CopyDiagnostics()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== Device Diagnostics: {IP} ===");
        sb.AppendLine($"Hostname: {Hostname}");
        sb.AppendLine($"MAC: {MAC}");
        sb.AppendLine($"Vendor: {Vendor}");
        sb.AppendLine($"Device Type: {DeviceType}");
        sb.AppendLine($"Status: {Status}");
        sb.AppendLine($"Latency: {Latency}");
        sb.AppendLine($"TTL: {TtlText}");
        sb.AppendLine($"OS: {OS}");
        sb.AppendLine($"Open Ports: {Ports.Count}");
        foreach (var p in Ports)
            sb.AppendLine($"  - Port {p.Port} ({p.Service}) [{p.State}]");

        try
        {
            System.Windows.Clipboard.SetText(sb.ToString());
            System.Windows.MessageBox.Show("All device diagnostics copied to clipboard!", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch { }
    }
}

public sealed class PortDisplay
{
    public int Port { get; init; }
    public string State { get; init; } = "Open";
    public string Service { get; init; } = "";
}

public sealed class HistoryEntry
{
    public string Operation { get; init; } = "";
    public string Description { get; init; } = "";
    public string Date { get; init; } = "";
    public string Result { get; init; } = "";
    public string Duration { get; init; } = "";
}

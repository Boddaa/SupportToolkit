using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using NetworkDiscoveryTool.Data;
using NetworkDiscoveryTool.UI.Services;

namespace NetworkDiscoveryTool.UI.ViewModels;

public sealed partial class PortCheckerViewModel : ObservableObject
{
    private readonly IOperationHistoryService _history;
    private readonly CurrentUserService _currentUser;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly Services.INavigationService _navigation;

    private CancellationTokenSource? _cts;

    // === Configuration Inputs ===
    [ObservableProperty] private string _targetHost = "10.3.6.200";
    [ObservableProperty] private string? _selectedTargetHistoryItem;
    [ObservableProperty] private string _selectedPreset = "Top Common (20 Ports)";
    [ObservableProperty] private string _customPortsInput = "20,21,22,23,25,53,80,110,135,139,143,443,445,993,995,1433,3306,3389,5432,8080";
    [ObservableProperty] private int _timeoutMs = 800;
    [ObservableProperty] private int _maxConcurrency = 32;

    // === Scan Status & Progress ===
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private int _progress;
    [ObservableProperty] private string _statusText = "Ready to scan";
    [ObservableProperty] private string _scanDurationText = "0.0s";

    // === Search & State Filters ===
    [ObservableProperty] private string _searchFilter = "";
    [ObservableProperty] private string _selectedFilterState = "All";
    [ObservableProperty] private bool _isFilterAllSelected = true;
    [ObservableProperty] private bool _isFilterOpenSelected;
    [ObservableProperty] private bool _isFilterClosedSelected;
    [ObservableProperty] private bool _hasNoResults = true;

    // === Real-Time KPI Stats ===
    [ObservableProperty] private int _totalScannedCount;
    [ObservableProperty] private int _openCount;
    [ObservableProperty] private int _closedCount;
    [ObservableProperty] private int _filteredCount;
    [ObservableProperty] private int _avgLatencyMs;

    public ObservableCollection<string> Presets { get; } =
    [
        "Top Common (20 Ports)",
        "Web & API Services (80, 443, 8080, 8443...)",
        "Database Ports (1433, 3306, 5432, 27017...)",
        "Remote Access & Admin (22, 3389, 5900, 21...)",
        "Mail & Directory (25, 110, 143, 993, 389...)",
        "CCTV & Streaming (554, 8000, 37777, 8554...)",
        "All Well-Known (1 - 1024)",
        "Custom Port List"
    ];

    public ObservableCollection<string> TargetHistory { get; } = [];
    public ObservableCollection<string> FilterStateOptions { get; } = ["All", "Open Only", "Closed Only"];

    private readonly List<PortCheckerResultItem> _allResultsMaster = [];
    public ObservableCollection<PortCheckerResultItem> FilteredResults { get; } = [];

    public PortCheckerViewModel(
        IOperationHistoryService history,
        CurrentUserService currentUser,
        IDbContextFactory<AppDbContext> contextFactory,
        Services.INavigationService navigation)
    {
        _history = history;
        _currentUser = currentUser;
        _contextFactory = contextFactory;
        _navigation = navigation;

        _ = LoadTargetsFromNetworkAndDbAsync();
    }

    private async Task LoadTargetsFromNetworkAndDbAsync()
    {
        try
        {
            TargetHistory.Add("127.0.0.1 (Localhost)");

            // Add local adapter IPs
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == OperationalStatus.Up &&
                    ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    var ipProps = ni.GetIPProperties();
                    foreach (var addr in ipProps.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            string localIp = addr.Address.ToString();
                            if (!TargetHistory.Contains(localIp))
                                TargetHistory.Add(localIp);
                            if (TargetHost == "10.3.6.200" || TargetHost == "127.0.0.1")
                                TargetHost = localIp;
                        }
                    }

                    var gw = ipProps.GatewayAddresses.FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork);
                    if (gw != null && !TargetHistory.Contains(gw.Address.ToString()))
                    {
                        TargetHistory.Add($"{gw.Address} (Gateway)");
                    }
                }
            }

            // Load last scanned devices from database
            await using var context = await _contextFactory.CreateDbContextAsync();
            var recentDevices = await context.Devices
                .OrderByDescending(d => d.Id)
                .Take(20)
                .Select(d => d.IP)
                .Distinct()
                .ToListAsync();

            foreach (var ip in recentDevices)
            {
                if (!TargetHistory.Contains(ip))
                    TargetHistory.Add(ip);
            }
        }
        catch { }
    }

    partial void OnSelectedTargetHistoryItemChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            var ip = value.Split(' ')[0];
            TargetHost = ip;
        }
    }

    partial void OnSelectedPresetChanged(string value)
    {
        CustomPortsInput = value switch
        {
            "Top Common (20 Ports)" => "20,21,22,23,25,53,80,110,135,139,143,443,445,993,995,1433,3306,3389,5432,8080",
            "Web & API Services (80, 443, 8080, 8443...)" => "80,443,8000,8080,8443,8888,3000,5000,9000,8081",
            "Database Ports (1433, 3306, 5432, 27017...)" => "1433,1521,3306,5432,27017,6379,9200,11211",
            "Remote Access & Admin (22, 3389, 5900, 21...)" => "21,22,23,3389,5900,5985,5986,2222",
            "Mail & Directory (25, 110, 143, 993, 389...)" => "25,110,143,465,587,993,995,389,636",
            "CCTV & Streaming (554, 8000, 37777, 8554...)" => "554,1935,8000,8554,37777,80,443",
            "All Well-Known (1 - 1024)" => "1-1024",
            _ => CustomPortsInput
        };
    }

    partial void OnSearchFilterChanged(string value) => ApplyFilter();
    partial void OnSelectedFilterStateChanged(string value) => ApplyFilter();

    private List<int> ParsePortList()
    {
        var result = new HashSet<int>();
        var parts = CustomPortsInput.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in parts)
        {
            if (part.Contains('-'))
            {
                var rangeParts = part.Split('-');
                if (rangeParts.Length == 2 &&
                    int.TryParse(rangeParts[0].Trim(), out var start) &&
                    int.TryParse(rangeParts[1].Trim(), out var end) &&
                    start > 0 && end >= start && end <= 65535)
                {
                    int maxRange = Math.Min(end, start + 1024); // cap to 1024 at a time
                    for (int p = start; p <= maxRange; p++)
                        result.Add(p);
                }
            }
            else if (int.TryParse(part, out var port) && port > 0 && port <= 65535)
            {
                result.Add(port);
            }
        }

        return result.OrderBy(p => p).ToList();
    }

    [RelayCommand]
    private async Task StartScanAsync()
    {
        string host = TargetHost?.Trim() ?? "";
        if (host.Contains(' '))
            host = host.Split(' ')[0];

        if (string.IsNullOrWhiteSpace(host))
        {
            System.Windows.MessageBox.Show("Please enter a valid target IP address or hostname.", "Target Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var ports = ParsePortList();
        if (ports.Count == 0)
        {
            System.Windows.MessageBox.Show("Please enter one or more ports to scan (e.g. 80,443,3389).", "Ports Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _cts = new CancellationTokenSource();
        IsScanning = true;
        Progress = 0;
        StatusText = $"Scanning {host} ({ports.Count} ports)...";
        _allResultsMaster.Clear();
        FilteredResults.Clear();
        TotalScannedCount = 0;
        OpenCount = 0;
        ClosedCount = 0;
        FilteredCount = 0;
        AvgLatencyMs = 0;

        var sw = Stopwatch.StartNew();
        int scannedCount = 0;
        long totalLatency = 0;
        int latencySamples = 0;

        var semaphore = new SemaphoreSlim(MaxConcurrency);
        var tasks = new List<Task>();

        foreach (var port in ports)
        {
            if (_cts.IsCancellationRequested) break;

            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync(_cts.Token);
                try
                {
                    if (_cts.IsCancellationRequested) return;

                    var item = await CheckSinglePortAsync(host, port, TimeoutMs, _cts.Token);

                    lock (_allResultsMaster)
                    {
                        _allResultsMaster.Add(item);
                        scannedCount++;
                        if (item.State == "Open")
                        {
                            OpenCount++;
                            if (item.LatencyMs > 0)
                            {
                                totalLatency += item.LatencyMs;
                                latencySamples++;
                            }
                        }
                        else if (item.State == "Closed")
                        {
                            ClosedCount++;
                        }
                        else
                        {
                            FilteredCount++;
                        }

                        TotalScannedCount = scannedCount;
                        Progress = (int)((double)scannedCount / ports.Count * 100);
                        if (latencySamples > 0)
                            AvgLatencyMs = (int)(totalLatency / latencySamples);
                    }

                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        if (MatchesFilter(item))
                            FilteredResults.Add(item);
                    });
                }
                catch { }
                finally
                {
                    semaphore.Release();
                }
            }));
        }

        try
        {
            await Task.WhenAll(tasks);
            sw.Stop();
            ScanDurationText = $"{sw.Elapsed.TotalSeconds:F1}s";
            StatusText = $"Completed - Found {OpenCount} open port(s) on {host} in {ScanDurationText}";

            await _history.LogAsync("Port Checker", $"{host} | {OpenCount}/{ports.Count} Open", "Success", sw.ElapsedMilliseconds, _currentUser.Username);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            StatusText = "Scan cancelled";
        }
        catch (Exception ex)
        {
            sw.Stop();
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            Progress = 100;
            ApplyFilter();
        }
    }

    [RelayCommand]
    private void StopScan()
    {
        _cts?.Cancel();
        IsScanning = false;
        StatusText = "Stopping scan...";
    }

    [RelayCommand]
    private void ClearResults()
    {
        _allResultsMaster.Clear();
        FilteredResults.Clear();
        TotalScannedCount = 0;
        OpenCount = 0;
        ClosedCount = 0;
        FilteredCount = 0;
        AvgLatencyMs = 0;
        Progress = 0;
        StatusText = "Ready to scan";
        ScanDurationText = "0.0s";
    }

    [RelayCommand]
    private void ExportResults()
    {
        try
        {
            if (_allResultsMaster.Count == 0)
            {
                System.Windows.MessageBox.Show("No results to export.", "Empty Results", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV File (*.csv)|*.csv|Text Report (*.txt)|*.txt",
                FileName = $"PortScan_{TargetHost}_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (saveDialog.ShowDialog() == true)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Port,Protocol,Service,State,LatencyMs,Banner");
                foreach (var r in _allResultsMaster)
                {
                    sb.AppendLine($"{r.Port},{r.Protocol},{r.Service},{r.State},{r.LatencyMs},\"{r.Banner.Replace("\"", "\"\"")}\"");
                }

                File.WriteAllText(saveDialog.FileName, sb.ToString());
                System.Windows.MessageBox.Show($"Exported {_allResultsMaster.Count} port results successfully.", "Export Completed", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public void SetFilterState(string state)
    {
        SelectedFilterState = state;
        IsFilterAllSelected = state == "All";
        IsFilterOpenSelected = state == "Open Only";
        IsFilterClosedSelected = state == "Closed Only";
        ApplyFilter();
    }

    public void ApplyFilter()
    {
        FilteredResults.Clear();
        List<PortCheckerResultItem> items;
        lock (_allResultsMaster)
        {
            items = _allResultsMaster.Where(MatchesFilter).OrderBy(r => r.Port).ToList();
        }

        foreach (var item in items)
            FilteredResults.Add(item);

        HasNoResults = FilteredResults.Count == 0;
    }

    private bool MatchesFilter(PortCheckerResultItem item)
    {
        if (SelectedFilterState == "Open Only")
        {
            if (item.State != "Open") return false;
        }
        else if (SelectedFilterState == "Closed Only")
        {
            if (item.State == "Open") return false;
        }

        if (!string.IsNullOrWhiteSpace(SearchFilter))
        {
            var s = SearchFilter.Trim().ToLowerInvariant();
            return item.Port.ToString().Contains(s) ||
                   item.Service.ToLowerInvariant().Contains(s) ||
                   item.Banner.ToLowerInvariant().Contains(s) ||
                   item.State.ToLowerInvariant().Contains(s);
        }

        return true;
    }

    private static async Task<PortCheckerResultItem> CheckSinglePortAsync(string host, int port, int timeoutMs, CancellationToken ct)
    {
        string serviceName = GetWellKnownServiceName(port);
        var sw = Stopwatch.StartNew();

        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            var completedTask = await Task.WhenAny(connectTask, Task.Delay(timeoutMs, ct));

            if (completedTask == connectTask && client.Connected)
            {
                sw.Stop();
                long latency = sw.ElapsedMilliseconds;
                string banner = await TryGrabBannerAsync(client, port);

                return new PortCheckerResultItem
                {
                    Port = port,
                    Protocol = "TCP",
                    Service = serviceName,
                    State = "Open",
                    LatencyMs = latency,
                    LatencyText = $"{latency}ms",
                    Banner = !string.IsNullOrWhiteSpace(banner) ? banner : "-",
                    StateColor = "#10B981", // Success Green
                    StateBg = "#1510B981"
                };
            }
            else
            {
                sw.Stop();
                return new PortCheckerResultItem
                {
                    Port = port,
                    Protocol = "TCP",
                    Service = serviceName,
                    State = "Filtered",
                    LatencyMs = sw.ElapsedMilliseconds,
                    LatencyText = "Timeout",
                    Banner = "-",
                    StateColor = "#F59E0B", // Amber
                    StateBg = "#15F59E0B"
                };
            }
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
        {
            sw.Stop();
            return new PortCheckerResultItem
            {
                Port = port,
                Protocol = "TCP",
                Service = serviceName,
                State = "Closed",
                LatencyMs = sw.ElapsedMilliseconds,
                LatencyText = $"{sw.ElapsedMilliseconds}ms",
                Banner = "-",
                StateColor = "#EF4444", // Red
                StateBg = "#15EF4444"
            };
        }
        catch
        {
            sw.Stop();
            return new PortCheckerResultItem
            {
                Port = port,
                Protocol = "TCP",
                Service = serviceName,
                State = "Closed",
                LatencyMs = sw.ElapsedMilliseconds,
                LatencyText = "-",
                Banner = "-",
                StateColor = "#EF4444",
                StateBg = "#15EF4444"
            };
        }
    }

    private static async Task<string> TryGrabBannerAsync(TcpClient client, int port)
    {
        try
        {
            client.ReceiveTimeout = 400;
            using var stream = client.GetStream();

            if (port is 80 or 8080 or 8000 or 3000 or 5000)
            {
                byte[] request = Encoding.ASCII.GetBytes($"HEAD / HTTP/1.1\r\nHost: {client.Client.RemoteEndPoint}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(request);
            }
            else if (port == 21) // FTP
            {
                // FTP sends welcome banner immediately
            }

            byte[] buffer = new byte[256];
            var readTask = stream.ReadAsync(buffer, 0, buffer.Length);
            if (await Task.WhenAny(readTask, Task.Delay(300)) == readTask)
            {
                int bytesRead = await readTask;
                if (bytesRead > 0)
                {
                    string raw = Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim();
                    var firstLine = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    return firstLine ?? raw;
                }
            }
        }
        catch { }

        return "-";
    }

    private static string GetWellKnownServiceName(int port) => port switch
    {
        20 => "FTP-Data",
        21 => "FTP Control",
        22 => "SSH Remote Shell",
        23 => "Telnet",
        25 => "SMTP Mail",
        53 => "DNS Domain Name System",
        80 => "HTTP Web Server",
        110 => "POP3 Mail",
        135 => "RPC Endpoint Mapper",
        139 => "NetBIOS Session",
        143 => "IMAP Mail",
        443 => "HTTPS Secure Web",
        445 => "SMB File Sharing",
        465 => "SMTPS Secure Mail",
        515 => "LPD Line Printer",
        554 => "RTSP Video Stream",
        587 => "SMTP Submission",
        631 => "IPP Internet Printing",
        636 => "LDAPS Secure Directory",
        993 => "IMAPS Secure Mail",
        995 => "POP3S Secure Mail",
        1433 => "MSSQL Database Server",
        1521 => "Oracle Database",
        1935 => "RTMP Flash Streaming",
        3000 => "Node.js / React Dev Server",
        3306 => "MySQL Database",
        3389 => "RDP Remote Desktop",
        5000 => "ASP.NET / Flask Web API",
        5432 => "PostgreSQL Database",
        5900 => "VNC Remote Desktop",
        5985 => "WinRM HTTP",
        5986 => "WinRM HTTPS",
        6379 => "Redis In-Memory DB",
        8000 => "HTTP Alternate / CCTV",
        8080 => "HTTP Proxy / Web Alternate",
        8443 => "HTTPS Web Alternate",
        8554 => "RTSP Proxy",
        8888 => "Jupyter / HTTP Alt",
        9000 => "SonarQube / Web Console",
        9100 => "RAW JetDirect Printer",
        9200 => "Elasticsearch REST API",
        27017 => "MongoDB Database",
        37777 => "Dahua DVR Control",
        _ => $"Port {port}"
    };
}

public sealed class PortCheckerResultItem
{
    public int Port { get; init; }
    public string Protocol { get; init; } = "TCP";
    public string Service { get; init; } = "";
    public string State { get; init; } = "Closed";
    public long LatencyMs { get; init; }
    public string LatencyText { get; init; } = "-";
    public string Banner { get; init; } = "-";
    public string StateColor { get; init; } = "#10B981";
    public string StateBg { get; init; } = "#1510B981";
}

using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetworkDiscoveryTool.Core.Interfaces;
using NetworkDiscoveryTool.Core.Models;
using NetworkDiscoveryTool.Data;
using NetworkDiscoveryTool.Helpers;

namespace NetworkDiscoveryTool.Services;

public sealed class NetworkScannerService : INetworkScanner
{
    private static readonly int[] CommonPorts =
    [
        21, 22, 23, 25, 53, 80, 110, 135, 139, 143, 389, 443, 445, 554, 631, 9100, 3389, 8080
    ];

    private static readonly Dictionary<int, string> ServiceMap = new()
    {
        { 21, "FTP" }, { 22, "SSH" }, { 23, "Telnet" }, { 25, "SMTP" },
        { 53, "DNS" }, { 80, "HTTP" }, { 110, "POP3" }, { 135, "RPC" },
        { 139, "NetBIOS" }, { 143, "IMAP" }, { 389, "LDAP" }, { 443, "HTTPS" },
        { 445, "SMB" }, { 554, "RTSP" }, { 631, "IPP" }, { 9100, "RawPrint" },
        { 3389, "RDP" }, { 8080, "HTTP-Alt" },
    };

    private static readonly Dictionary<int, string> PortDeviceMap = new()
    {
        { 21, "FTP Server" }, { 22, "SSH Server" }, { 23, "Telnet" },
        { 25, "SMTP Server" }, { 53, "DNS Server" }, { 80, "Web Server" },
        { 110, "Mail Server" }, { 135, "Windows RPC" }, { 139, "NetBIOS" },
        { 143, "Mail Server" }, { 389, "LDAP Server" }, { 443, "Web Server" },
        { 445, "File Share" }, { 554, "RTSP" }, { 631, "IPP" }, { 9100, "Raw Print" },
        { 3389, "Remote Desktop" }, { 8080, "Web Proxy" },
    };

    private static readonly HashSet<int> RouterPorts = [22, 23, 80, 443, 161];
    private static readonly HashSet<int> WindowsPorts = [135, 139, 445, 3389];

    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<NetworkScannerService> _logger;
    private readonly IDeviceClassifier _classifier;

    public NetworkScannerService(
        IDbContextFactory<AppDbContext> contextFactory,
        IDeviceClassifier? classifier = null,
        ILogger<NetworkScannerService>? logger = null)
    {
        _contextFactory = contextFactory;
        _classifier = classifier ?? new DeviceClassifier();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<NetworkScannerService>.Instance;
    }

    public static uint IpToUInt(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return BitConverter.ToUInt32(bytes, 0);
    }

    public static IPAddress UIntToIp(uint val)
    {
        var bytes = BitConverter.GetBytes(val);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return new IPAddress(bytes);
    }

    public static List<string> ParseIpTargets(string startInput, string endInput)
    {
        var targets = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(startInput)) return targets;

        // Split inputs by comma or semicolon or newline
        var startParts = startInput.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var endParts = !string.IsNullOrWhiteSpace(endInput)
            ? endInput.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : Array.Empty<string>();

        int count = Math.Max(startParts.Length, endParts.Length);

        for (int p = 0; p < count; p++)
        {
            string sStr = p < startParts.Length ? startParts[p] : startParts.Last();
            string eStr = p < endParts.Length ? endParts[p] : (p < startParts.Length ? startParts[p] : (endParts.Length > 0 ? endParts.Last() : sStr));

            // Check if string contains dash (e.g. "192.168.1.1-192.168.1.254")
            if (sStr.Contains('-'))
            {
                var dashParts = sStr.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (dashParts.Length >= 2)
                {
                    sStr = dashParts[0];
                    eStr = dashParts[1];
                }
            }

            // Check if string is CIDR (e.g. "192.168.1.0/24")
            if (sStr.Contains('/'))
            {
                var cidrParts = sStr.Split('/');
                if (IPAddress.TryParse(cidrParts[0], out var baseIp) && int.TryParse(cidrParts[1], out int prefixLength) && prefixLength >= 16 && prefixLength <= 32)
                {
                    uint baseUInt = IpToUInt(baseIp);
                    uint mask = prefixLength == 0 ? 0 : uint.MaxValue << (32 - prefixLength);
                    uint netStart = (baseUInt & mask) + 1; // start from .1
                    uint netEnd = (netStart | ~mask) - 1;  // end at .254

                    if (prefixLength >= 31)
                    {
                        netStart = baseUInt & mask;
                        netEnd = netStart | ~mask;
                    }

                    for (uint cur = netStart; cur <= netEnd; cur++)
                    {
                        var ipStr = UIntToIp(cur).ToString();
                        if (seen.Add(ipStr)) targets.Add(ipStr);
                        if (targets.Count >= 65536) break;
                    }
                    continue;
                }
            }

            if (IPAddress.TryParse(sStr, out var sIp) && IPAddress.TryParse(eStr, out var eIp))
            {
                uint sUint = IpToUInt(sIp);
                uint eUint = IpToUInt(eIp);
                if (sUint > eUint) (sUint, eUint) = (eUint, sUint);

                for (uint cur = sUint; cur <= eUint; cur++)
                {
                    var ipStr = UIntToIp(cur).ToString();
                    if (seen.Add(ipStr)) targets.Add(ipStr);
                    if (targets.Count >= 65536) break;
                }
            }
            else if (IPAddress.TryParse(sStr, out var singleIp))
            {
                var ipStr = singleIp.ToString();
                if (seen.Add(ipStr)) targets.Add(ipStr);
            }
        }

        return targets;
    }

    public async Task<List<Device>> ScanRangeAsync(
        string startIP, string endIP,
        int maxThreads = 100,
        int pingTimeoutMs = 2000,
        int portTimeoutMs = 500,
        string? description = null,
        string? notes = null,
        int? userId = null,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var ipTargets = ParseIpTargets(startIP, endIP);
        int total = Math.Max(1, ipTargets.Count);

        var results = new List<Device>();
        var lockObj = new object();
        var throttler = new SemaphoreSlim(maxThreads);
        var tasks = new List<Task>();
        int scanned = 0;

        _logger.LogInformation("Starting scan: {Count} IP targets from {StartIP} - {EndIP}, Threads={MaxThreads}", total, startIP, endIP, maxThreads);

        foreach (var ip in ipTargets)
        {
            if (ct.IsCancellationRequested) break;

            await throttler.WaitAsync(ct);

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var device = await ScanDeviceAsync(ip, pingTimeoutMs, portTimeoutMs, ct);
                    if (device is not null) { lock (lockObj) results.Add(device); }
                    var current = Interlocked.Increment(ref scanned);
                    progress?.Report((int)((double)current / total * 100));
                }
                finally { throttler.Release(); }
            }, ct));
        }

        await Task.WhenAll(tasks);
        sw.Stop();

        await SaveScanAsync(results, startIP, endIP, sw.ElapsedMilliseconds, description, notes, userId, ct);

        _logger.LogInformation("Scan complete: {Count} devices found across {Total} targets in {Duration}ms", results.Count, total, sw.ElapsedMilliseconds);

        return results;
    }

    private async Task SaveScanAsync(List<Device> devices, string startIP, string endIP,
        long durationMs, string? description, string? notes, int? userId, CancellationToken ct)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var scan = new Scan
        {
            Date = DateTime.UtcNow,
            StartIP = startIP,
            EndIP = endIP,
            DurationMs = durationMs,
            Description = description,
            Notes = notes,
            UserId = userId,
            Devices = devices,
        };

        context.Scans.Add(scan);
        await context.SaveChangesAsync(ct);

        _logger.LogInformation("Scan saved to database: ID={ScanId}, Devices={Count}", scan.Id, devices.Count);
    }

    private async Task<Device?> ScanDeviceAsync(string ip, int pingTimeoutMs, int portTimeoutMs, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return null;

        var (isAlive, latency) = await PingHostAsync(ip, pingTimeoutMs);
        var status = isAlive ? "Online" : "Offline";

        if (!isAlive)
        {
            return new Device
            {
                IP = ip, Status = status, LatencyMs = latency,
                Hostname = ip, DeviceType = "Offline",
            };
        }

        // 1. Resolve MAC address via SendARP & ARP cache
        var macTask = MacResolver.ResolveAsync(ip);

        // 2. Resolve Hostname via NetBIOS & DNS/mDNS
        var hostnameTask = HostnameResolver.ResolveHostnameAsync(ip, Math.Min(pingTimeoutMs, 1000));

        // 3. Scan Ports
        var portsTask = ScanPortsAsync(ip, portTimeoutMs, ct);

        await Task.WhenAll(macTask, hostnameTask, portsTask);

        var mac = await macTask;
        var hostname = await hostnameTask;
        var ports = await portsTask;
        var openPorts = ports.Where(p => p.State == "Open").Select(p => p.PortNumber).ToList();

        // 4. Grab HTTP banner if web ports are open
        string? httpBanner = null;
        if (openPorts.Any(p => p is 80 or 8080 or 8000 or 8081 or 443 or 8443))
        {
            httpBanner = await HostnameResolver.GrabHttpBannerAsync(ip, openPorts, 600);
        }

        // If hostname was not resolved, but HTTP title gives a device name (e.g. WV-S1536L Network Camera)
        if (hostname == ip && !string.IsNullOrWhiteSpace(httpBanner))
        {
            // Extract the first clean segment
            var cleanTitle = httpBanner.Split('(')[0].Trim();
            if (!string.IsNullOrWhiteSpace(cleanTitle) && cleanTitle.Length > 2 && cleanTitle.Length < 40)
            {
                hostname = cleanTitle;
            }
        }

        var vendor = OuiLookup.Lookup(mac, hostname, openPorts);
        var deviceType = _classifier.Classify(ports, vendor, hostname, httpBanner, ip);

        return new Device
        {
            IP = ip, Status = status, LatencyMs = latency,
            Hostname = hostname, MAC = mac, Vendor = vendor,
            DeviceType = deviceType,
            Ports = ports,
        };
    }

    private static async Task<(bool Alive, long LatencyMs)> PingHostAsync(string ip, int timeout)
    {
        try
        {
            using var ping = new Ping();
            var sw = Stopwatch.StartNew();
            var reply = await ping.SendPingAsync(ip, timeout);
            sw.Stop();
            return (reply.Status == IPStatus.Success, sw.ElapsedMilliseconds);
        }
        catch { return (false, 0); }
    }

    private static async Task<List<Port>> ScanPortsAsync(string ip, int timeoutMs, CancellationToken ct)
    {
        var scanTasks = CommonPorts.Select(async port =>
        {
            if (ct.IsCancellationRequested)
            {
                return new Port
                {
                    PortNumber = port,
                    State = "Closed",
                    Service = ServiceMap.GetValueOrDefault(port),
                };
            }

            try
            {
                using var client = new TcpClient();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeoutMs);

                await client.ConnectAsync(ip, port, cts.Token);
                bool open = client.Connected;

                return new Port
                {
                    PortNumber = port,
                    State = open ? "Open" : "Closed",
                    Service = ServiceMap.GetValueOrDefault(port),
                };
            }
            catch
            {
                return new Port
                {
                    PortNumber = port,
                    State = "Closed",
                    Service = ServiceMap.GetValueOrDefault(port),
                };
            }
        });

        var results = await Task.WhenAll(scanTasks);
        return results.OrderBy(p => p.PortNumber).ToList();
    }
}

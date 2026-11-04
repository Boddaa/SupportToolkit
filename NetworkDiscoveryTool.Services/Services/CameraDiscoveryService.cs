using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using NetworkDiscoveryTool.Core.Interfaces;
using NetworkDiscoveryTool.Core.Models;
using NetworkDiscoveryTool.Helpers;

namespace NetworkDiscoveryTool.Services.Services;

public sealed class CameraDiscoveryService : ICameraDiscoveryService
{
    private readonly ILogger<CameraDiscoveryService> _logger;

    public CameraDiscoveryService(ILogger<CameraDiscoveryService>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<CameraDiscoveryService>.Instance;
    }

    public async Task<List<DiscoveredCamera>> DiscoverCamerasAsync(
        string? localInterfaceIp = null,
        IProgress<string>? statusProgress = null,
        CancellationToken ct = default)
    {
        var cameraDict = new ConcurrentDictionary<string, DiscoveredCamera>(StringComparer.OrdinalIgnoreCase);

        // 1. Identify local IPs & Subnets (Support multi-homed / multi-IP environments)
        var localSubnets = GetAllLocalSubnets(localInterfaceIp);
        var targetIps = GetActiveLocalIps(localInterfaceIp);

        statusProgress?.Report($"Binding discovery engines to {targetIps.Count} local network endpoint(s)...");

        // 2. Launch All Discovery Engines concurrently
        statusProgress?.Report("Broadcasting ONVIF, Dahua, i-PRO & SADP discovery probes...");

        var tasks = new List<Task>
        {
            Task.Run(() => RunOnvifDiscoveryAsync(cameraDict, targetIps, ct), ct),
            Task.Run(() => RunDahuaDiscoveryAsync(cameraDict, targetIps, ct), ct),
            Task.Run(() => RunSsdpDiscoveryAsync(cameraDict, targetIps, ct), ct),
            Task.Run(() => RunHikvisionSadpDiscoveryAsync(cameraDict, targetIps, ct), ct),
            Task.Run(() => ScanArpCacheForCamerasAsync(cameraDict, ct), ct)
        };

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // Cancelled
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "One or more camera discovery probes encountered an issue");
        }

        // 3. Post-Processing & Resolution
        statusProgress?.Report("Resolving MAC addresses, OUI brands & RTSP streams...");

        var results = cameraDict.Values.ToList();

        var enrichTasks = results.Select(async cam =>
        {
            // Resolve MAC if missing
            if (string.IsNullOrWhiteSpace(cam.MACAddress) || cam.MACAddress == "00:00:00:00:00:00")
            {
                var resolvedMac = await MacResolver.ResolveAsync(cam.IPAddress);
                if (!string.IsNullOrWhiteSpace(resolvedMac))
                {
                    cam.MACAddress = resolvedMac;
                }
            }

            // OUI Brand Enrichment
            if (!string.IsNullOrWhiteSpace(cam.MACAddress))
            {
                var vendor = CameraOuiDatabase.Lookup(cam.MACAddress);
                if (vendor != null)
                {
                    if (cam.Brand == "Unknown" || cam.Brand == "Generic ONVIF")
                    {
                        cam.Brand = vendor.Brand;
                    }
                    if (string.IsNullOrWhiteSpace(cam.ExtraInfo) || cam.ExtraInfo.Contains("Device"))
                    {
                        cam.ExtraInfo = $"{vendor.CompanyName} ({vendor.Category})";
                    }
                }
            }

            // If Brand is still unknown, try text heuristic on model
            if (cam.Brand == "Unknown" || cam.Brand == "Generic ONVIF")
            {
                cam.Brand = CameraOuiDatabase.DetectBrandFromText($"{cam.Model} {cam.ExtraInfo}");
            }

            // Clean model name if generic
            if (cam.Model.EndsWith("Device") && !string.IsNullOrWhiteSpace(cam.ExtraInfo))
            {
                cam.Model = cam.ExtraInfo;
            }

            // Subnet mismatch check against ALL local machine subnets
            if (IPAddress.TryParse(cam.IPAddress, out var camIp))
            {
                bool inSubnet = localSubnets.Count == 0 || IsInAnyLocalSubnet(camIp, localSubnets);
                cam.IsSubnetMismatch = !inSubnet;
                if (!inSubnet)
                {
                    cam.Status = "Subnet Mismatch";
                    cam.SubnetMismatchDetails = $"Camera {cam.IPAddress} is in a different subnet than host";
                }
                else
                {
                    cam.Status = "Online";
                }
            }

            // Web & RTSP Links
            cam.WebUrl = $"http://{cam.IPAddress}:{cam.HttpPort}/";
            cam.RtspUrl = BuildRtspUrl(cam);
        });

        await Task.WhenAll(enrichTasks);

        statusProgress?.Report($"Discovery finished. Discovered {results.Count} camera(s).");
        return results.OrderBy(c => c.Brand).ThenBy(c => c.IPAddress).ToList();
    }

    // =========================================================================
    // ENGINE 1: ONVIF WS-DISCOVERY (Multicast 239.255.255.250:3702)
    // =========================================================================
    private async Task RunOnvifDiscoveryAsync(
        ConcurrentDictionary<string, DiscoveredCamera> dict,
        List<IPAddress> localIps,
        CancellationToken ct)
    {
        const string MulticastAddress = "239.255.255.250";
        const int Port = 3702;

        var messageId = Guid.NewGuid().ToString();
        var probeXml = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <e:Envelope xmlns:e="http://www.w3.org/2003/05/soap-envelope"
                        xmlns:w="http://schemas.xmlsoap.org/ws/2004/08/addressing"
                        xmlns:d="http://schemas.xmlsoap.org/ws/2005/04/discovery"
                        xmlns:dn="http://www.onvif.org/ver10/network/wsdl">
              <e:Header>
                <w:MessageID>uuid:{messageId}</w:MessageID>
                <w:To e:mustUnderstand="true">urn:schemas-xmlsoap-org:ws:2005:04:discovery</w:To>
                <w:Action e:mustUnderstand="true">http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</w:Action>
              </e:Header>
              <e:Body>
                <d:Probe>
                  <d:Types>dn:NetworkVideoTransmitter</d:Types>
                </d:Probe>
              </e:Body>
            </e:Envelope>
            """;

        byte[] requestBytes = Encoding.UTF8.GetBytes(probeXml);
        var clients = new List<UdpClient>();

        try
        {
            var endpoint = new IPEndPoint(IPAddress.Parse(MulticastAddress), Port);

            foreach (var ip in localIps)
            {
                try
                {
                    var client = new UdpClient();
                    client.EnableBroadcast = true;
                    client.Client.Bind(new IPEndPoint(ip, 0));
                    clients.Add(client);
                    await client.SendAsync(requestBytes, requestBytes.Length, endpoint);
                }
                catch { }
            }

            var stopWatch = System.Diagnostics.Stopwatch.StartNew();
            while (stopWatch.ElapsedMilliseconds < 2800 && !ct.IsCancellationRequested)
            {
                bool anyAvailable = false;
                foreach (var client in clients)
                {
                    if (client.Available > 0)
                    {
                        anyAvailable = true;
                        try
                        {
                            var result = await client.ReceiveAsync(ct);
                            var response = Encoding.UTF8.GetString(result.Buffer);
                            ParseOnvifResponse(response, result.RemoteEndPoint.Address.ToString(), dict);
                        }
                        catch { }
                    }
                }

                if (!anyAvailable)
                {
                    await Task.Delay(40, ct);
                }
            }
        }
        finally
        {
            foreach (var c in clients)
            {
                try { c.Dispose(); } catch { }
            }
        }
    }

    private static void ParseOnvifResponse(string xml, string senderIp, ConcurrentDictionary<string, DiscoveredCamera> dict)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            XNamespace d = "http://schemas.xmlsoap.org/ws/2005/04/discovery";

            var probeMatches = doc.Descendants(d + "ProbeMatch");
            foreach (var match in probeMatches)
            {
                var xaddrs = match.Element(d + "XAddrs")?.Value ?? string.Empty;
                var scopes = match.Element(d + "Scopes")?.Value ?? string.Empty;

                var ip = senderIp;
                int port = 80;

                var urlMatch = Regex.Match(xaddrs, @"https?://(?<host>[^:/]+)(?::(?<port>\d+))?", RegexOptions.IgnoreCase);
                if (urlMatch.Success)
                {
                    ip = urlMatch.Groups["host"].Value;
                    if (int.TryParse(urlMatch.Groups["port"].Value, out int parsedPort))
                        port = parsedPort;
                }

                string model = "ONVIF Camera";
                string brand = "Generic ONVIF";
                string extra = string.Empty;

                var scopeParts = scopes.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries);
                foreach (var s in scopeParts)
                {
                    if (s.Contains("hardware/", StringComparison.OrdinalIgnoreCase))
                    {
                        model = Uri.UnescapeDataString(s.Substring(s.IndexOf("hardware/", StringComparison.OrdinalIgnoreCase) + 9));
                    }
                    else if (s.Contains("name/", StringComparison.OrdinalIgnoreCase))
                    {
                        var name = Uri.UnescapeDataString(s.Substring(s.IndexOf("name/", StringComparison.OrdinalIgnoreCase) + 5));
                        if (name.Length > 2) model = name;
                    }
                    else if (s.Contains("location/", StringComparison.OrdinalIgnoreCase))
                    {
                        extra = Uri.UnescapeDataString(s.Substring(s.IndexOf("location/", StringComparison.OrdinalIgnoreCase) + 9));
                    }
                }

                brand = CameraOuiDatabase.DetectBrandFromText($"{model} {scopes}");

                dict.AddOrUpdate(ip,
                    _ => new DiscoveredCamera
                    {
                        IPAddress = ip,
                        HttpPort = port,
                        OnvifPort = port,
                        Brand = brand,
                        Model = model,
                        Protocol = "ONVIF (WS-Discovery)",
                        ExtraInfo = extra
                    },
                    (_, existing) =>
                    {
                        if (existing.Brand == "Unknown" || existing.Brand == "Generic ONVIF")
                            existing.Brand = brand;
                        if (existing.Model.Contains("Device") || existing.Model == "IP Camera")
                            existing.Model = model;
                        existing.OnvifPort = port;
                        return existing;
                    });
            }
        }
        catch { }
    }

    // =========================================================================
    // ENGINE 2: DAHUA DISCOVERY PROTOCOL (Broadcast UDP 37810 / 38888)
    // =========================================================================
    private async Task RunDahuaDiscoveryAsync(
        ConcurrentDictionary<string, DiscoveredCamera> dict,
        List<IPAddress> localIps,
        CancellationToken ct)
    {
        const int DahuaPort = 37810;
        var query = "{\"id\":1,\"method\":\"client.search\",\"params\":{\"mac\":\"\"}}\r\n";
        byte[] queryBytes = Encoding.UTF8.GetBytes(query);
        var clients = new List<UdpClient>();

        try
        {
            var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, DahuaPort);
            var legacyEndpoint = new IPEndPoint(IPAddress.Broadcast, 38888);

            foreach (var ip in localIps)
            {
                try
                {
                    var client = new UdpClient();
                    client.EnableBroadcast = true;
                    client.Client.Bind(new IPEndPoint(ip, 0));
                    clients.Add(client);
                    await client.SendAsync(queryBytes, queryBytes.Length, broadcastEndpoint);
                    await client.SendAsync(queryBytes, queryBytes.Length, legacyEndpoint);
                }
                catch { }
            }

            var stopWatch = System.Diagnostics.Stopwatch.StartNew();
            while (stopWatch.ElapsedMilliseconds < 2500 && !ct.IsCancellationRequested)
            {
                bool anyAvailable = false;
                foreach (var client in clients)
                {
                    if (client.Available > 0)
                    {
                        anyAvailable = true;
                        try
                        {
                            var result = await client.ReceiveAsync(ct);
                            var json = Encoding.UTF8.GetString(result.Buffer);
                            ParseDahuaResponse(json, result.RemoteEndPoint.Address.ToString(), dict);
                        }
                        catch { }
                    }
                }

                if (!anyAvailable)
                {
                    await Task.Delay(40, ct);
                }
            }
        }
        finally
        {
            foreach (var c in clients)
            {
                try { c.Dispose(); } catch { }
            }
        }
    }

    private static void ParseDahuaResponse(string json, string senderIp, ConcurrentDictionary<string, DiscoveredCamera> dict)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("params", out var paramsObj))
                return;

            string ip = senderIp;
            if (paramsObj.TryGetProperty("IPv4Address", out var ipv4) && ipv4.TryGetProperty("ip", out var ipVal))
                ip = ipVal.GetString() ?? senderIp;

            string? mac = null;
            if (paramsObj.TryGetProperty("mac", out var macVal))
                mac = macVal.GetString();

            string deviceType = "Dahua Camera";
            if (paramsObj.TryGetProperty("deviceType", out var devVal))
                deviceType = devVal.GetString() ?? "Dahua Camera";

            string? serialNo = null;
            if (paramsObj.TryGetProperty("serialNo", out var snVal))
                serialNo = snVal.GetString();

            string? version = null;
            if (paramsObj.TryGetProperty("version", out var verVal))
                version = verVal.GetString();

            int port = 80;
            if (paramsObj.TryGetProperty("port", out var portVal) && portVal.TryGetInt32(out int p))
                port = p;

            dict.AddOrUpdate(ip,
                _ => new DiscoveredCamera
                {
                    IPAddress = ip,
                    MACAddress = FormatMac(mac),
                    Brand = "Dahua",
                    Model = deviceType,
                    SerialNumber = serialNo,
                    FirmwareVersion = version,
                    HttpPort = port,
                    Protocol = "Dahua-Discovery",
                    ExtraInfo = $"SN: {serialNo}"
                },
                (_, existing) =>
                {
                    existing.Brand = "Dahua";
                    existing.Model = deviceType;
                    if (!string.IsNullOrWhiteSpace(mac)) existing.MACAddress = FormatMac(mac);
                    if (!string.IsNullOrWhiteSpace(serialNo)) existing.SerialNumber = serialNo;
                    if (!string.IsNullOrWhiteSpace(version)) existing.FirmwareVersion = version;
                    return existing;
                });
        }
        catch { }
    }

    // =========================================================================
    // ENGINE 3: SSDP / UPNP (Multicast 239.255.255.250:1900) - For i-PRO & UPnP
    // =========================================================================
    private async Task RunSsdpDiscoveryAsync(
        ConcurrentDictionary<string, DiscoveredCamera> dict,
        List<IPAddress> localIps,
        CancellationToken ct)
    {
        const string MulticastAddress = "239.255.255.250";
        const int Port = 1900;

        var ssdpQuery = "M-SEARCH * HTTP/1.1\r\n" +
                        "HOST: 239.255.255.250:1900\r\n" +
                        "MAN: \"ssdp:discover\"\r\n" +
                        "MX: 2\r\n" +
                        "ST: ssdp:all\r\n\r\n";

        byte[] requestBytes = Encoding.UTF8.GetBytes(ssdpQuery);
        var clients = new List<UdpClient>();

        try
        {
            var endpoint = new IPEndPoint(IPAddress.Parse(MulticastAddress), Port);

            foreach (var ip in localIps)
            {
                try
                {
                    var client = new UdpClient();
                    client.EnableBroadcast = true;
                    client.Client.Bind(new IPEndPoint(ip, 0));
                    clients.Add(client);
                    await client.SendAsync(requestBytes, requestBytes.Length, endpoint);
                }
                catch { }
            }

            var stopWatch = System.Diagnostics.Stopwatch.StartNew();
            while (stopWatch.ElapsedMilliseconds < 2500 && !ct.IsCancellationRequested)
            {
                bool anyAvailable = false;
                foreach (var client in clients)
                {
                    if (client.Available > 0)
                    {
                        anyAvailable = true;
                        try
                        {
                            var result = await client.ReceiveAsync(ct);
                            var response = Encoding.UTF8.GetString(result.Buffer);
                            ParseSsdpResponse(response, result.RemoteEndPoint.Address.ToString(), dict);
                        }
                        catch { }
                    }
                }

                if (!anyAvailable)
                {
                    await Task.Delay(40, ct);
                }
            }
        }
        finally
        {
            foreach (var c in clients)
            {
                try { c.Dispose(); } catch { }
            }
        }
    }

    private static void ParseSsdpResponse(string text, string senderIp, ConcurrentDictionary<string, DiscoveredCamera> dict)
    {
        try
        {
            var lower = text.ToLowerInvariant();
            if (!lower.Contains("camera") && !lower.Contains("ipro") && !lower.Contains("panasonic") &&
                !lower.Contains("dahua") && !lower.Contains("axis") && !lower.Contains("onvif") &&
                !lower.Contains("cctv") && !lower.Contains("nvr"))
            {
                return;
            }

            string ip = senderIp;
            int port = 80;

            var locMatch = Regex.Match(text, @"Location:\s*https?://(?<host>[^:/]+)(?::(?<port>\d+))?", RegexOptions.IgnoreCase);
            if (locMatch.Success)
            {
                ip = locMatch.Groups["host"].Value;
                if (int.TryParse(locMatch.Groups["port"].Value, out int p)) port = p;
            }

            string brand = CameraOuiDatabase.DetectBrandFromText(text);
            string model = "IP Camera";

            var serverMatch = Regex.Match(text, @"Server:\s*(?<srv>[^\r\n]+)", RegexOptions.IgnoreCase);
            if (serverMatch.Success)
            {
                var srv = serverMatch.Groups["srv"].Value.Trim();
                if (srv.Contains("i-PRO", StringComparison.OrdinalIgnoreCase) || srv.Contains("Panasonic", StringComparison.OrdinalIgnoreCase))
                {
                    brand = "i-PRO";
                    model = srv;
                }
                else if (srv.Length > 2)
                {
                    model = srv;
                }
            }

            dict.AddOrUpdate(ip,
                _ => new DiscoveredCamera
                {
                    IPAddress = ip,
                    HttpPort = port,
                    Brand = brand,
                    Model = model,
                    Protocol = "SSDP / UPnP",
                    ExtraInfo = serverMatch.Success ? serverMatch.Groups["srv"].Value.Trim() : null
                },
                (_, existing) =>
                {
                    if (existing.Brand == "Unknown" || existing.Brand == "Generic ONVIF")
                        existing.Brand = brand;
                    return existing;
                });
        }
        catch { }
    }

    // =========================================================================
    // ENGINE 4: HIKVISION SADP MULTICAST (Multicast 239.255.255.250:37020)
    // =========================================================================
    private async Task RunHikvisionSadpDiscoveryAsync(
        ConcurrentDictionary<string, DiscoveredCamera> dict,
        List<IPAddress> localIps,
        CancellationToken ct)
    {
        const string MulticastAddress = "239.255.255.250";
        const int Port = 37020;

        var probeXml = $"<?xml version=\"1.0\" encoding=\"utf-8\"?><Probe><Uuid>{Guid.NewGuid()}</Uuid><Types>inquiry</Types></Probe>";
        byte[] requestBytes = Encoding.UTF8.GetBytes(probeXml);
        var clients = new List<UdpClient>();

        try
        {
            var endpoint = new IPEndPoint(IPAddress.Parse(MulticastAddress), Port);

            foreach (var ip in localIps)
            {
                try
                {
                    var client = new UdpClient();
                    client.EnableBroadcast = true;
                    client.Client.Bind(new IPEndPoint(ip, 0));
                    clients.Add(client);
                    await client.SendAsync(requestBytes, requestBytes.Length, endpoint);
                }
                catch { }
            }

            var stopWatch = System.Diagnostics.Stopwatch.StartNew();
            while (stopWatch.ElapsedMilliseconds < 2500 && !ct.IsCancellationRequested)
            {
                bool anyAvailable = false;
                foreach (var client in clients)
                {
                    if (client.Available > 0)
                    {
                        anyAvailable = true;
                        try
                        {
                            var result = await client.ReceiveAsync(ct);
                            var xml = Encoding.UTF8.GetString(result.Buffer);
                            ParseSadpResponse(xml, result.RemoteEndPoint.Address.ToString(), dict);
                        }
                        catch { }
                    }
                }

                if (!anyAvailable)
                {
                    await Task.Delay(40, ct);
                }
            }
        }
        finally
        {
            foreach (var c in clients)
            {
                try { c.Dispose(); } catch { }
            }
        }
    }

    private static void ParseSadpResponse(string xml, string senderIp, ConcurrentDictionary<string, DiscoveredCamera> dict)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root == null) return;

            var ip = root.Element("IPv4Address")?.Value ?? senderIp;
            var mac = root.Element("MAC")?.Value;
            var model = root.Element("DeviceDescription")?.Value ?? root.Element("DeviceType")?.Value ?? "Hikvision Camera";
            var version = root.Element("SoftwareVersion")?.Value;
            var portStr = root.Element("HttpPort")?.Value ?? root.Element("CommandPort")?.Value;
            int port = 80;
            if (!string.IsNullOrWhiteSpace(portStr) && int.TryParse(portStr, out int p)) port = p;

            dict.AddOrUpdate(ip,
                _ => new DiscoveredCamera
                {
                    IPAddress = ip,
                    MACAddress = FormatMac(mac),
                    Brand = "Hikvision",
                    Model = model,
                    FirmwareVersion = version,
                    HttpPort = port,
                    Protocol = "Hikvision-SADP"
                },
                (_, existing) =>
                {
                    existing.Brand = "Hikvision";
                    existing.Model = model;
                    if (!string.IsNullOrWhiteSpace(mac)) existing.MACAddress = FormatMac(mac);
                    return existing;
                });
        }
        catch { }
    }

    // =========================================================================
    // ENGINE 5: LOCAL ARP CACHE SCANNER (For ARH / LPR / Radar & Hidden Cameras)
    // =========================================================================
    private Task ScanArpCacheForCamerasAsync(
        ConcurrentDictionary<string, DiscoveredCamera> dict,
        CancellationToken ct)
    {
        return Task.Run(() =>
        {
            try
            {
                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "arp",
                    Arguments = "-a",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });

                if (process == null) return;

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(1000);

                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (ct.IsCancellationRequested) break;

                    var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && IPAddress.TryParse(parts[0], out var parsedIp))
                    {
                        var ipStr = parsedIp.ToString();
                        var macStr = parts[1].Replace('-', ':').ToUpperInvariant();

                        if (macStr.Length == 17)
                        {
                            var vendor = CameraOuiDatabase.Lookup(macStr);
                            if (vendor != null)
                            {
                                dict.AddOrUpdate(ipStr,
                                    _ => new DiscoveredCamera
                                    {
                                        IPAddress = ipStr,
                                        MACAddress = macStr,
                                        Brand = vendor.Brand,
                                        Model = $"{vendor.CompanyName} ({vendor.Category})",
                                        Protocol = "ARP / OUI Lookup",
                                        ExtraInfo = $"{vendor.CompanyName} ({vendor.Category})"
                                    },
                                    (_, existing) =>
                                    {
                                        existing.MACAddress = macStr;
                                        if (existing.Brand == "Unknown" || existing.Brand == "Generic ONVIF")
                                            existing.Brand = vendor.Brand;
                                        if (existing.Model == "IP Camera")
                                            existing.Model = $"{vendor.CompanyName} ({vendor.Category})";
                                        return existing;
                                    });
                            }
                        }
                    }
                }
            }
            catch { }
        }, ct);
    }

    // =========================================================================
    // HELPER METHODS
    // =========================================================================
    private static List<(IPAddress Ip, IPAddress Mask)> GetAllLocalSubnets(string? requestedIp)
    {
        var list = new List<(IPAddress Ip, IPAddress Mask)>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                var ipProps = ni.GetIPProperties();
                foreach (var u in ipProps.UnicastAddresses)
                {
                    if (u.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        var ipStr = u.Address.ToString();
                        if (string.IsNullOrWhiteSpace(requestedIp))
                        {
                            if (!ipStr.StartsWith("169.254."))
                                list.Add((u.Address, u.IPv4Mask));
                        }
                        else if (ipStr == requestedIp)
                        {
                            list.Add((u.Address, u.IPv4Mask));
                        }
                    }
                }
            }
        }
        catch { }

        return list;
    }

    private static List<IPAddress> GetActiveLocalIps(string? requestedIp)
    {
        var ips = new List<IPAddress>();
        if (!string.IsNullOrWhiteSpace(requestedIp) && IPAddress.TryParse(requestedIp, out var rIp))
        {
            ips.Add(rIp);
            return ips;
        }

        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                var ipProps = ni.GetIPProperties();
                foreach (var u in ipProps.UnicastAddresses)
                {
                    if (u.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        var s = u.Address.ToString();
                        if (!s.StartsWith("169.254.") && !s.StartsWith("127."))
                            ips.Add(u.Address);
                    }
                }
            }
        }
        catch { }

        if (ips.Count == 0) ips.Add(IPAddress.Any);
        return ips;
    }

    private static bool IsInAnyLocalSubnet(IPAddress camIp, List<(IPAddress Ip, IPAddress Mask)> localSubnets)
    {
        foreach (var (localIp, mask) in localSubnets)
        {
            if (IsInSameSubnet(camIp, localIp, mask))
                return true;
        }
        return false;
    }

    private static bool IsInSameSubnet(IPAddress ip1, IPAddress ip2, IPAddress mask)
    {
        byte[] b1 = ip1.GetAddressBytes();
        byte[] b2 = ip2.GetAddressBytes();
        byte[] m = mask.GetAddressBytes();

        for (int i = 0; i < 4; i++)
        {
            if ((b1[i] & m[i]) != (b2[i] & m[i]))
                return false;
        }

        return true;
    }

    private static string? FormatMac(string? mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return null;
        var clean = mac.Replace(":", "").Replace("-", "").Trim().ToUpperInvariant();
        if (clean.Length == 12)
        {
            return $"{clean[0..2]}:{clean[2..4]}:{clean[4..6]}:{clean[6..8]}:{clean[8..10]}:{clean[10..12]}";
        }
        return mac;
    }

    private static string BuildRtspUrl(DiscoveredCamera cam)
    {
        var brand = cam.Brand.ToLowerInvariant();
        if (brand.Contains("dahua"))
            return $"rtsp://{cam.IPAddress}:{cam.RtspPort}/cam/realmonitor?channel=1&subtype=0";

        if (brand.Contains("ipro") || brand.Contains("panasonic"))
            return $"rtsp://{cam.IPAddress}:{cam.RtspPort}/MediaInput/stream_1";

        if (brand.Contains("hikvision"))
            return $"rtsp://{cam.IPAddress}:{cam.RtspPort}/Streaming/Channels/101";

        if (brand.Contains("axis"))
            return $"rtsp://{cam.IPAddress}:{cam.RtspPort}/axis-media/media.amp";

        return $"rtsp://{cam.IPAddress}:{cam.RtspPort}/live";
    }
}

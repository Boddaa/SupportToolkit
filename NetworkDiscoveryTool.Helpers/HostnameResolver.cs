using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkDiscoveryTool.Helpers;

public static class HostnameResolver
{
    private static readonly HttpClient HttpClientInstance = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromMilliseconds(600),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 2,
    })
    {
        Timeout = TimeSpan.FromMilliseconds(800)
    };

    public static async Task<string> ResolveHostnameAsync(string ip, int timeoutMs = 600)
    {
        // 1. Try NetBIOS Name Query (UDP 137) - Best for Windows, Samba, LAN Workstations
        try
        {
            var netbiosTask = QueryNetBiosNameAsync(ip, timeoutMs);
            var netbiosName = await netbiosTask;
            if (!string.IsNullOrWhiteSpace(netbiosName) && netbiosName != ip)
            {
                return CleanHostname(netbiosName);
            }
        }
        catch { }

        // 2. Try Standard DNS / mDNS PTR Query
        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            var hostEntryTask = Dns.GetHostEntryAsync(ip, cts.Token);
            var hostEntry = await hostEntryTask;
            if (!string.IsNullOrWhiteSpace(hostEntry.HostName) && hostEntry.HostName != ip)
            {
                return CleanHostname(hostEntry.HostName);
            }
        }
        catch { }

        return ip;
    }

    public static async Task<string?> GrabHttpBannerAsync(string ip, IEnumerable<int> openPorts, int timeoutMs = 800)
    {
        var httpPorts = openPorts.Where(p => p is 80 or 8080 or 8000 or 8081 or 443 or 8443).ToList();
        if (httpPorts.Count == 0) return null;

        foreach (var port in httpPorts)
        {
            try
            {
                string scheme = port is 443 or 8443 ? "https" : "http";
                string url = $"{scheme}://{ip}:{port}/";

                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) NetScope/2.0");

                using var cts = new CancellationTokenSource(timeoutMs);
                using var response = await HttpClientInstance.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                string? serverHeader = response.Headers.Server?.ToString();
                string? pageTitle = null;

                try
                {
                    var stream = await response.Content.ReadAsStreamAsync(cts.Token);
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    char[] buffer = new char[2048];
                    int read = await reader.ReadAsync(buffer, 0, buffer.Length);
                    if (read > 0)
                    {
                        string html = new string(buffer, 0, read);
                        var match = Regex.Match(html, @"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                        if (match.Success)
                        {
                            pageTitle = match.Groups[1].Value.Trim();
                            pageTitle = Regex.Replace(pageTitle, @"\s+", " ");
                            pageTitle = WebUtility.HtmlDecode(pageTitle);
                        }
                    }
                }
                catch { }

                if (!string.IsNullOrWhiteSpace(pageTitle) && !string.IsNullOrWhiteSpace(serverHeader))
                    return $"{pageTitle} ({serverHeader})";
                if (!string.IsNullOrWhiteSpace(pageTitle))
                    return pageTitle;
                if (!string.IsNullOrWhiteSpace(serverHeader))
                    return serverHeader;
            }
            catch { }
        }

        return null;
    }

    public static async Task<string?> QueryNetBiosNameAsync(string ip, int timeoutMs = 500)
    {
        try
        {
            using var udp = new UdpClient();
            udp.Client.ReceiveTimeout = timeoutMs;
            udp.Client.SendTimeout = timeoutMs;

            byte[] query = new byte[] {
                0x80, 0x94, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x20, 0x43, 0x4B, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41,
                0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41,
                0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x00,
                0x00, 0x21, 0x00, 0x01
            };

            var ep = new IPEndPoint(IPAddress.Parse(ip), 137);
            await udp.SendAsync(query, query.Length, ep);

            var receiveTask = udp.ReceiveAsync();
            if (await Task.WhenAny(receiveTask, Task.Delay(timeoutMs)) == receiveTask)
            {
                var res = receiveTask.Result;
                byte[] data = res.Buffer;
                if (data.Length > 56)
                {
                    int nameCount = data[56];
                    int offset = 57;
                    string? workstationName = null;

                    for (int i = 0; i < nameCount && offset + 18 <= data.Length; i++)
                    {
                        string name = Encoding.ASCII.GetString(data, offset, 15).Trim();
                        byte type = data[offset + 15];
                        byte flags = data[offset + 16];
                        bool isGroup = (flags & 0x80) != 0;

                        if (!isGroup && type == 0x00 && !string.IsNullOrEmpty(name))
                        {
                            workstationName = name;
                            break;
                        }
                        if (!isGroup && type == 0x20 && workstationName == null)
                        {
                            workstationName = name;
                        }
                        offset += 18;
                    }
                    return workstationName;
                }
            }
        }
        catch { }
        return null;
    }

    public static string CleanHostname(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        var name = raw.Trim().TrimEnd('.');

        string[] suffixes = [".local", ".lan", ".home", ".localdomain", ".corp", ".internal"];
        foreach (var suf in suffixes)
        {
            if (name.EndsWith(suf, StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^suf.Length];
                break;
            }
        }

        // Tailscale suffix (e.g. mti.taild1bdf0.ts.net)
        if (name.Contains(".ts.net", StringComparison.OrdinalIgnoreCase))
        {
            int firstDot = name.IndexOf('.');
            if (firstDot > 0) name = name[..firstDot];
        }

        return name;
    }
}

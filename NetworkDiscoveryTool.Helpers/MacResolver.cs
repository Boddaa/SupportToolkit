using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace NetworkDiscoveryTool.Helpers;

public static class MacResolver
{
    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern int SendARP(uint destIP, uint srcIP, byte[] pMacAddr, ref uint phyAddrLen);

    public static async Task<string> ResolveAsync(string ip)
    {
        // 1. Instant Win32 SendARP (Direct Layer-2 probe)
        try
        {
            if (IPAddress.TryParse(ip, out var addr))
            {
                byte[] macBytes = new byte[6];
                uint len = (uint)macBytes.Length;
                uint dest = BitConverter.ToUInt32(addr.GetAddressBytes(), 0);

                if (SendARP(dest, 0, macBytes, ref len) == 0 && len == 6)
                {
                    var mac = BitConverter.ToString(macBytes).Replace('-', ':').ToUpperInvariant();
                    if (!string.IsNullOrWhiteSpace(mac) && mac != "00:00:00:00:00:00")
                        return mac;
                }
            }
        }
        catch
        {
            // Fallback to CLI ARP
        }

        // 2. Fallback to CLI arp -a
        return await Task.Run(() =>
        {
            try
            {
                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "arp",
                    Arguments = $"-a {ip}",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });

                if (process is null) return string.Empty;

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(500);

                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.Contains(ip, StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            var mac = parts[1].Replace('-', ':').ToUpperInvariant();
                            if (mac.Length == 17) return mac;
                        }
                    }
                }
            }
            catch { }

            return string.Empty;
        });
    }
}

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace NetworkDiscoveryTool.Helpers;

public static class MacResolver
{
    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern int SendARP(uint destIP, uint srcIP, byte[] pMacAddr, ref uint phyAddrLen);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetIpNetTable(IntPtr pIpNetTable, ref int pdwSize, bool bOrder);

    private const int ERROR_INSUFFICIENT_BUFFER = 122;

    public static Task<string> ResolveAsync(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip) || !IPAddress.TryParse(ip, out var addr))
            return Task.FromResult(string.Empty);

        // 1. Loopback check
        if (IPAddress.IsLoopback(addr))
            return Task.FromResult("00:00:00:00:00:00");

        // 2. Check local NIC IP match
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                var ipProps = nic.GetIPProperties();
                if (ipProps.UnicastAddresses.Any(u => u.Address.Equals(addr)))
                {
                    var physBytes = nic.GetPhysicalAddress().GetAddressBytes();
                    if (physBytes.Length == 6)
                        return Task.FromResult(BitConverter.ToString(physBytes).Replace('-', ':').ToUpperInvariant());
                }
            }
        }
        catch { }

        // 3. Instant Win32 SendARP (Direct Layer-2 probe)
        try
        {
            byte[] macBytes = new byte[6];
            uint len = (uint)macBytes.Length;
            uint dest = BitConverter.ToUInt32(addr.GetAddressBytes(), 0);

            if (SendARP(dest, 0, macBytes, ref len) == 0 && len == 6)
            {
                var mac = BitConverter.ToString(macBytes).Replace('-', ':').ToUpperInvariant();
                if (!string.IsNullOrWhiteSpace(mac) && mac != "00:00:00:00:00:00")
                    return Task.FromResult(mac);
            }
        }
        catch { }

        // 4. Fallback to native Win32 ARP table (GetIpNetTable) - 0.1ms, zero processes spawned
        try
        {
            var macFromTable = GetMacFromArpTable(addr);
            if (!string.IsNullOrWhiteSpace(macFromTable))
                return Task.FromResult(macFromTable);
        }
        catch { }

        return Task.FromResult(string.Empty);
    }

    private static string? GetMacFromArpTable(IPAddress targetIp)
    {
        uint targetUint = BitConverter.ToUInt32(targetIp.GetAddressBytes(), 0);
        int bufferSize = 0;

        // First call to determine required buffer size
        GetIpNetTable(IntPtr.Zero, ref bufferSize, false);
        if (bufferSize <= 0) return null;

        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            if (GetIpNetTable(buffer, ref bufferSize, false) == 0)
            {
                int numEntries = Marshal.ReadInt32(buffer);
                int offset = 4; // Start of MIB_IPNETROW entries
                const int rowSize = 24; // sizeof(MIB_IPNETROW): 4 + 4 + 8 + 4 + 4

                for (int i = 0; i < numEntries && offset + rowSize <= bufferSize; i++)
                {
                    int physLen = Marshal.ReadInt32(buffer, offset + 4);
                    uint entryIp = (uint)Marshal.ReadInt32(buffer, offset + 16);

                    if (entryIp == targetUint && physLen == 6)
                    {
                        byte[] macBytes = new byte[6];
                        Marshal.Copy(IntPtr.Add(buffer, offset + 8), macBytes, 0, 6);
                        var mac = BitConverter.ToString(macBytes).Replace('-', ':').ToUpperInvariant();
                        if (mac != "00:00:00:00:00:00")
                            return mac;
                    }

                    offset += rowSize;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return null;
    }
}

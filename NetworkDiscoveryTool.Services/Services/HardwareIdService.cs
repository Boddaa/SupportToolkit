using System;
using System.Management;
using System.Security.Cryptography;
using System.Text;

namespace NetworkDiscoveryTool.Services.Services;

public static class HardwareIdService
{
    private static string? _cachedHardwareId;

    public static string GetHardwareId()
    {
        if (!string.IsNullOrEmpty(_cachedHardwareId))
            return _cachedHardwareId;

        try
        {
            var sb = new StringBuilder();
            sb.Append(Environment.MachineName);
            sb.Append(Environment.UserName);
            sb.Append(GetCpuId());
            sb.Append(GetVolumeSerial());

            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var hash = sha256.ComputeHash(bytes);
            
            var hex = Convert.ToHexString(hash);
            _cachedHardwareId = $"STK-{hex[..4]}-{hex[4..8]}-{hex[8..12]}-{hex[12..16]}";
            return _cachedHardwareId;
        }
        catch
        {
            var fallback = $"STK-FB-{Environment.MachineName.GetHashCode():X8}";
            _cachedHardwareId = fallback;
            return fallback;
        }
    }

    private static string GetCpuId()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor");
            foreach (ManagementObject obj in searcher.Get())
            {
                var id = obj["ProcessorId"]?.ToString();
                if (!string.IsNullOrWhiteSpace(id))
                    return id;
            }
        }
        catch { /* WMI fallback */ }
        return Environment.ProcessorCount.ToString();
    }

    private static string GetVolumeSerial()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT VolumeSerialNumber FROM Win32_LogicalDisk WHERE DeviceID='C:'");
            foreach (ManagementObject obj in searcher.Get())
            {
                var serial = obj["VolumeSerialNumber"]?.ToString();
                if (!string.IsNullOrWhiteSpace(serial))
                    return serial;
            }
        }
        catch { /* WMI fallback */ }
        return Environment.SystemDirectory;
    }
}

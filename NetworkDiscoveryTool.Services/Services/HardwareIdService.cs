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

            // 1. Motherboard Serial (Win32_BaseBoard)
            var mbSerial = GetMotherboardSerial();
            if (!string.IsNullOrWhiteSpace(mbSerial))
                sb.Append("MB:").Append(mbSerial);

            // 2. Computer System Product UUID (Win32_ComputerSystemProduct)
            var sysUuid = GetSystemUuid();
            if (!string.IsNullOrWhiteSpace(sysUuid))
                sb.Append("|UUID:").Append(sysUuid);

            // 3. BIOS Serial Number (Win32_BIOS)
            var biosSerial = GetBiosSerial();
            if (!string.IsNullOrWhiteSpace(biosSerial))
                sb.Append("|BIOS:").Append(biosSerial);

            // 4. Processor ID (Win32_Processor)
            var cpuId = GetCpuId();
            if (!string.IsNullOrWhiteSpace(cpuId))
                sb.Append("|CPU:").Append(cpuId);

            // 5. C: Volume Serial Number
            var volSerial = GetVolumeSerial();
            if (!string.IsNullOrWhiteSpace(volSerial))
                sb.Append("|VOL:").Append(volSerial);

            // Fallback if all WMI calls were empty
            if (sb.Length == 0)
            {
                sb.Append("MACHINE:").Append(Environment.MachineName);
            }

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

    private static string GetMotherboardSerial()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard");
            foreach (ManagementObject obj in searcher.Get())
            {
                var serial = obj["SerialNumber"]?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(serial) && serial != "None" && serial != "Default string")
                    return serial;
            }
        }
        catch { }
        return string.Empty;
    }

    private static string GetSystemUuid()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT UUID FROM Win32_ComputerSystemProduct");
            foreach (ManagementObject obj in searcher.Get())
            {
                var uuid = obj["UUID"]?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(uuid) && uuid != "FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF")
                    return uuid;
            }
        }
        catch { }
        return string.Empty;
    }

    private static string GetBiosSerial()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BIOS");
            foreach (ManagementObject obj in searcher.Get())
            {
                var serial = obj["SerialNumber"]?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(serial) && serial != "None" && serial != "Default string")
                    return serial;
            }
        }
        catch { }
        return string.Empty;
    }

    private static string GetCpuId()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor");
            foreach (ManagementObject obj in searcher.Get())
            {
                var id = obj["ProcessorId"]?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(id))
                    return id;
            }
        }
        catch { }
        return Environment.ProcessorCount.ToString();
    }

    private static string GetVolumeSerial()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT VolumeSerialNumber FROM Win32_LogicalDisk WHERE DeviceID='C:'");
            foreach (ManagementObject obj in searcher.Get())
            {
                var serial = obj["VolumeSerialNumber"]?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(serial))
                    return serial;
            }
        }
        catch { }
        return string.Empty;
    }
}

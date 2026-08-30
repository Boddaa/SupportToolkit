using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using NetworkDiscoveryTool.UI.Models;

namespace NetworkDiscoveryTool.UI.Services;

public interface ISystemInformationService
{
    Task<SystemInformationModel> GatherAsync();
}

public sealed class SystemInformationService : ISystemInformationService
{
    public async Task<SystemInformationModel> GatherAsync()
    {
        var model = new SystemInformationModel();

        await Task.Run(() =>
        {
            try { PopulateSystem(model); } catch { }
            try { PopulateOs(model); } catch { }
            try { PopulateCpu(model); } catch { }
            try { PopulateMemory(model); } catch { }
            try { PopulateDrives(model); } catch { }
            try { PopulateNetwork(model); } catch { }
            try { PopulateUptime(model); } catch { }
        });

        return model;
    }

    private static void PopulateSystem(SystemInformationModel m)
    {
        m.MachineName = Environment.MachineName;
        m.UserName = Environment.UserName;
        m.ComputerName = Environment.MachineName;

        using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem");
        foreach (var obj in searcher.Get().Cast<ManagementObject>())
        {
            m.MachineName = obj["Name"]?.ToString() ?? m.MachineName;
            m.ComputerName = obj["DNSHostName"]?.ToString() ?? m.ComputerName;
        }
    }

    private static void PopulateOs(SystemInformationModel m)
    {
        using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem");
        foreach (var obj in searcher.Get().Cast<ManagementObject>())
        {
            m.WindowsVersion = $"{obj["Caption"]} (Build {obj["BuildNumber"]})";
            m.OSArchitecture = obj["OSArchitecture"]?.ToString() ?? "";
        }
    }

    private static void PopulateCpu(SystemInformationModel m)
    {
        using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
        foreach (var obj in searcher.Get().Cast<ManagementObject>())
        {
            m.ProcessorName = obj["Name"]?.ToString()?.Trim() ?? "";
            break;
        }

        try
        {
            using var counter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            counter.NextValue();
            Thread.Sleep(500);
            m.CpuUsage = $"{counter.NextValue():F1}%";
        }
        catch
        {
            m.CpuUsage = "N/A";
        }
    }

    private static void PopulateMemory(SystemInformationModel m)
    {
        using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem");
        foreach (var obj in searcher.Get().Cast<ManagementObject>())
        {
            var total = Convert.ToDouble(obj["TotalVisibleMemorySize"]) / 1048576.0;
            var free = Convert.ToDouble(obj["FreePhysicalMemory"]) / 1048576.0;
            m.InstalledRam = $"{total:F1} GB";
            m.AvailableRam = $"{free:F1} GB";
            break;
        }
    }

    private static void PopulateDrives(SystemInformationModel m)
    {
        foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            var total = d.TotalSize;
            var free = d.AvailableFreeSpace;
            var used = total - free;
            var percent = total > 0 ? (double)used / total * 100.0 : 0;

            m.Drives.Add(new DriveInfoModel
            {
                Name = d.Name,
                Label = d.VolumeLabel ?? "",
                TotalSize = $"{total / 1073741824.0:F1} GB",
                FreeSpace = $"{free / 1073741824.0:F1} GB",
                UsedSpace = $"{used / 1073741824.0:F1} GB",
                UsagePercent = Math.Round(percent, 1),
            });
        }
    }

    private static void PopulateNetwork(SystemInformationModel m)
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
        {
            var ip = ni.GetIPProperties().UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);

            if (ip is null) continue;

            m.IpAddress = ip.Address.ToString();
            m.MacAddress = string.Join(":", ni.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")));
            m.NetworkAdapter = ni.Description;
            break;
        }
    }

    private static void PopulateUptime(SystemInformationModel m)
    {
        var ticks = Environment.TickCount64;
        var uptime = TimeSpan.FromMilliseconds(ticks);
        m.UpTime = uptime.Days > 0
            ? $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s"
            : $"{uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s";
    }
}

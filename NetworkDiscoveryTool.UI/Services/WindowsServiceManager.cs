using System.Management;
using System.ServiceProcess;
using NetworkDiscoveryTool.UI.Models;

namespace NetworkDiscoveryTool.UI.Services;

public interface IWindowsServiceManager
{
    Task<List<ServiceItemModel>> GetAllAsync();
    Task StartAsync(string serviceName);
    Task StopAsync(string serviceName);
    Task RestartAsync(string serviceName);
    Task<ServiceDetailModel?> GetDetailAsync(string serviceName);
}

public sealed class WindowsServiceManager : IWindowsServiceManager
{
    public async Task<List<ServiceItemModel>> GetAllAsync()
    {
        return await Task.Run(() =>
        {
            var result = new List<ServiceItemModel>();

            using var searcher = new ManagementObjectSearcher("SELECT Name, StartMode FROM Win32_Service");
            var startModes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var mo in searcher.Get().Cast<ManagementObject>())
                    startModes[mo["Name"]?.ToString() ?? ""] = mo["StartMode"]?.ToString() ?? "Unknown";
            }
            catch { }

            foreach (var svc in ServiceController.GetServices().OrderBy(s => s.DisplayName))
            {
                startModes.TryGetValue(svc.ServiceName, out var startMode);
                result.Add(new ServiceItemModel
                {
                    Name = svc.ServiceName,
                    DisplayName = svc.DisplayName,
                    Status = svc.Status.ToString(),
                    StartupType = FormatStartupType(startMode ?? "Unknown"),
                    CanStop = svc.CanStop,
                    CanPause = svc.CanPauseAndContinue,
                    StatusCode = (int)svc.Status,
                });
            }

            return result;
        });
    }

    public Task StartAsync(string serviceName)
    {
        return Task.Run(() =>
        {
            using var svc = new ServiceController(serviceName);
            if (svc.Status == ServiceControllerStatus.Stopped)
            {
                svc.Start();
                svc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
            }
        });
    }

    public Task StopAsync(string serviceName)
    {
        return Task.Run(() =>
        {
            using var svc = new ServiceController(serviceName);
            if (svc.CanStop && svc.Status != ServiceControllerStatus.Stopped)
            {
                svc.Stop();
                svc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
            }
        });
    }

    public Task RestartAsync(string serviceName)
    {
        return Task.Run(() =>
        {
            using var svc = new ServiceController(serviceName);
            if (svc.CanStop && svc.Status != ServiceControllerStatus.Stopped)
            {
                svc.Stop();
                svc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
            }
            svc.Start();
            svc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
        });
    }

    public async Task<ServiceDetailModel?> GetDetailAsync(string serviceName)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var svc = new ServiceController(serviceName);
                var detail = new ServiceDetailModel
                {
                    Name = svc.ServiceName,
                    DisplayName = svc.DisplayName,
                    Status = svc.Status.ToString(),
                    CanStop = svc.CanStop,
                    CanPause = svc.CanPauseAndContinue,
                    MachineName = svc.MachineName,
                    ServiceType = svc.ServiceType.ToString(),
                };

                using var searcher = new ManagementObjectSearcher(
                    $"SELECT * FROM Win32_Service WHERE Name = '{serviceName.Replace("'", "''")}'");
                foreach (var mo in searcher.Get().Cast<ManagementObject>())
                {
                    detail.StartupType = FormatStartupType(mo["StartMode"]?.ToString() ?? "");
                    detail.StartName = mo["StartName"]?.ToString() ?? "";
                    detail.PathName = mo["PathName"]?.ToString() ?? "";
                    detail.Description = mo["Description"]?.ToString() ?? "";
                    detail.ProcessId = Convert.ToInt32(mo["ProcessId"]);
                    detail.State = mo["State"]?.ToString() ?? "";
                    detail.ServiceName = mo["Caption"]?.ToString() ?? "";
                }

                return detail;
            }
            catch
            {
                return null;
            }
        });
    }

    private static string FormatStartupType(string mode) => mode.ToLowerInvariant() switch
    {
        "auto" or "automatic" => "Automatic",
        "disabled" => "Disabled",
        "manual" => "Manual",
        "boot" => "Boot",
        "system" => "System",
        _ => mode,
    };
}

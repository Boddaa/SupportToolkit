using System.Collections.ObjectModel;

namespace NetworkDiscoveryTool.UI.Models;

public sealed class SystemInformationModel
{
    public string MachineName { get; set; } = "";
    public string UserName { get; set; } = "";
    public string ComputerName { get; set; } = "";
    public string WindowsVersion { get; set; } = "";
    public string OSArchitecture { get; set; } = "";
    public string ProcessorName { get; set; } = "";
    public string CpuUsage { get; set; } = "0%";
    public string InstalledRam { get; set; } = "";
    public string AvailableRam { get; set; } = "";
    public string UpTime { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public string MacAddress { get; set; } = "";
    public string NetworkAdapter { get; set; } = "";
    public ObservableCollection<DriveInfoModel> Drives { get; set; } = [];
}

public sealed class DriveInfoModel
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public string TotalSize { get; set; } = "";
    public string FreeSpace { get; set; } = "";
    public string UsedSpace { get; set; } = "";
    public double UsagePercent { get; set; }
}

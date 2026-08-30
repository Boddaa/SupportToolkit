using CommunityToolkit.Mvvm.ComponentModel;

namespace NetworkDiscoveryTool.UI.Models;

public sealed class ServiceItemModel
{
    public string Name { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Status { get; set; } = "";
    public string StartupType { get; init; } = "";
    public bool CanStop { get; init; }
    public bool CanPause { get; init; }
    public int StatusCode { get; init; }

    public bool IsRunning => Status == "Running";
    public bool IsStopped => Status == "Stopped";
    public bool CanStart => IsStopped;
    public bool CanExecuteStop => CanStop && IsRunning;
    public bool CanRestart => IsStopped || (IsRunning && CanStop);
}

public sealed class ServiceDetailModel
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Status { get; set; } = "";
    public string StartupType { get; set; } = "";
    public bool CanStop { get; set; }
    public bool CanPause { get; set; }
    public string MachineName { get; set; } = "";
    public string ServiceType { get; set; } = "";
    public string StartName { get; set; } = "";
    public string PathName { get; set; } = "";
    public string Description { get; set; } = "";
    public int ProcessId { get; set; }
    public string State { get; set; } = "";
    public string ServiceName { get; set; } = "";
}

public enum ServiceFilter
{
    All,
    Running,
    Stopped,
}

public enum StartupTypeFilter
{
    All,
    Automatic,
    Manual,
    Disabled,
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NetworkDiscoveryTool.UI.Models;

public sealed partial class ProcessItemModel : ObservableObject
{
    [ObservableProperty] private int _id;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _status = "Running"; // Running, Suspended, Stopped
    [ObservableProperty] private double _cpuUsage;
    [ObservableProperty] private string _cpuUsageText = "0.0%";
    [ObservableProperty] private long _memoryBytes;
    [ObservableProperty] private string _memoryUsageText = "0 MB";
    [ObservableProperty] private double _memoryPercent;
    [ObservableProperty] private int _threadCount;
    [ObservableProperty] private int _handleCount;
    [ObservableProperty] private string _userName = "SYSTEM";
    [ObservableProperty] private string _priority = "Normal";
    [ObservableProperty] private string _executablePath = "N/A";
    [ObservableProperty] private string _commandLine = "N/A";
    [ObservableProperty] private DateTime? _startTime;
    [ObservableProperty] private string _startTimeText = "N/A";
    [ObservableProperty] private int? _parentProcessId;
    [ObservableProperty] private string? _parentProcessName;
    [ObservableProperty] private string _company = "Unknown";
    [ObservableProperty] private string _fileVersion = "N/A";
    [ObservableProperty] private int _sessionId = 1;
    [ObservableProperty] private bool _isSystemProcess;
    [ObservableProperty] private bool _isApplication;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isExpanded = true;
    [ObservableProperty] private int _indentLevel;
    [ObservableProperty] private bool _hasChildren;

    // Real executable icon
    [ObservableProperty] private ImageSource? _icon;
    [ObservableProperty] private bool _hasCustomIcon;

    // History buffers for sparklines (up to 30 points)
    public List<double> CpuHistory { get; } = new(35);
    public List<double> MemoryHistory { get; } = new(35);

    // Tree hierarchy
    public ObservableCollection<ProcessItemModel> Children { get; } = new();

    public void AddCpuSample(double cpu)
    {
        CpuHistory.Add(cpu);
        if (CpuHistory.Count > 30)
            CpuHistory.RemoveAt(0);
    }

    public void AddMemorySample(double memBytes)
    {
        MemoryHistory.Add(memBytes);
        if (MemoryHistory.Count > 30)
            MemoryHistory.RemoveAt(0);
    }
}

public sealed class StartupItemModel
{
    public string Name { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string Status { get; set; } = "Enabled";
    public string Impact { get; set; } = "Medium";
    public string Command { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public ImageSource? Icon { get; set; }
    public bool HasCustomIcon => Icon != null;
}

public sealed class SystemPerformanceSnapshot
{
    public double CpuUsagePercent { get; set; }
    public string CpuSpeedGhz { get; set; } = "N/A";
    public long TotalMemoryBytes { get; set; }
    public long UsedMemoryBytes { get; set; }
    public long FreeMemoryBytes { get; set; }
    public double MemoryUsagePercent { get; set; }
    public int TotalProcesses { get; set; }
    public int TotalThreads { get; set; }
    public int TotalHandles { get; set; }
    public List<ProcessItemModel> Processes { get; set; } = new();
}

public enum ProcessCategoryFilter
{
    All,
    Applications,
    BackgroundProcesses,
    SystemProcesses
}

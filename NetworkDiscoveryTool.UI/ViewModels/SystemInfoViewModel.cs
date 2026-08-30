using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetworkDiscoveryTool.UI.Models;
using NetworkDiscoveryTool.UI.Services;

namespace NetworkDiscoveryTool.UI.ViewModels;

public sealed partial class SystemInfoViewModel : ObservableObject
{
    private readonly ISystemInformationService _service;

    public SystemInfoViewModel(ISystemInformationService service)
    {
        _service = service;
    }

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = "";

    // System
    [ObservableProperty] private string _machineName = "";
    [ObservableProperty] private string _userName = "";
    [ObservableProperty] private string _computerName = "";

    // OS
    [ObservableProperty] private string _windowsVersion = "";
    [ObservableProperty] private string _oSArchitecture = "";

    // CPU
    [ObservableProperty] private string _processorName = "";
    [ObservableProperty] private string _cpuUsage = "";

    // Memory
    [ObservableProperty] private string _installedRam = "";
    [ObservableProperty] private string _availableRam = "";

    // Network
    [ObservableProperty] private string _ipAddress = "";
    [ObservableProperty] private string _macAddress = "";
    [ObservableProperty] private string _networkAdapter = "";

    // Uptime
    [ObservableProperty] private string _upTime = "";

    // Drives
    public ObservableCollection<DriveInfoModel> Drives { get; } = [];

    // CPU usage color
    public string CpuUsageColor => CpuUsage switch
    {
        string s when s.Contains('%') && double.TryParse(s[..^1], out var v) => v switch
        {
            > 80 => "#EF4444",
            > 50 => "#F59E0B",
            _ => "#22C55E"
        },
        _ => "#64748B"
    };

    [RelayCommand]
    private async Task LoadSystemInfoAsync()
    {
        IsLoading = true;
        HasError = false;
        ErrorMessage = "";

        try
        {
            var result = await _service.GatherAsync();

            MachineName = result.MachineName;
            UserName = result.UserName;
            ComputerName = result.ComputerName;
            WindowsVersion = result.WindowsVersion;
            OSArchitecture = result.OSArchitecture;
            ProcessorName = result.ProcessorName;
            CpuUsage = result.CpuUsage;
            InstalledRam = result.InstalledRam;
            AvailableRam = result.AvailableRam;
            UpTime = result.UpTime;
            IpAddress = result.IpAddress;
            MacAddress = result.MacAddress;
            NetworkAdapter = result.NetworkAdapter;

            Drives.Clear();
            foreach (var d in result.Drives)
                Drives.Add(d);

            OnPropertyChanged(nameof(CpuUsageColor));
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Failed to load system info: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}

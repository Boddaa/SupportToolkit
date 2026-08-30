using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using NetworkDiscoveryTool.UI.ViewModels;

namespace NetworkDiscoveryTool.UI.Views.SystemInfo;

public partial class SystemInfoPage : INotifyPropertyChanged
{
    private readonly SystemInfoViewModel _vm;
    private string _lastRefreshTime = "Last updated: --";

    public string LastRefreshTime
    {
        get => _lastRefreshTime;
        set { _lastRefreshTime = value; OnPropertyChanged(); }
    }

    public SystemInfoPage(SystemInfoViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        _vm = vm;
        _vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(_vm.IsLoading) && !_vm.IsLoading && _vm.MachineName != "")
            LastRefreshTime = $"Last updated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_vm.MachineName == "")
            _vm.LoadSystemInfoCommand.Execute(null);
    }

    private void CopyInfo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Machine Name: {_vm.MachineName}");
            sb.AppendLine($"Computer Name: {_vm.ComputerName}");
            sb.AppendLine($"User Name: {_vm.UserName}");
            sb.AppendLine($"Windows Version: {_vm.WindowsVersion}");
            sb.AppendLine($"Architecture: {_vm.OSArchitecture}");
            sb.AppendLine($"Processor: {_vm.ProcessorName}");
            sb.AppendLine($"CPU Usage: {_vm.CpuUsage}");
            sb.AppendLine($"Installed RAM: {_vm.InstalledRam}");
            sb.AppendLine($"Available RAM: {_vm.AvailableRam}");
            sb.AppendLine($"Uptime: {_vm.UpTime}");
            sb.AppendLine($"IP Address: {_vm.IpAddress}");
            sb.AppendLine($"MAC Address: {_vm.MacAddress}");
            sb.AppendLine($"Network Adapter: {_vm.NetworkAdapter}");
            System.Windows.Clipboard.SetText(sb.ToString());
        }
        catch { /* suppress */ }
    }

    private void ExportTxt_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt",
                FileName = $"SystemInfo_{Environment.MachineName}_{DateTime.Now:yyyyMMdd}.txt"
            };
            if (dialog.ShowDialog() == true)
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== System Information ===");
                sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Machine: {_vm.MachineName}");
                sb.AppendLine($"Computer: {_vm.ComputerName}");
                sb.AppendLine($"User: {_vm.UserName}");
                sb.AppendLine($"Windows: {_vm.WindowsVersion}");
                sb.AppendLine($"Architecture: {_vm.OSArchitecture}");
                sb.AppendLine($"Processor: {_vm.ProcessorName}");
                sb.AppendLine($"CPU Usage: {_vm.CpuUsage}");
                sb.AppendLine($"Installed RAM: {_vm.InstalledRam}");
                sb.AppendLine($"Available RAM: {_vm.AvailableRam}");
                sb.AppendLine($"Uptime: {_vm.UpTime}");
                sb.AppendLine($"IP Address: {_vm.IpAddress}");
                sb.AppendLine($"MAC Address: {_vm.MacAddress}");
                sb.AppendLine($"Network Adapter: {_vm.NetworkAdapter}");
                File.WriteAllText(dialog.FileName, sb.ToString());
            }
        }
        catch { /* suppress */ }
    }

    private void ExportJson_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON Files (*.json)|*.json",
                FileName = $"SystemInfo_{Environment.MachineName}_{DateTime.Now:yyyyMMdd}.json"
            };
            if (dialog.ShowDialog() == true)
            {
                var escaped = new Func<string, string>(s => s.Replace("\\", "\\\\").Replace("\"", "\\\""));
                var json = $"{{\n" +
                    $"  \"generated\": \"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\",\n" +
                    $"  \"machineName\": \"{escaped(_vm.MachineName)}\",\n" +
                    $"  \"computerName\": \"{escaped(_vm.ComputerName)}\",\n" +
                    $"  \"userName\": \"{escaped(_vm.UserName)}\",\n" +
                    $"  \"windowsVersion\": \"{escaped(_vm.WindowsVersion)}\",\n" +
                    $"  \"architecture\": \"{escaped(_vm.OSArchitecture)}\",\n" +
                    $"  \"processor\": \"{escaped(_vm.ProcessorName)}\",\n" +
                    $"  \"cpuUsage\": \"{escaped(_vm.CpuUsage)}\",\n" +
                    $"  \"installedRam\": \"{escaped(_vm.InstalledRam)}\",\n" +
                    $"  \"availableRam\": \"{escaped(_vm.AvailableRam)}\",\n" +
                    $"  \"uptime\": \"{escaped(_vm.UpTime)}\",\n" +
                    $"  \"ipAddress\": \"{escaped(_vm.IpAddress)}\",\n" +
                    $"  \"macAddress\": \"{escaped(_vm.MacAddress)}\",\n" +
                    $"  \"networkAdapter\": \"{escaped(_vm.NetworkAdapter)}\"\n" +
                    $"}}";
                File.WriteAllText(dialog.FileName, json);
            }
        }
        catch { /* suppress */ }
    }

    private void OpenNetworkSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:network",
                UseShellExecute = true
            });
        }
        catch { /* suppress */ }
    }
}

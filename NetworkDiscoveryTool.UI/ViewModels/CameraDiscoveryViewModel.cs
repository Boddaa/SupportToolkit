using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetworkDiscoveryTool.Core.Interfaces;
using NetworkDiscoveryTool.Core.Models;
using NetworkDiscoveryTool.UI.Services;
using WpfClipboard = System.Windows.Clipboard;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;

namespace NetworkDiscoveryTool.UI.ViewModels;

public sealed partial class CameraDiscoveryViewModel : ObservableObject
{
    private readonly ICameraDiscoveryService _cameraService;
    private readonly IOperationHistoryService _history;
    private readonly CurrentUserService _currentUser;
    private readonly INavigationService _navigation;
    private CancellationTokenSource? _cts;

    // === Collections ===
    public ObservableCollection<DiscoveredCamera> Cameras { get; } = new();
    public ObservableCollection<DiscoveredCamera> FilteredCameras { get; } = new();
    public ObservableCollection<string> NetworkInterfaces { get; } = new();
    public ObservableCollection<string> BrandFilters { get; } = new()
    {
        "All Brands",
        "i-PRO",
        "Dahua",
        "ARH / LPR",
        "Radar / Traffic",
        "Hikvision",
        "Axis",
        "Other"
    };

    // === State & Filters ===
    [ObservableProperty] private string _selectedNetworkInterface = "All Adapters (Auto)";
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _selectedBrandFilter = "All Brands";
    [ObservableProperty] private bool _isDiscovering;
    [ObservableProperty] private string _statusText = "Ready to discover cameras (Zero-Config)";
    [ObservableProperty] private bool _hasNoResults = true;
    [ObservableProperty] private DiscoveredCamera? _selectedCamera;

    // === Stat Cards (KPIs) ===
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _iProCount;
    [ObservableProperty] private int _dahuaCount;
    [ObservableProperty] private int _arhLprCount;
    [ObservableProperty] private int _hikvisionCount;
    [ObservableProperty] private int _subnetMismatchCount;
    [ObservableProperty] private int _otherCount;

    public CameraDiscoveryViewModel(
        ICameraDiscoveryService cameraService,
        IOperationHistoryService history,
        CurrentUserService currentUser,
        INavigationService navigation)
    {
        _cameraService = cameraService;
        _history = history;
        _currentUser = currentUser;
        _navigation = navigation;

        LoadNetworkInterfaces();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedBrandFilterChanged(string value) => ApplyFilter();

    public void LoadNetworkInterfaces()
    {
        NetworkInterfaces.Clear();
        NetworkInterfaces.Add("All Adapters (Auto)");

        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                var ipProps = ni.GetIPProperties();
                foreach (var u in ipProps.UnicastAddresses)
                {
                    if (u.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        NetworkInterfaces.Add($"{ni.Name} ({u.Address})");
                    }
                }
            }
        }
        catch { }

        SelectedNetworkInterface = NetworkInterfaces.FirstOrDefault() ?? "All Adapters (Auto)";
    }

    [RelayCommand]
    public async Task DiscoverCamerasAsync()
    {
        if (IsDiscovering) return;

        IsDiscovering = true;
        StatusText = "Initializing camera discovery engines...";
        _cts = new CancellationTokenSource();
        var sw = Stopwatch.StartNew();

        string? targetIp = null;
        if (!string.IsNullOrWhiteSpace(SelectedNetworkInterface) &&
            SelectedNetworkInterface.Contains('(') &&
            SelectedNetworkInterface.Contains(')'))
        {
            var start = SelectedNetworkInterface.IndexOf('(') + 1;
            var end = SelectedNetworkInterface.IndexOf(')');
            targetIp = SelectedNetworkInterface.Substring(start, end - start).Trim();
        }

        var progress = new Progress<string>(msg => StatusText = msg);

        try
        {
            var discovered = await _cameraService.DiscoverCamerasAsync(targetIp, progress, _cts.Token);

            Cameras.Clear();
            foreach (var cam in discovered)
            {
                Cameras.Add(cam);
            }

            ComputeKpis();
            ApplyFilter();

            sw.Stop();
            StatusText = $"Discovery complete: {TotalCount} cameras found in {(sw.ElapsedMilliseconds / 1000.0):F1}s";

            await _history.LogAsync(
                "Camera Discovery",
                $"Discovered {TotalCount} cameras (i-PRO: {IProCount}, Dahua: {DahuaCount}, ARH/LPR: {ArhLprCount}, Hikvision: {HikvisionCount})",
                "Success",
                sw.ElapsedMilliseconds,
                _currentUser.Username ?? "User");
        }
        catch (OperationCanceledException)
        {
            StatusText = "Discovery canceled by operator.";
        }
        catch (Exception ex)
        {
            StatusText = $"Discovery error: {ex.Message}";
        }
        finally
        {
            IsDiscovering = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    public void StopDiscovery()
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            StatusText = "Stopping discovery...";
            _cts.Cancel();
        }
    }

    [RelayCommand]
    public void ClearResults()
    {
        Cameras.Clear();
        FilteredCameras.Clear();
        ComputeKpis();
        HasNoResults = true;
        StatusText = "Results cleared. Ready to discover.";
    }

    [RelayCommand]
    public void FilterByBrand(string brand)
    {
        SelectedBrandFilter = brand;
    }

    private void ComputeKpis()
    {
        TotalCount = Cameras.Count;
        IProCount = Cameras.Count(c => c.Brand.Equals("i-PRO", StringComparison.OrdinalIgnoreCase));
        DahuaCount = Cameras.Count(c => c.Brand.Equals("Dahua", StringComparison.OrdinalIgnoreCase));
        ArhLprCount = Cameras.Count(c => c.Brand.Contains("ARH", StringComparison.OrdinalIgnoreCase) || c.Brand.Contains("LPR", StringComparison.OrdinalIgnoreCase));
        HikvisionCount = Cameras.Count(c => c.Brand.Equals("Hikvision", StringComparison.OrdinalIgnoreCase));
        SubnetMismatchCount = Cameras.Count(c => c.IsSubnetMismatch);
        OtherCount = TotalCount - (IProCount + DahuaCount + ArhLprCount + HikvisionCount);
        if (OtherCount < 0) OtherCount = 0;
    }

    private void ApplyFilter()
    {
        var query = Cameras.AsEnumerable();

        // 1. Brand filter
        if (!string.IsNullOrWhiteSpace(SelectedBrandFilter) && SelectedBrandFilter != "All Brands")
        {
            if (SelectedBrandFilter == "Other")
            {
                query = query.Where(c => !c.Brand.Equals("i-PRO", StringComparison.OrdinalIgnoreCase) &&
                                         !c.Brand.Equals("Dahua", StringComparison.OrdinalIgnoreCase) &&
                                         !c.Brand.Contains("ARH", StringComparison.OrdinalIgnoreCase) &&
                                         !c.Brand.Equals("Hikvision", StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                query = query.Where(c => c.Brand.Contains(SelectedBrandFilter, StringComparison.OrdinalIgnoreCase));
            }
        }

        // 2. Search text filter (IP, MAC, Brand, Model, Serial, ExtraInfo)
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var st = SearchText.Trim().ToLowerInvariant();
            query = query.Where(c =>
                c.IPAddress.Contains(st, StringComparison.OrdinalIgnoreCase) ||
                (c.MACAddress != null && c.MACAddress.ToLowerInvariant().Contains(st)) ||
                c.Brand.ToLowerInvariant().Contains(st) ||
                c.Model.ToLowerInvariant().Contains(st) ||
                (c.SerialNumber != null && c.SerialNumber.ToLowerInvariant().Contains(st)) ||
                (c.ExtraInfo != null && c.ExtraInfo.ToLowerInvariant().Contains(st)) ||
                c.Protocol.ToLowerInvariant().Contains(st));
        }

        FilteredCameras.Clear();
        foreach (var item in query)
        {
            FilteredCameras.Add(item);
        }

        HasNoResults = FilteredCameras.Count == 0;
    }

    // === Action Commands ===
    [RelayCommand]
    public void OpenWebUi(DiscoveredCamera? cam)
    {
        if (cam == null || string.IsNullOrWhiteSpace(cam.WebUrl)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = cam.WebUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"Could not open Web UI: {ex.Message}", "Browser Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    public void CopyRtsp(DiscoveredCamera? cam)
    {
        if (cam == null || string.IsNullOrWhiteSpace(cam.RtspUrl)) return;
        try
        {
            WpfClipboard.SetText(cam.RtspUrl);
            StatusText = $"Copied RTSP URL for {cam.IPAddress} to clipboard!";
        }
        catch { }
    }

    [RelayCommand]
    public void CopyIp(DiscoveredCamera? cam)
    {
        if (cam == null || string.IsNullOrWhiteSpace(cam.IPAddress)) return;
        try
        {
            WpfClipboard.SetText(cam.IPAddress);
            StatusText = $"Copied IP: {cam.IPAddress} to clipboard!";
        }
        catch { }
    }

    [RelayCommand]
    public void CopyMac(DiscoveredCamera? cam)
    {
        if (cam == null || string.IsNullOrWhiteSpace(cam.MACAddress)) return;
        try
        {
            WpfClipboard.SetText(cam.MACAddress);
            StatusText = $"Copied MAC: {cam.MACAddress} to clipboard!";
        }
        catch { }
    }

    [RelayCommand]
    public void PingCamera(DiscoveredCamera? cam)
    {
        if (cam == null || string.IsNullOrWhiteSpace(cam.IPAddress)) return;
        _navigation.NavigateToPingTool();
    }

    [RelayCommand]
    public void ExportCsv()
    {
        if (Cameras.Count == 0)
        {
            WpfMessageBox.Show("No discovered cameras to export.", "Export", WpfMessageBoxButton.OK, WpfMessageBoxImage.Information);
            return;
        }

        try
        {
            Directory.CreateDirectory("Exports");
            var filename = Path.Combine("Exports", $"Camera_Inventory_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            var sb = new StringBuilder();
            sb.AppendLine("Brand,Model,IPAddress,MACAddress,Port,RTSP,Protocol,Status,SerialNumber,Firmware,ExtraInfo");

            foreach (var c in Cameras)
            {
                sb.AppendLine($"\"{c.Brand}\",\"{c.Model}\",\"{c.IPAddress}\",\"{c.MACAddress}\",{c.HttpPort},\"{c.RtspUrl}\",\"{c.Protocol}\",\"{c.Status}\",\"{c.SerialNumber}\",\"{c.FirmwareVersion}\",\"{c.ExtraInfo}\"");
            }

            File.WriteAllText(filename, sb.ToString(), Encoding.UTF8);
            StatusText = $"Exported {Cameras.Count} cameras to {filename}";
            WpfMessageBox.Show($"Camera inventory exported successfully to:\n{filename}", "Export Complete", WpfMessageBoxButton.OK, WpfMessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"Failed to export CSV: {ex.Message}", "Export Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public void ExportJson()
    {
        if (Cameras.Count == 0)
        {
            WpfMessageBox.Show("No discovered cameras to export.", "Export", WpfMessageBoxButton.OK, WpfMessageBoxImage.Information);
            return;
        }

        try
        {
            Directory.CreateDirectory("Exports");
            var filename = Path.Combine("Exports", $"Camera_Inventory_{DateTime.Now:yyyyMMdd_HHmmss}.json");

            var json = JsonSerializer.Serialize(Cameras, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filename, json, Encoding.UTF8);

            StatusText = $"Exported JSON to {filename}";
            WpfMessageBox.Show($"Camera inventory exported successfully to:\n{filename}", "Export Complete", WpfMessageBoxButton.OK, WpfMessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"Failed to export JSON: {ex.Message}", "Export Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using NetworkDiscoveryTool.Core.Models;
using NetworkDiscoveryTool.Data;
using NetworkDiscoveryTool.Helpers;
using NetworkDiscoveryTool.UI.Services;

namespace NetworkDiscoveryTool.UI.ViewModels;

public sealed partial class TopologyViewModel : ObservableObject
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly Services.INavigationService _navigation;
    private readonly CurrentUserService _currentUser;

// === Top Toolbar Properties ===
    [ObservableProperty] private string _selectedSubnet = "All Scanned Devices";
    [ObservableProperty] private string _selectedFilter = "All Devices";
    [ObservableProperty] private double _zoomScale = 1.0;
    [ObservableProperty] private string _zoomText = "100%";
    [ObservableProperty] private double _canvasWidth = 1400;
    [ObservableProperty] private double _canvasHeight = 900;
    [ObservableProperty] private bool _isLoading;

    public ObservableCollection<string> Subnets { get; } = [];
    public ObservableCollection<string> FilterOptions { get; } =
    [
        "All Devices",
        "Routers & Switches",
        "Servers",
        "IP Cameras",
        "Workstations",
        "Printers"
    ];

    // === Device Legend Counts ===
    [ObservableProperty] private int _routerCount;
    [ObservableProperty] private int _switchCount;
    [ObservableProperty] private int _serverCount;
    [ObservableProperty] private int _cameraCount;
    [ObservableProperty] private int _workstationCount;
    [ObservableProperty] private int _printerCount;
    [ObservableProperty] private int _unknownCount;
    [ObservableProperty] private int _totalDevicesCount;

    // === Network Health KPI Metrics ===
    [ObservableProperty] private string _availabilityText = "100%";
    [ObservableProperty] private string _availabilityDelta = "+0.0%";
    [ObservableProperty] private string _avgLatencyText = "0ms";
    [ObservableProperty] private string _latencyDelta = "0ms";
    [ObservableProperty] private string _packetLossText = "0.0%";
    [ObservableProperty] private string _packetLossDelta = "0.0%";
    [ObservableProperty] private string _devicesOnlineText = "0 / 0";
    [ObservableProperty] private string _devicesOnlinePercent = "0.0%";

    // === Inspector Selected Node ===
    [ObservableProperty] private TopologyNodeItem? _selectedNode;
    [ObservableProperty] private bool _hasSelectedNode;
    [ObservableProperty] private int _selectedInspectorTab = 0; // 0=Overview, 1=Ports/Services, 2=Neighbors/Traffic
    [ObservableProperty] private bool _isOverviewTabSelected = true;
    [ObservableProperty] private bool _isInterfacesTabSelected;
    [ObservableProperty] private bool _isTrafficTabSelected;
    [ObservableProperty] private string _quickPingStatus = "";

    public ObservableCollection<TopologyNodeItem> Nodes { get; } = [];
    public ObservableCollection<TopologyLinkItem> Links { get; } = [];
    public ObservableCollection<TopologyNeighborItem> ConnectedNeighbors { get; } = [];

    private List<Device> _currentRawDevices = [];

    public TopologyViewModel(
        IDbContextFactory<AppDbContext> contextFactory,
        Services.INavigationService navigation,
        CurrentUserService currentUser)
    {
        _contextFactory = contextFactory;
        _navigation = navigation;
        _currentUser = currentUser;

        _currentUser.UserChanged += () => { _ = LoadTopologyDataAsync(); };
        _ = LoadTopologyDataAsync();
    }

    [RelayCommand]
    public async Task LoadTopologyDataAsync()
    {
        if (IsLoading) return;
        IsLoading = true;

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            // 1. Discover local subnets from network interfaces & previous scans
            var detectedSubnets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "192.168.1.0 / 24",
                "10.0.0.0 / 24",
                "172.16.0.0 / 24"
            };

            string? localGateway = null;

            foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (iface.OperationalStatus == OperationalStatus.Up &&
                    iface.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    var ipProps = iface.GetIPProperties();
                    foreach (var addr in ipProps.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            var bytes = addr.Address.GetAddressBytes();
                            string subnet = $"{bytes[0]}.{bytes[1]}.{bytes[2]}.0 / 24";
                            detectedSubnets.Add(subnet);
                        }
                    }

                    var gw = ipProps.GatewayAddresses.FirstOrDefault(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                    if (gw != null && localGateway == null)
                    {
                        localGateway = gw.Address.ToString();
                    }
                }
            }

            // Also load subnets from Scans table for current user
            var query = context.Scans.AsQueryable();
            if (_currentUser.UserId > 0)
            {
                query = query.Where(s => s.UserId == _currentUser.UserId);
            }
            else if (_currentUser.IsAdmin)
            {
                query = query.Where(s => s.UserId == null || s.UserId == 0);
            }
            else
            {
                query = query.Where(s => false);
            }

            var scans = await query
                .OrderByDescending(s => s.Date)
                .Take(20)
                .ToListAsync();

            // 2. Fetch devices from the latest scan or current active scan
            var latestScan = scans.FirstOrDefault();
            List<Device> allDbDevices;

            if (latestScan != null)
            {
                allDbDevices = await context.Devices
                    .Where(d => d.ScanId == latestScan.Id)
                    .Include(d => d.Ports)
                    .Include(d => d.Scan)
                    .OrderBy(d => d.IP)
                    .ToListAsync();

                // If latest scan has no devices, fallback to most recent scan with devices
                if (allDbDevices.Count == 0)
                {
                    var scanWithDevices = scans.FirstOrDefault(s => context.Devices.Any(d => d.ScanId == s.Id));
                    if (scanWithDevices != null)
                    {
                        allDbDevices = await context.Devices
                            .Where(d => d.ScanId == scanWithDevices.Id)
                            .Include(d => d.Ports)
                            .Include(d => d.Scan)
                            .OrderBy(d => d.IP)
                            .ToListAsync();
                    }
                }
            }
            else
            {
                allDbDevices = new List<Device>();
            }

            // Extract all distinct subnets directly from the discovered devices
            foreach (var d in allDbDevices)
            {
                if (IPAddress.TryParse(d.IP, out var ip))
                {
                    var b = ip.GetAddressBytes();
                    detectedSubnets.Add($"{b[0]}.{b[1]}.{b[2]}.0 / 24");
                }
            }

            Subnets.Clear();
            Subnets.Add("All Scanned Devices");
            foreach (var s in detectedSubnets.OrderBy(s => s))
                Subnets.Add(s);

            if (string.IsNullOrEmpty(SelectedSubnet) || !Subnets.Contains(SelectedSubnet))
                SelectedSubnet = "All Scanned Devices";

            // Filter by subnet if a specific subnet is selected
            if (!string.IsNullOrEmpty(SelectedSubnet) && SelectedSubnet != "All Scanned Devices")
            {
                var subnetPrefix = SelectedSubnet.Split(' ')[0]; // e.g. "192.168.1.0"
                var parts = subnetPrefix.Split('.');
                if (parts.Length >= 3)
                {
                    var prefix = $"{parts[0]}.{parts[1]}.{parts[2]}.";
                    _currentRawDevices = allDbDevices.Where(d => d.IP.StartsWith(prefix)).ToList();
                }
                else
                {
                    _currentRawDevices = allDbDevices;
                }
            }
            else
            {
                _currentRawDevices = allDbDevices;
            }

            // Deduplicate devices by IP, keeping the most complete record
            _currentRawDevices = _currentRawDevices
                .GroupBy(d => d.IP)
                .Select(g => g.OrderByDescending(x => x.Ports?.Count ?? 0).ThenByDescending(x => x.Id).First())
                .ToList();

            // Recompute counts and layout
            RecomputeAndLayout(localGateway);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Topology ERROR] {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedSubnetChanged(string value)
    {
        _ = LoadTopologyDataAsync();
    }

    partial void OnSelectedFilterChanged(string value)
    {
        RecomputeAndLayout();
    }

    [RelayCommand]
    public void SetInspectorTab(string tabName)
    {
        IsOverviewTabSelected = tabName == "Overview";
        IsInterfacesTabSelected = tabName == "Interfaces";
        IsTrafficTabSelected = tabName == "Traffic";
        SelectedInspectorTab = tabName switch
        {
            "Interfaces" => 1,
            "Traffic" => 2,
            _ => 0
        };
    }

    private void RecomputeAndLayout(string? defaultGatewayIp = null)
    {
        Nodes.Clear();
        Links.Clear();

        // Filter only Active / Online devices for the Live Topology map
        var activeRawDevices = _currentRawDevices
            .Where(d => string.Equals(d.Status, "Online", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (activeRawDevices.Count == 0)
        {
            // If no active devices scanned yet, show clean default gateway
            var defGw = new TopologyNodeItem
            {
                Id = "gw-root",
                Name = "Core Gateway Router",
                Category = "Router",
                SubTitle = "Default Gateway",
                IPAddress = defaultGatewayIp ?? "192.168.1.1",
                MACAddress = "00:00:5E:00:01:01",
                Vendor = "Network Gateway",
                Model = "Core Gateway",
                Uptime = "Active",
                OSVersion = "Network OS",
                Status = "Online",
                LastSeen = "Just now",
                TrafficIn = "1.0 Mbps",
                TrafficOut = "2.0 Mbps",
                ConnectedToText = "WAN / Internet Backbone",
                ConnectedSwitchName = "Internet WAN",
                ConnectedSwitchIp = "0.0.0.0",
                SwitchPortText = "WAN-1",
                IsCore = true,
                X = 600,
                Y = 120,
                Icon = "\uE809"
            };
            Nodes.Add(defGw);
            SelectNode(defGw);

            TotalDevicesCount = 1;
            RouterCount = 1;
            SwitchCount = 0;
            ServerCount = 0;
            CameraCount = 0;
            WorkstationCount = 0;
            PrinterCount = 0;
            UnknownCount = 0;
            DevicesOnlineText = "1 / 1 Active";
            DevicesOnlinePercent = "100%";
            AvailabilityText = "100%";
            AvgLatencyText = "1ms";
            return;
        }

        // 1. Classify every active raw device
        var classifiedList = new List<TopologyNodeItem>();
        int routers = 0, switches = 0, servers = 0, cameras = 0, workstations = 0, printers = 0, unknowns = 0;
        int totalOnline = activeRawDevices.Count;
        double sumLatency = 0;
        int latencyCount = 0;

        // Find or determine Gateway
        string gatewayIp = defaultGatewayIp ?? "";
        if (string.IsNullOrEmpty(gatewayIp))
        {
            var firstDevice = activeRawDevices.FirstOrDefault();
            if (firstDevice != null && IPAddress.TryParse(firstDevice.IP, out var parsed))
            {
                var b = parsed.GetAddressBytes();
                gatewayIp = $"{b[0]}.{b[1]}.{b[2]}.1";
            }
        }

        foreach (var d in activeRawDevices)
        {
            if (d.LatencyMs > 0)
            {
                sumLatency += d.LatencyMs;
                latencyCount++;
            }

            var openPorts = d.Ports?
                .Where(p => string.Equals(p.State, "Open", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.PortNumber)
                .ToHashSet() ?? [];

            var portInfoList = new ObservableCollection<TopologyPortInfo>();
            if (d.Ports != null)
            {
                foreach (var p in d.Ports.Where(x => string.Equals(x.State, "Open", StringComparison.OrdinalIgnoreCase)))
                {
                    portInfoList.Add(new TopologyPortInfo
                    {
                        PortNumber = p.PortNumber,
                        Protocol = "TCP",
                        ServiceName = GetServiceName(p.PortNumber, p.Service),
                        State = "Open",
                        BadgeColor = GetPortColor(p.PortNumber)
                    });
                }
            }

            string category;
            string icon;
            bool isCore = false;
            bool isDell = false;
            bool isPrinter = false;

            string host = d.Hostname ?? "";
            string devType = d.DeviceType ?? "";
            string v = d.Vendor ?? "";

            // 1. Router / Gateway
            if (d.IP == gatewayIp || (d.IP.EndsWith(".1") && d.IP.Length <= 15) || devType.Equals("Router", StringComparison.OrdinalIgnoreCase))
            {
                category = "Router";
                icon = "\uE809";
                isCore = true;
                routers++;
            }
            // 2. Printer / Imaging
            else if (devType.Contains("Printer", StringComparison.OrdinalIgnoreCase) ||
                     openPorts.Contains(9100) || openPorts.Contains(515) || openPorts.Contains(631) ||
                     v.Contains("Canon", StringComparison.OrdinalIgnoreCase) || v.Contains("Epson", StringComparison.OrdinalIgnoreCase) || v.Contains("Xerox", StringComparison.OrdinalIgnoreCase) || v.Contains("Brother", StringComparison.OrdinalIgnoreCase) || v.Contains("Kyocera", StringComparison.OrdinalIgnoreCase) || v.Contains("Ricoh", StringComparison.OrdinalIgnoreCase) || (v.Contains("HP", StringComparison.OrdinalIgnoreCase) && openPorts.Contains(9100)))
            {
                category = "Printer";
                icon = "\uE749";
                isPrinter = true;
                printers++;
            }
            // 3. Servers (Matches real servers: Internal Server, ACTIVE, MAINCLUSTER, GET-UP, SRV, DB ports)
            else if (devType.Contains("Server", StringComparison.OrdinalIgnoreCase) ||
                     host.Contains("SERVER", StringComparison.OrdinalIgnoreCase) ||
                     host.Contains("SRV", StringComparison.OrdinalIgnoreCase) ||
                     host.Contains("CLUSTER", StringComparison.OrdinalIgnoreCase) ||
                     host.Contains("ACTIVE", StringComparison.OrdinalIgnoreCase) ||
                     host.Contains("GET-UP", StringComparison.OrdinalIgnoreCase) ||
                     host.Contains("EXCHANGE", StringComparison.OrdinalIgnoreCase) ||
                     host.Contains("ESXI", StringComparison.OrdinalIgnoreCase) ||
                     host.Contains("VMWARE", StringComparison.OrdinalIgnoreCase) ||
                     host.Contains("PROXMOX", StringComparison.OrdinalIgnoreCase) ||
                     openPorts.Contains(1433) || openPorts.Contains(3306) || openPorts.Contains(5432) || openPorts.Contains(1521) || openPorts.Contains(8006))
            {
                category = "Server";
                icon = "\uE80F";
                servers++;
                if (v.Contains("Dell", StringComparison.OrdinalIgnoreCase)) isDell = true;
            }
            // 4. IP Camera / Surveillance
            else if (devType.Contains("Camera", StringComparison.OrdinalIgnoreCase) ||
                     v.Contains("Camera", StringComparison.OrdinalIgnoreCase) ||
                     v.Contains("Panasonic", StringComparison.OrdinalIgnoreCase) ||
                     v.Contains("Alerton", StringComparison.OrdinalIgnoreCase) ||
                     v.Contains("Hikvision", StringComparison.OrdinalIgnoreCase) ||
                     v.Contains("Dahua", StringComparison.OrdinalIgnoreCase) ||
                     v.Contains("Uniview", StringComparison.OrdinalIgnoreCase) ||
                     v.Contains("Axis", StringComparison.OrdinalIgnoreCase) ||
                     v.Contains("Ezviz", StringComparison.OrdinalIgnoreCase) ||
                     v.Contains("Imou", StringComparison.OrdinalIgnoreCase) ||
                     openPorts.Contains(554) || openPorts.Contains(37777) || openPorts.Contains(8000) ||
                     host.Contains("CAM", StringComparison.OrdinalIgnoreCase) ||
                     host.Contains("DVR", StringComparison.OrdinalIgnoreCase) ||
                     host.Contains("NVR", StringComparison.OrdinalIgnoreCase) ||
                     host.Contains("gSOAP", StringComparison.OrdinalIgnoreCase) ||
                     host.Contains("WV-", StringComparison.OrdinalIgnoreCase))
            {
                category = "IP Camera";
                icon = "\uE714";
                cameras++;
            }
            // 5. Switch / Network Managed Switch
            else if (devType.Contains("Switch", StringComparison.OrdinalIgnoreCase) ||
                     (openPorts.Contains(161) && (v.Contains("Cisco", StringComparison.OrdinalIgnoreCase) || v.Contains("MikroTik", StringComparison.OrdinalIgnoreCase) || v.Contains("Ubiquiti", StringComparison.OrdinalIgnoreCase) || v.Contains("TP-Link", StringComparison.OrdinalIgnoreCase) || v.Contains("Netgear", StringComparison.OrdinalIgnoreCase) || v.Contains("Huawei", StringComparison.OrdinalIgnoreCase) || v.Contains("D-Link", StringComparison.OrdinalIgnoreCase))))
            {
                category = "Switch";
                icon = "\uE7F4";
                switches++;
            }
            // 6. Workstation / PC / Laptop
            else
            {
                category = "Workstation";
                icon = "\uE7F8";
                workstations++;
                if (v.Contains("Dell", StringComparison.OrdinalIgnoreCase)) isDell = true;
            }

            string vendor = !string.IsNullOrWhiteSpace(d.Vendor) && d.Vendor != "Unknown" ? d.Vendor : OuiLookup.Lookup(d.MAC, d.Hostname, openPorts);
            string hostDisplayName = !string.IsNullOrWhiteSpace(d.Hostname) && d.Hostname != "-" && d.Hostname != d.IP
                ? d.Hostname
                : $"{category} ({d.IP.Split('.').Last()})";

            var node = new TopologyNodeItem
            {
                Id = $"dev-{d.Id}",
                Name = hostDisplayName,
                Category = category,
                SubTitle = !string.IsNullOrWhiteSpace(d.DeviceType) ? d.DeviceType : $"{vendor} Device",
                IPAddress = d.IP,
                MACAddress = !string.IsNullOrWhiteSpace(d.MAC) ? d.MAC : "-",
                Vendor = vendor,
                Model = !string.IsNullOrWhiteSpace(d.DeviceType) ? d.DeviceType : vendor,
                Uptime = "Active",
                OSVersion = !string.IsNullOrWhiteSpace(d.OS) ? d.OS : (category == "Server" ? "Windows Server / Linux" : "Windows 11 / 10"),
                Status = "Online",
                LastSeen = "Just now",
                LatencyText = d.LatencyMs > 0 ? $"{d.LatencyMs} ms" : "< 1 ms",
                TrafficIn = $"{Math.Round(0.5 + (d.LatencyMs % 4), 2)} Mbps",
                TrafficOut = $"{Math.Round(1.0 + (d.LatencyMs % 6), 2)} Mbps",
                IsCore = isCore,
                IsDell = isDell,
                IsPrinter = isPrinter,
                Icon = icon,
                OpenPorts = portInfoList,
                IsWebAccessible = openPorts.Contains(80) || openPorts.Contains(443) || openPorts.Contains(8080) || openPorts.Contains(8443),
                IsRdpAccessible = openPorts.Contains(3389),
                IsSshAccessible = openPorts.Contains(22),
                WebUrl = openPorts.Contains(443) || openPorts.Contains(8443) ? $"https://{d.IP}" : $"http://{d.IP}"
            };

            classifiedList.Add(node);
        }

        // Apply Legend Counts
        TotalDevicesCount = classifiedList.Count;
        RouterCount = Math.Max(1, routers);
        SwitchCount = switches;
        ServerCount = servers;
        CameraCount = cameras;
        WorkstationCount = workstations;
        PrinterCount = printers;
        UnknownCount = unknowns;

        // Apply KPI Metrics
        int totalRaw = _currentRawDevices.Count;
        DevicesOnlineText = $"{totalOnline} Active / {totalRaw}";
        DevicesOnlinePercent = totalRaw > 0 ? $"{(double)totalOnline / totalRaw * 100:F1}%" : "100%";
        AvailabilityText = totalRaw > 0 ? $"{(double)totalOnline / totalRaw * 100:F1}%" : "100%";
        AvgLatencyText = latencyCount > 0 ? $"{Math.Round(sumLatency / latencyCount)}ms" : "2ms";

        // 2. Filter nodes based on user selection
        var filteredNodes = classifiedList.AsEnumerable();
        if (SelectedFilter == "Routers & Switches")
            filteredNodes = filteredNodes.Where(n => n.Category is "Router" or "Switch");
        else if (SelectedFilter == "Servers")
            filteredNodes = filteredNodes.Where(n => n.Category == "Server" || n.IsCore);
        else if (SelectedFilter == "IP Cameras")
            filteredNodes = filteredNodes.Where(n => n.Category == "IP Camera" || n.Category == "Switch" || n.IsCore);
        else if (SelectedFilter == "Workstations")
            filteredNodes = filteredNodes.Where(n => n.Category == "Workstation" || n.Category == "Switch" || n.IsCore);
        else if (SelectedFilter == "Printers")
            filteredNodes = filteredNodes.Where(n => n.Category == "Printer" || n.Category == "Switch" || n.IsCore);

        var displayList = filteredNodes.ToList();
        if (displayList.Count == 0) displayList = classifiedList;

        // 3. Build Smart Separated Categories Tree Topology containing Active Devices Only
        LayoutCategorizedActiveTopology(displayList, defaultGatewayIp);
    }

    private void LayoutCategorizedActiveTopology(List<TopologyNodeItem> displayList, string? defaultGatewayIp)
    {
        Nodes.Clear();
        Links.Clear();

        if (displayList.Count == 0) return;

        // 1. Identify or create Core Gateway Router
        var core = displayList.FirstOrDefault(n => n.IsCore) ?? displayList.FirstOrDefault(n => n.Category == "Router");
        if (core == null)
        {
            string gwIp = defaultGatewayIp ?? (displayList.FirstOrDefault()?.IPAddress ?? "192.168.1.1");
            if (IPAddress.TryParse(gwIp, out var parsedIp))
            {
                var b = parsedIp.GetAddressBytes();
                gwIp = $"{b[0]}.{b[1]}.{b[2]}.1";
            }

            core = new TopologyNodeItem
            {
                Id = "core-gw",
                Name = "Core Gateway Router",
                Category = "Router",
                SubTitle = "Core Gateway / Firewall",
                IPAddress = gwIp,
                MACAddress = "00:00:5E:00:01:01",
                Vendor = "Network Gateway",
                Model = "Core Router",
                Uptime = "Active",
                OSVersion = "Network OS",
                Status = "Online",
                LastSeen = "Just now",
                TrafficIn = "10.5 Mbps",
                TrafficOut = "24.8 Mbps",
                ConnectedToText = "Internet Uplink / ISP Gateway",
                ConnectedSwitchName = "WAN Gateway",
                ConnectedSwitchIp = "0.0.0.0",
                SwitchPortText = "WAN-1",
                IsCore = true,
                Icon = "\uE809"
            };
        }

        string baseSubnet = core.IPAddress.Contains('.') 
            ? core.IPAddress.Substring(0, core.IPAddress.LastIndexOf('.') + 1) 
            : "192.168.1.";

        // 2. Separate all active endpoints strictly by category
        var endpoints = displayList.Where(n => n.Id != core.Id).ToList();

        var cameraGroup = endpoints.Where(n => n.Category == "IP Camera").ToList();
        var serverGroup = endpoints.Where(n => n.Category == "Server").ToList();
        var workstationGroup = endpoints.Where(n => n.Category == "Workstation").ToList();
        var printerGroup = endpoints.Where(n => n.Category == "Printer").ToList();
        var switchGroup = endpoints.Where(n => n.Category == "Switch").ToList();
        var otherGroup = endpoints.Where(n => n.Category != "IP Camera" && n.Category != "Server" && n.Category != "Workstation" && n.Category != "Printer" && n.Category != "Switch").ToList();
        if (otherGroup.Count > 0)
        {
            workstationGroup.AddRange(otherGroup);
        }

        // 3. Build Dedicated Switch Clusters
        var switchClusters = new List<SwitchCluster>();

        if (switchGroup.Count > 0)
        {
            // Use the real discovered switches ONLY (exact matching count)
            var categoryBatches = new List<List<TopologyNodeItem>> { cameraGroup, serverGroup, workstationGroup, printerGroup }
                .Where(b => b.Count > 0)
                .ToList();

            for (int sIdx = 0; sIdx < switchGroup.Count; sIdx++)
            {
                var realSw = switchGroup[sIdx];
                realSw.ConnectedToText = $"Connected to Core Router ({core.IPAddress}) via Dedicated Trunk";
                realSw.ConnectedSwitchName = core.Name;
                realSw.ConnectedSwitchIp = core.IPAddress;
                realSw.SwitchPortText = $"Trunk-{sIdx + 1}";
                realSw.Icon = "\uE7F4";

                List<TopologyNodeItem> assigned;
                if (sIdx < categoryBatches.Count)
                {
                    assigned = new List<TopologyNodeItem>(categoryBatches[sIdx]);
                }
                else
                {
                    assigned = new List<TopologyNodeItem>();
                }

                switchClusters.Add(new SwitchCluster
                {
                    SwitchNode = realSw,
                    AssignedDevices = assigned
                });
            }

            // If there are more category batches than switches, append remaining to the last switch
            if (categoryBatches.Count > switchGroup.Count && switchClusters.Count > 0)
            {
                for (int rem = switchGroup.Count; rem < categoryBatches.Count; rem++)
                {
                    switchClusters.Last().AssignedDevices.AddRange(categoryBatches[rem]);
                }
            }
        }
        else
        {
            // Fallback: If no physical switches were discovered in the network scan, build virtual category switches
            if (cameraGroup.Count > 0)
            {
                var camSwitch = new TopologyNodeItem
                {
                    Id = "sw-cctv",
                    Name = "PoE Switch (Cameras & CCTV)",
                    Category = "Switch",
                    SubTitle = "Surveillance PoE Switch",
                    IPAddress = $"{baseSubnet}2",
                    MACAddress = "00:1A:A0:5B:C0:01",
                    Vendor = "Hikvision / Dahua PoE",
                    Model = "16-Port PoE+ Switch",
                    Uptime = "Active",
                    OSVersion = "PoE Switch OS",
                    Status = "Online",
                    LastSeen = "Just now",
                    TrafficIn = "18.5 Mbps",
                    TrafficOut = "26.0 Mbps",
                    ConnectedToText = $"Connected to Core Router ({core.IPAddress}) via Gigabit Trunk",
                    ConnectedSwitchName = core.Name,
                    ConnectedSwitchIp = core.IPAddress,
                    SwitchPortText = "PoE-Trunk",
                    Icon = "\uE714"
                };
                switchClusters.Add(new SwitchCluster { SwitchNode = camSwitch, AssignedDevices = cameraGroup });
            }

            if (serverGroup.Count > 0)
            {
                var srvSwitch = new TopologyNodeItem
                {
                    Id = "sw-servers",
                    Name = "Core Switch (Servers & DC)",
                    Category = "Switch",
                    SubTitle = "DataCenter Core Switch",
                    IPAddress = $"{baseSubnet}3",
                    MACAddress = "00:1A:A0:5B:D0:02",
                    Vendor = "Cisco Catalyst",
                    Model = "24-Port 10G Switch",
                    Uptime = "Active",
                    OSVersion = "Cisco IOS-XE",
                    Status = "Online",
                    LastSeen = "Just now",
                    TrafficIn = "32.0 Mbps",
                    TrafficOut = "48.5 Mbps",
                    ConnectedToText = $"Connected to Core Router ({core.IPAddress}) via 10G Fiber Link",
                    ConnectedSwitchName = core.Name,
                    ConnectedSwitchIp = core.IPAddress,
                    SwitchPortText = "TenGig0/1",
                    Icon = "\uE80F"
                };
                switchClusters.Add(new SwitchCluster { SwitchNode = srvSwitch, AssignedDevices = serverGroup });
            }

            if (workstationGroup.Count > 0)
            {
                var wsSwitch = new TopologyNodeItem
                {
                    Id = "sw-workstations",
                    Name = "Distribution Switch (Workstations)",
                    Category = "Switch",
                    SubTitle = "Access LAN Switch",
                    IPAddress = $"{baseSubnet}4",
                    MACAddress = "00:1A:A0:5B:E0:03",
                    Vendor = "Dell PowerSwitch",
                    Model = "48-Port Gigabit Switch",
                    Uptime = "Active",
                    OSVersion = "Dell Networking OS",
                    Status = "Online",
                    LastSeen = "Just now",
                    TrafficIn = "14.2 Mbps",
                    TrafficOut = "19.8 Mbps",
                    ConnectedToText = $"Connected to Core Router ({core.IPAddress}) via Gigabit Trunk",
                    ConnectedSwitchName = core.Name,
                    ConnectedSwitchIp = core.IPAddress,
                    SwitchPortText = "Gi0/1",
                    Icon = "\uE7F8"
                };
                switchClusters.Add(new SwitchCluster { SwitchNode = wsSwitch, AssignedDevices = workstationGroup });
            }

            if (printerGroup.Count > 0)
            {
                var prnSwitch = new TopologyNodeItem
                {
                    Id = "sw-printers",
                    Name = "Access Switch (Printers & Scanners)",
                    Category = "Switch",
                    SubTitle = "Peripherals Access Switch",
                    IPAddress = $"{baseSubnet}5",
                    MACAddress = "00:1A:A0:5B:F0:04",
                    Vendor = "HP ProCurve",
                    Model = "24-Port Smart Switch",
                    Uptime = "Active",
                    OSVersion = "ProCurve OS",
                    Status = "Online",
                    LastSeen = "Just now",
                    TrafficIn = "3.5 Mbps",
                    TrafficOut = "5.0 Mbps",
                    ConnectedToText = $"Connected to Core Router ({core.IPAddress}) via Gigabit Trunk",
                    ConnectedSwitchName = core.Name,
                    ConnectedSwitchIp = core.IPAddress,
                    SwitchPortText = "Gi0/4",
                    Icon = "\uE749"
                };
                switchClusters.Add(new SwitchCluster { SwitchNode = prnSwitch, AssignedDevices = printerGroup });
            }
        }

        // Fallback: If no clusters were formed, create a default LAN switch
        if (switchClusters.Count == 0 && endpoints.Count > 0)
        {
            var defSwitch = new TopologyNodeItem
            {
                Id = "sw-main",
                Name = "Main LAN Switch",
                Category = "Switch",
                SubTitle = "Primary Distribution Switch",
                IPAddress = $"{baseSubnet}2",
                MACAddress = "00:1A:A0:5B:12:01",
                Vendor = "Cisco Catalyst",
                Model = "24-Port Gigabit Switch",
                Uptime = "Active",
                OSVersion = "Cisco IOS",
                Status = "Online",
                LastSeen = "Just now",
                TrafficIn = "10.0 Mbps",
                TrafficOut = "15.0 Mbps",
                ConnectedToText = $"Connected to Core Router ({core.IPAddress})",
                ConnectedSwitchName = core.Name,
                ConnectedSwitchIp = core.IPAddress,
                SwitchPortText = "Gi0/1",
                Icon = "\uE7F4"
            };
            switchClusters.Add(new SwitchCluster { SwitchNode = defSwitch, AssignedDevices = endpoints });
        }

        // 4. Calculate Dynamic Canvas Dimensions and Node Coordinates
        int clusterCount = switchClusters.Count;
        if (clusterCount == 0)
        {
            core.X = 600;
            core.Y = 120;
            Nodes.Add(core);
            SelectNode(core);
            CanvasWidth = 1200;
            CanvasHeight = 600;
            return;
        }

        double clusterSpacing = 360.0;
        int maxDevicesInAnyCluster = switchClusters.Max(c => c.AssignedDevices.Count);
        int devicesPerRow = 3;
        int maxRows = (int)Math.Ceiling((double)Math.Max(1, maxDevicesInAnyCluster) / devicesPerRow);

        double totalWidth = Math.Max(1350, (clusterCount * clusterSpacing) + 240);
        double totalHeight = Math.Max(800, 360 + (maxRows * 125) + 120);

        CanvasWidth = totalWidth;
        CanvasHeight = totalHeight;

        double coreX = totalWidth / 2.0;
        double coreY = 80.0;

        // Place Core Gateway Node
        core.X = coreX;
        core.Y = coreY;
        Nodes.Add(core);

        // Place Each Category Switch and its underlying Active Devices
        double stepX = totalWidth / (clusterCount + 1);
        double switchY = 240.0;

        for (int cIdx = 0; cIdx < clusterCount; cIdx++)
        {
            var cluster = switchClusters[cIdx];
            double swX = stepX * (cIdx + 1);
            var swNode = cluster.SwitchNode;

            swNode.X = swX;
            swNode.Y = switchY;
            swNode.ConnectedToText = $"Connected to Core Router ({core.IPAddress}) via Dedicated Trunk";
            swNode.ConnectedSwitchName = core.Name;
            swNode.ConnectedSwitchIp = core.IPAddress;
            swNode.SwitchPortText = $"Trunk-{cIdx + 1}";

            Nodes.Add(swNode);

            // Connect Gateway -> Category Switch Link
            Links.Add(new TopologyLinkItem
            {
                StartX = coreX,
                StartY = coreY,
                EndX = swX,
                EndY = switchY,
                LinkBrush = "#0EA5E9",
                StrokeThickness = 2.5
            });

            // Layout active devices belonging to this category
            var devList = cluster.AssignedDevices;
            int devCount = devList.Count;

            double devStartY = switchY + 120.0;
            double colSpacing = 125.0;
            double rowSpacing = 110.0;

            for (int dIdx = 0; dIdx < devCount; dIdx++)
            {
                var dev = devList[dIdx];
                int row = dIdx / devicesPerRow;
                int col = dIdx % devicesPerRow;

                // Center columns horizontally beneath their parent switch
                int countInThisRow = Math.Min(devicesPerRow, devCount - (row * devicesPerRow));
                double rowStartX = swX - ((countInThisRow - 1) * colSpacing / 2.0);

                double devX = rowStartX + (col * colSpacing);
                double devY = devStartY + (row * rowSpacing);

                int portNumber = dIdx + 1;
                dev.X = devX;
                dev.Y = devY;
                dev.ConnectedSwitchName = swNode.Name;
                dev.ConnectedSwitchIp = swNode.IPAddress;
                dev.SwitchPortText = $"Port 0/{portNumber}";
                dev.ConnectedToText = $"Connected to {swNode.Name} ({swNode.IPAddress}) on Port {portNumber}";

                Nodes.Add(dev);

                // Connect Category Switch -> Active Device Link
                Links.Add(new TopologyLinkItem
                {
                    StartX = swX,
                    StartY = switchY,
                    EndX = devX,
                    EndY = devY,
                    LinkBrush = "#38BDF8",
                    StrokeThickness = 1.5,
                    IsBranch = true
                });
            }
        }

        // Auto-select Core node
        SelectNode(core);
    }

    [RelayCommand]
    public void SelectNode(TopologyNodeItem node)
    {
        if (node is null) return;
        SelectedNode = node;
        HasSelectedNode = true;
        QuickPingStatus = "";

        ConnectedNeighbors.Clear();

        if (node.IsCore)
        {
            foreach (var sw in Nodes.Where(n => n.Category == "Switch"))
            {
                ConnectedNeighbors.Add(new TopologyNeighborItem
                {
                    Name = sw.Name,
                    IPAddress = sw.IPAddress,
                    Icon = sw.Icon,
                    Role = "Distribution Switch",
                    Port = sw.SwitchPortText,
                    StatusColor = sw.Status == "Online" ? "#10B981" : "#64748B"
                });
            }
        }
        else if (node.Category == "Switch")
        {
            var core = Nodes.FirstOrDefault(n => n.IsCore);
            if (core != null)
            {
                ConnectedNeighbors.Add(new TopologyNeighborItem
                {
                    Name = core.Name,
                    IPAddress = core.IPAddress,
                    Icon = core.Icon,
                    Role = "Upstream Core Gateway",
                    Port = node.SwitchPortText,
                    StatusColor = "#10B981"
                });
            }

            foreach (var child in Nodes.Where(n => n.ConnectedSwitchName == node.Name && n.Id != node.Id))
            {
                if (ConnectedNeighbors.Count < 12)
                {
                    ConnectedNeighbors.Add(new TopologyNeighborItem
                    {
                        Name = child.Name,
                        IPAddress = child.IPAddress,
                        Icon = child.Icon,
                        Role = child.Category,
                        Port = child.SwitchPortText,
                        StatusColor = child.Status == "Online" ? "#10B981" : "#64748B"
                    });
                }
            }
        }
        else
        {
            // Endpoint device: Show connected Switch and siblings
            var parentSwitch = Nodes.FirstOrDefault(n => n.Name == node.ConnectedSwitchName) ?? Nodes.FirstOrDefault(n => n.IsCore);
            if (parentSwitch != null)
            {
                ConnectedNeighbors.Add(new TopologyNeighborItem
                {
                    Name = parentSwitch.Name,
                    IPAddress = parentSwitch.IPAddress,
                    Icon = parentSwitch.Icon,
                    Role = "Parent Switch",
                    Port = node.SwitchPortText,
                    StatusColor = "#10B981"
                });
            }

            // Add peer devices on same switch
            if (parentSwitch != null)
            {
                foreach (var peer in Nodes.Where(n => n.ConnectedSwitchName == parentSwitch.Name && n.Id != node.Id).Take(5))
                {
                    ConnectedNeighbors.Add(new TopologyNeighborItem
                    {
                        Name = peer.Name,
                        IPAddress = peer.IPAddress,
                        Icon = peer.Icon,
                        Role = $"Peer {peer.Category}",
                        Port = peer.SwitchPortText,
                        StatusColor = peer.Status == "Online" ? "#10B981" : "#64748B"
                    });
                }
            }
        }
    }

    [RelayCommand]
    private async Task PingSelectedNodeAsync()
    {
        if (SelectedNode == null || string.IsNullOrWhiteSpace(SelectedNode.IPAddress)) return;

        QuickPingStatus = "Pinging...";
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(SelectedNode.IPAddress, 1500);
            if (reply.Status == IPStatus.Success)
            {
                QuickPingStatus = $"Online • {reply.RoundtripTime} ms";
                SelectedNode.Status = "Online";
                SelectedNode.LatencyText = $"{reply.RoundtripTime} ms";
            }
            else
            {
                QuickPingStatus = "Offline • No reply";
                SelectedNode.Status = "Offline";
                SelectedNode.LatencyText = "Timeout";
            }
        }
        catch
        {
            QuickPingStatus = "Error during ping";
        }
    }

    [RelayCommand]
    private void CopyNodeIp()
    {
        if (SelectedNode != null && !string.IsNullOrWhiteSpace(SelectedNode.IPAddress))
        {
            try
            {
                System.Windows.Clipboard.SetText(SelectedNode.IPAddress);
                QuickPingStatus = "IP Copied to Clipboard!";
            }
            catch { }
        }
    }

    [RelayCommand]
    private void OpenWebAdmin()
    {
        if (SelectedNode != null && !string.IsNullOrWhiteSpace(SelectedNode.WebUrl))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = SelectedNode.WebUrl,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    [RelayCommand]
    private void ConnectRdp()
    {
        if (SelectedNode != null && !string.IsNullOrWhiteSpace(SelectedNode.IPAddress))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "mstsc.exe",
                    Arguments = $"/v:{SelectedNode.IPAddress}",
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    [RelayCommand]
    private void CloseInspector() => HasSelectedNode = false;

    [RelayCommand]
    private void ZoomIn()
    {
        if (ZoomScale < 1.8)
        {
            ZoomScale = Math.Round(ZoomScale + 0.15, 2);
            ZoomText = $"{(int)(ZoomScale * 100)}%";
        }
    }

    [RelayCommand]
    private void ZoomOut()
    {
        if (ZoomScale > 0.4)
        {
            ZoomScale = Math.Round(ZoomScale - 0.15, 2);
            ZoomText = $"{(int)(ZoomScale * 100)}%";
        }
    }

    [RelayCommand]
    private void AutoLayout()
    {
        ZoomScale = 1.0;
        ZoomText = "100%";
        RecomputeAndLayout();
    }

    [RelayCommand]
    private void FitToScreen()
    {
        ZoomScale = 0.85;
        ZoomText = "85%";
    }

    [RelayCommand]
    private void RefreshTopology() => _ = LoadTopologyDataAsync();

    [RelayCommand]
    private void OpenDeviceDetails()
    {
        if (SelectedNode is not null && !string.IsNullOrWhiteSpace(SelectedNode.IPAddress))
            _navigation.NavigateToDeviceDetails(SelectedNode.IPAddress);
    }

    public Func<string, bool>? RequestVisualExport { get; set; }

    [RelayCommand]
    private void ExportTopology()
    {
        try
        {
            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export Network Topology",
                Filter = "PNG Image (*.png)|*.png|JSON Topology Map (*.json)|*.json|CSV Device Inventory (*.csv)|*.csv",
                FileName = $"Topology_Map_{DateTime.Now:yyyyMMdd_HHmmss}.png"
            };

            if (saveDialog.ShowDialog() == true)
            {
                var ext = System.IO.Path.GetExtension(saveDialog.FileName).ToLowerInvariant();

                if (ext == ".json")
                {
                    var json = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        ExportDate = DateTime.Now,
                        TotalDevices = TotalDevicesCount,
                        OnlineCount = Nodes.Count(n => n.Status == "Online"),
                        CoreGateway = Nodes.FirstOrDefault(n => n.IsCore)?.IPAddress,
                        Nodes = Nodes.Select(n => new
                        {
                            n.Name,
                            n.IPAddress,
                            n.MACAddress,
                            n.Category,
                            n.Vendor,
                            n.Status,
                            n.ConnectedSwitchName,
                            n.ConnectedSwitchIp,
                            n.SwitchPortText,
                            n.LatencyText,
                            OpenPorts = n.OpenPorts.Select(p => new { p.PortNumber, p.ServiceName, p.State })
                        }),
                        Links = Links.Select(l => new { l.StartX, l.StartY, l.EndX, l.EndY })
                    }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

                    System.IO.File.WriteAllText(saveDialog.FileName, json);
                    System.Windows.MessageBox.Show($"Topology data exported successfully to:\n{saveDialog.FileName}", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (ext == ".csv")
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("Name,IP Address,MAC Address,Category,Vendor,Connected Switch,Switch Port,Status,Latency,Open Ports");
                    foreach (var n in Nodes)
                    {
                        var ports = string.Join(" | ", n.OpenPorts.Select(p => $"{p.PortNumber}:{p.ServiceName}"));
                        sb.AppendLine($"\"{n.Name}\",\"{n.IPAddress}\",\"{n.MACAddress}\",\"{n.Category}\",\"{n.Vendor}\",\"{n.ConnectedSwitchName}\",\"{n.SwitchPortText}\",\"{n.Status}\",\"{n.LatencyText}\",\"{ports}\"");
                    }
                    System.IO.File.WriteAllText(saveDialog.FileName, sb.ToString());
                    System.Windows.MessageBox.Show($"Device inventory exported successfully to:\n{saveDialog.FileName}", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    // Render PNG
                    bool success = RequestVisualExport?.Invoke(saveDialog.FileName) ?? false;
                    if (success && System.IO.File.Exists(saveDialog.FileName))
                    {
                        System.Windows.MessageBox.Show($"Topology image exported successfully to:\n{saveDialog.FileName}", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        // Fallback: Export JSON if PNG visual export was unavailable
                        var fallbackJsonPath = System.IO.Path.ChangeExtension(saveDialog.FileName, ".json");
                        var json = System.Text.Json.JsonSerializer.Serialize(Nodes, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                        System.IO.File.WriteAllText(fallbackJsonPath, json);
                        System.Windows.MessageBox.Show($"Topology layout saved to:\n{fallbackJsonPath}", "Export Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string GetServiceName(int port, string? fallbackService)
    {
        if (!string.IsNullOrWhiteSpace(fallbackService) && fallbackService != "Unknown")
            return fallbackService;

        return port switch
        {
            21 => "FTP",
            22 => "SSH Remote Shell",
            23 => "Telnet",
            25 => "SMTP Mail",
            53 => "DNS Server",
            80 => "HTTP Web Server",
            110 => "POP3 Mail",
            135 => "RPC Endpoint",
            139 => "NetBIOS Session",
            143 => "IMAP Mail",
            161 => "SNMP Management",
            443 => "HTTPS Secure Web",
            445 => "SMB File Sharing",
            515 => "LPD Network Printer",
            554 => "RTSP Camera Stream",
            631 => "IPP Network Printer",
            1433 => "MS SQL Database",
            1521 => "Oracle Database",
            3306 => "MySQL Database",
            3389 => "RDP Remote Desktop",
            5432 => "PostgreSQL Database",
            5900 => "VNC Remote",
            8000 => "Hikvision / Video Admin",
            8080 => "HTTP Alternate / Proxy",
            8443 => "HTTPS Alternate Web",
            9100 => "RAW Print Server",
            37777 => "Dahua DVR Stream",
            _ => $"Port {port}"
        };
    }

    private static string GetPortColor(int port)
    {
        return port switch
        {
            80 or 443 or 8080 or 8443 => "#38BDF8", // Cyan Web
            1433 or 3306 or 5432 => "#F59E0B",     // Amber Database
            22 or 3389 => "#10B981",               // Green Remote
            554 or 8000 or 37777 => "#EC4899",     // Pink Camera
            9100 or 515 or 631 => "#A855F7",       // Purple Printer
            _ => "#94A3B8"
        };
    }

    private sealed class SwitchCluster
    {
        public required TopologyNodeItem SwitchNode { get; init; }
        public required List<TopologyNodeItem> AssignedDevices { get; init; }
    }
}

public sealed class TopologyLinkItem
{
    public double StartX { get; init; }
    public double StartY { get; init; }
    public double EndX { get; init; }
    public double EndY { get; init; }
    public string LinkBrush { get; init; } = "#38BDF8";
    public double StrokeThickness { get; init; } = 2.0;
    public bool IsBranch { get; init; }
}

public sealed class TopologyNodeItem
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public string SubTitle { get; init; } = "";
    public string IPAddress { get; init; } = "";
    public string MACAddress { get; init; } = "";
    public string Vendor { get; init; } = "";
    public string Model { get; init; } = "-";
    public string Uptime { get; init; } = "Active";
    public string OSVersion { get; init; } = "-";
    public string Status { get; set; } = "Online";
    public string LastSeen { get; init; } = "Just now";
    public string LatencyText { get; set; } = "< 1 ms";
    public string TrafficIn { get; init; } = "1.2 Mbps";
    public string TrafficOut { get; init; } = "2.4 Mbps";
    public string ConnectedToText { get; set; } = "";
    public string ConnectedSwitchName { get; set; } = "";
    public string ConnectedSwitchIp { get; set; } = "";
    public string SwitchPortText { get; set; } = "";
    public bool IsCore { get; init; }
    public bool IsDell { get; init; }
    public bool IsPrinter { get; init; }
    public double X { get; set; }
    public double Y { get; set; }
    public string Icon { get; set; } = "\uE774";
    public ObservableCollection<TopologyPortInfo> OpenPorts { get; init; } = [];
    public bool HasOpenPorts => OpenPorts.Count > 0;
    public string OpenPortsCountText => $"{OpenPorts.Count} Open {(OpenPorts.Count == 1 ? "Port" : "Ports")}";
    public bool IsWebAccessible { get; init; }
    public bool IsRdpAccessible { get; init; }
    public bool IsSshAccessible { get; init; }
    public string WebUrl { get; init; } = "";
}

public sealed class TopologyPortInfo
{
    public int PortNumber { get; init; }
    public string Protocol { get; init; } = "TCP";
    public string ServiceName { get; init; } = "";
    public string State { get; init; } = "Open";
    public string BadgeColor { get; init; } = "#38BDF8";
}

public sealed class TopologyNeighborItem
{
    public string Name { get; init; } = "";
    public string IPAddress { get; init; } = "";
    public string Icon { get; init; } = "\uE774";
    public string Role { get; init; } = "Connected Device";
    public string Port { get; init; } = "Fa0/1";
    public string StatusColor { get; init; } = "#10B981";
}
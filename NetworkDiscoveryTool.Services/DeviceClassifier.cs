using System;
using System.Collections.Generic;
using System.Linq;
using NetworkDiscoveryTool.Core.Interfaces;
using NetworkDiscoveryTool.Core.Models;

namespace NetworkDiscoveryTool.Services;

public sealed class DeviceClassifier : IDeviceClassifier
{
    private static readonly HashSet<int> CameraPorts = [554, 8554, 37777, 8899];
    private static readonly HashSet<int> PrinterPorts = [515, 631, 9100];
    private static readonly HashSet<int> ServerPorts = [1433, 1521, 3306, 5432, 8006, 389, 636, 88];
    private static readonly HashSet<int> WindowsWorkstationPorts = [135, 139, 445, 3389];

    public string Classify(Device device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return Classify(device.Ports, device.Vendor, device.Hostname, null, device.IP);
    }

    public string Classify(IEnumerable<Port>? ports, string? vendor, string? hostname, string? httpBanner, string? ip)
    {
        var open = ports?
            .Where(p => string.Equals(p.State, "Open", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.PortNumber)
            .ToHashSet() ?? [];

        var vLower = (vendor ?? string.Empty).ToLowerInvariant();
        var hLower = (hostname ?? string.Empty).ToLowerInvariant();
        var bLower = (httpBanner ?? string.Empty).ToLowerInvariant();
        var ipStr = ip ?? string.Empty;

        // 1. IP Cameras / CCTV (Strict matching on RTSP, Camera Banners, or Camera Vendors)
        if (open.Overlaps(CameraPorts) ||
            bLower.Contains("network camera") || bLower.Contains("camera") || bLower.Contains("thttpd") ||
            bLower.Contains("wv-") || hLower.Contains("cam") || hLower.Contains("cctv") ||
            hLower.Contains("ipcam") || hLower.Contains("nvr") || hLower.Contains("dvr") ||
            vLower.Contains("camera") || vLower.Contains("axis") || vLower.Contains("hikvision") ||
            vLower.Contains("dahua") || vLower.Contains("uniview") || vLower.Contains("vivotek") ||
            vLower.Contains("amcrest") || vLower.Contains("foscam") || vLower.Contains("ezviz") ||
            vLower.Contains("imou"))
        {
            return "IP Camera";
        }

        // 2. Printers & Multifunction Devices
        if (open.Overlaps(PrinterPorts) ||
            bLower.Contains("xerox") || bLower.Contains("workcentre") || bLower.Contains("laserjet") ||
            bLower.Contains("pagewide") || bLower.Contains("colorqube") || hLower.Contains("xrx") ||
            hLower.Contains("print") || hLower.Contains("hp_") ||
            vLower.Contains("xerox") || vLower.Contains("canon") || vLower.Contains("epson") ||
            vLower.Contains("brother") || vLower.Contains("kyocera") || vLower.Contains("ricoh") ||
            vLower.Contains("lexmark") || vLower.Contains("konica"))
        {
            return "Printer";
        }

        // 3. Network Infrastructure (Routers, Switches, Firewalls, Access Points)
        if (bLower.Contains("huawei switch") || bLower.Contains("switch admin") ||
            bLower.Contains("tp-link") || bLower.Contains("d-link") || bLower.Contains("busybox") ||
            bLower.Contains("openwrt") || bLower.Contains("router") ||
            vLower.Contains("cisco") || vLower.Contains("huawei") || vLower.Contains("tp-link") ||
            vLower.Contains("d-link") || vLower.Contains("mikrotik") || vLower.Contains("ubiquiti") ||
            vLower.Contains("juniper") || vLower.Contains("aruba") || vLower.Contains("fortinet") ||
            vLower.Contains("tenda") || vLower.Contains("zyxel") || vLower.Contains("netgear") ||
            ((ipStr.EndsWith(".1", StringComparison.Ordinal) || ipStr.EndsWith(".254", StringComparison.Ordinal)) &&
             (open.Contains(80) || open.Contains(443) || open.Contains(22))))
        {
            return "Router / Switch";
        }

        // 4. Access Control, Biometrics & BMS
        if (vLower.Contains("suprema") || hLower.Contains("biostar"))
            return "Access Control";
        if (vLower.Contains("alerton") || bLower.Contains("gsoap"))
            return "BMS Controller";

        // 5. Servers, Databases, Hypervisors, Virtualization Hosts
        if (hLower.Contains("cluster") || hLower.Contains("maincluster") ||
            hLower.Contains("server") || hLower.Contains("srv") || hLower.Contains("exchange") ||
            hLower.Contains("esxi") || hLower.Contains("vmware") || hLower.Contains("proxmox") ||
            vLower.Contains("vmware") || vLower.Contains("hyper-v") || vLower.Contains("supermicro") ||
            open.Overlaps(ServerPorts))
        {
            return "Server";
        }

        // 6. Workstation / PC / Laptop
        if (hLower.StartsWith("desktop-", StringComparison.OrdinalIgnoreCase) ||
            hLower.StartsWith("laptop-", StringComparison.OrdinalIgnoreCase) ||
            hLower.StartsWith("win-", StringComparison.OrdinalIgnoreCase) ||
            hLower.StartsWith("pc-", StringComparison.OrdinalIgnoreCase) ||
            hLower.Contains("-pc", StringComparison.OrdinalIgnoreCase) ||
            hLower.Contains("workstation", StringComparison.OrdinalIgnoreCase) ||
            hLower.Contains("client", StringComparison.OrdinalIgnoreCase) ||
            open.Overlaps(WindowsWorkstationPorts) ||
            vLower.Contains("dell") || vLower.Contains("hewlett packard") || vLower.Contains("lenovo") ||
            vLower.Contains("giga-byte") || vLower.Contains("micro-star") || vLower.Contains("asus") ||
            vLower.Contains("acer") || vLower.Contains("realtek") || vLower.Contains("intel") ||
            vLower.Contains("seavo") || vLower.Contains("compal") || vLower.Contains("wistron") ||
            vLower.Contains("apple"))
        {
            return "Workstation";
        }

        return "Generic Device";
    }
}

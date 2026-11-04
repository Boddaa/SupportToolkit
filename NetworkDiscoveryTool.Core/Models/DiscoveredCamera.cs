using System;

namespace NetworkDiscoveryTool.Core.Models;

public sealed class DiscoveredCamera
{
    public string IPAddress { get; set; } = string.Empty;
    public string? MACAddress { get; set; }
    public string Brand { get; set; } = "Unknown";
    public string Model { get; set; } = "IP Camera";
    public string? SerialNumber { get; set; }
    public string? FirmwareVersion { get; set; }
    public int HttpPort { get; set; } = 80;
    public int RtspPort { get; set; } = 554;
    public int? OnvifPort { get; set; }
    public string Protocol { get; set; } = "ONVIF";
    public string Status { get; set; } = "Online";
    public bool IsSubnetMismatch { get; set; }
    public string? SubnetMismatchDetails { get; set; }
    public string? RtspUrl { get; set; }
    public string? WebUrl { get; set; }
    public string? ExtraInfo { get; set; }
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;
}


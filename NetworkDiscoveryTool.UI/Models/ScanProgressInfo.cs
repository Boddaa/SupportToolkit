namespace NetworkDiscoveryTool.UI.Models;

public sealed class ScanProgressInfo
{
    public int Percentage { get; set; }
    public string CurrentIP { get; set; } = string.Empty;
    public int DevicesFound { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
}

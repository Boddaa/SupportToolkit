using System.Text.Json.Serialization;

namespace NetworkDiscoveryTool.UI.Models;

public enum CaptureMode
{
    FullScreen,
    ActiveWindow
}

public sealed class ScreenshotEntry
{
    public string FileName { get; set; } = "";
    public string FullPath { get; set; } = "";
    public string Notes { get; set; } = "";
    public string CaptureMode { get; set; } = "FullScreen";
    public long SizeBytes { get; set; }
    public DateTime CapturedAt { get; set; }

    [JsonIgnore]
    public string SizeDisplay => SizeBytes switch
    {
        < 1024 => $"{SizeBytes} B",
        < 1048576 => $"{SizeBytes / 1024.0:F1} KB",
        _ => $"{SizeBytes / 1048576.0:F1} MB",
    };

    [JsonIgnore]
    public string CapturedDisplay => CapturedAt.ToString("yyyy-MM-dd HH:mm:ss");

    [JsonIgnore]
    public string Icon => CaptureMode == "FullScreen" ? "🖥" : "🪟";
}

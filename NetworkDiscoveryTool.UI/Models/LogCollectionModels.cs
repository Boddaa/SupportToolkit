namespace NetworkDiscoveryTool.UI.Models;

public sealed class LogFileEntry
{
    public string Source { get; set; } = "";
    public string FileName { get; set; } = "";
    public string FullPath { get; set; } = "";
    public long SizeBytes { get; set; }
    public string SizeDisplay => FormatSize(SizeBytes);
    public DateTime LastModified { get; set; }
    public bool Selected { get; set; }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1048576 => $"{bytes / 1024.0:F1} KB",
        < 1073741824 => $"{bytes / 1048576.0:F1} MB",
        _ => $"{bytes / 1073741824.0:F2} GB",
    };
}

public sealed class CollectResult
{
    public bool Success { get; set; }
    public string ZipPath { get; set; } = "";
    public int FileCount { get; set; }
    public long PackageSizeBytes { get; set; }
    public string PackageSize => PackageSizeBytes switch
    {
        < 1024 => $"{PackageSizeBytes} B",
        < 1048576 => $"{PackageSizeBytes / 1024.0:F1} KB",
        _ => $"{PackageSizeBytes / 1048576.0:F1} MB",
    };
    public string Message { get; set; } = "";
}

public sealed class LogSourceOption
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Icon { get; set; } = "";
    public bool IsEnabled { get; set; }
    public string DefaultPath { get; set; } = "";
}

public sealed class TimelineDay
{
    public DateTime Date { get; set; }
    public int Count { get; set; }
    public string Label { get; set; } = "";
    public string DayOfWeek { get; set; } = "";
    public double BarWidth { get; set; }
    public double BarOpacity { get; set; } = 0.7;
}

using System.Diagnostics;
using System.IO.Compression;
using System.IO;
using NetworkDiscoveryTool.UI.Models;

namespace NetworkDiscoveryTool.UI.Services;

public interface ILogCollectionService
{
    Task<List<LogFileEntry>> PreviewAsync(string sourceType, string folder, string pattern, DateTime from, DateTime to);
    Task<CollectResult> CollectAsync(List<LogFileEntry> entries);
    void OpenFolder(string path);
    string GetDefaultPath(string sourceType);
}

public sealed class LogCollectionService : ILogCollectionService
{
    private static readonly string ExportDir = Path.Combine(
        Path.GetTempPath(), "LogCollectorExports");

    public string GetDefaultPath(string sourceType) => sourceType switch
    {
        "Application" => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs"),
        "IIS" => @"C:\inetpub\logs\LogFiles",
        _ => Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
    };

    public Task<List<LogFileEntry>> PreviewAsync(
        string sourceType, string folder, string pattern, DateTime from, DateTime to)
    {
        return Task.Run(() =>
        {
            var result = new List<LogFileEntry>();

            try
            {
                switch (sourceType)
                {
                    case "Application":
                        result.AddRange(ScanFolder(folder, pattern, from, to, "Application"));
                        break;

                    case "EventLog":
                        result.AddRange(CollectEventLogs(from, to));
                        break;

                    case "IIS":
                        result.AddRange(ScanFolder(folder, "*.log", from, to, "IIS"));
                        break;

                    case "Custom":
                        result.AddRange(ScanFolder(folder, pattern, from, to, "Custom"));
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Preview error ({sourceType}): {ex.Message}");
            }

            return result;
        });
    }

    public Task<CollectResult> CollectAsync(List<LogFileEntry> entries)
    {
        return Task.Run(() =>
        {
            var result = new CollectResult();

            try
            {
                var selected = entries.Where(e => e.Selected).ToList();
                if (selected.Count == 0)
                {
                    result.Message = "No files selected";
                    return result;
                }

                Directory.CreateDirectory(ExportDir);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var zipPath = Path.Combine(ExportDir, $"Logs_{timestamp}.zip");

                using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);

                foreach (var entry in selected)
                {
                    if (!File.Exists(entry.FullPath)) continue;
                    var arcName = $"{entry.Source}/{entry.FileName}";
                    archive.CreateEntryFromFile(entry.FullPath, arcName);
                }

                var fi = new FileInfo(zipPath);
                result.Success = true;
                result.ZipPath = zipPath;
                result.FileCount = selected.Count;
                result.PackageSizeBytes = fi.Length;
                result.Message = $"Collected {selected.Count} files ({result.PackageSize})";
            }
            catch (Exception ex)
            {
                result.Message = $"Failed to create package: {ex.Message}";
            }

            return result;
        });
    }

    public void OpenFolder(string path)
    {
        try
        {
            var dir = File.Exists(path) ? Path.GetDirectoryName(path) : path;
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                Process.Start("explorer.exe", dir);
        }
        catch { }
    }

    private static List<LogFileEntry> ScanFolder(string folder, string pattern, DateTime from, DateTime to, string source)
    {
        var result = new List<LogFileEntry>();
        if (!Directory.Exists(folder)) return result;

        foreach (var f in Directory.GetFiles(folder, pattern, SearchOption.AllDirectories))
        {
            var fi = new FileInfo(f);
            if (fi.LastWriteTime >= from && fi.LastWriteTime <= to.AddDays(1))
            {
                result.Add(new LogFileEntry
                {
                    Source = source,
                    FileName = fi.Name,
                    FullPath = fi.FullName,
                    SizeBytes = fi.Length,
                    LastModified = fi.LastWriteTime,
                    Selected = true,
                });
            }
        }

        return result.OrderByDescending(e => e.LastModified).ToList();
    }

    private static List<LogFileEntry> CollectEventLogs(DateTime from, DateTime to)
    {
        var result = new List<LogFileEntry>();
        var exportDir = Path.Combine(ExportDir, "EventLogs");
        Directory.CreateDirectory(exportDir);

        try
        {
            foreach (var logName in new[] { "Application", "System", "Security" })
            {
                using var eventLog = new EventLog(logName);
                if (eventLog.Entries.Count == 0) continue;

                var lines = new List<string>();
                var count = 0;

                foreach (EventLogEntry entry in eventLog.Entries)
                {
                    if (entry.TimeGenerated < from || entry.TimeGenerated > to.AddDays(1))
                        continue;

                    lines.Add($"[{entry.TimeGenerated:yyyy-MM-dd HH:mm:ss}] [{entry.EntryType}] [{entry.Source}]");
                    lines.Add($"  Message: {entry.Message}");
                    lines.Add($"  Category: {entry.Category} | Event ID: {entry.InstanceId}");
                    lines.Add("");
                    count++;
                }

                if (count == 0) continue;

                var logFile = Path.Combine(exportDir, $"{logName}_Events.log");
                File.WriteAllText(logFile, string.Join("\n", lines));

                var fi = new FileInfo(logFile);
                result.Add(new LogFileEntry
                {
                    Source = "EventLog",
                    FileName = $"{logName}_Events.log",
                    FullPath = fi.FullName,
                    SizeBytes = fi.Length,
                    LastModified = fi.LastWriteTime,
                    Selected = true,
                });
            }
        }
        catch { }

        return result;
    }
}

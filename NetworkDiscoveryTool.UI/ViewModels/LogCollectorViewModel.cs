using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetworkDiscoveryTool.UI.Models;
using NetworkDiscoveryTool.UI.Services;

namespace NetworkDiscoveryTool.UI.ViewModels;

public sealed partial class LogCollectorViewModel : ObservableObject
{
    private readonly ILogCollectionService _service;

    public LogCollectorViewModel(ILogCollectionService service)
    {
        _service = service;
        FromDate = DateTime.Today.AddDays(-7);
        ToDate = DateTime.Today;
    }

    // === Sources ===
    [ObservableProperty] private bool _collectAppLogs = true;
    [ObservableProperty] private bool _collectEventLogs = true;
    [ObservableProperty] private bool _collectIisLogs;
    [ObservableProperty] private bool _collectCustomFolder;

    // === App Logs Config ===
    [ObservableProperty] private string _appLogPath = "";
    [ObservableProperty] private string _appLogPattern = "*.log";

    // === Custom Folder Config ===
    [ObservableProperty] private string _customFolderPath = "";
    [ObservableProperty] private string _customFilePattern = "*.*";

    // === IIS Config ===
    [ObservableProperty] private string _iisLogPath = "";

    // === Date Range ===
    [ObservableProperty] private DateTime _fromDate;
    [ObservableProperty] private DateTime _toDate;

    // === Preview ===
    public ObservableCollection<LogFileEntry> PreviewFiles { get; } = [];

    [ObservableProperty] private int _previewCount;
    [ObservableProperty] private string _previewSize = "";

    // === Collect Result ===
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private CollectResult? _collectResult;

    // === Timeline ===
    public ObservableCollection<TimelineDay> TimelineDays { get; } = [];

    // === Dropped Files ===
    public ObservableCollection<LogFileEntry> DroppedFiles { get; } = [];

    // === UI State ===
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _statusText = "Ready";

    [RelayCommand]
    private async Task PreviewAsync()
    {
        IsLoading = true;
        HasError = false;
        ErrorMessage = "";
        PreviewFiles.Clear();
        HasResult = false;

        try
        {
            var tasks = new List<Task<List<LogFileEntry>>>();

            if (CollectAppLogs)
            {
                var path = string.IsNullOrWhiteSpace(AppLogPath) ? _service.GetDefaultPath("Application") : AppLogPath;
                tasks.Add(_service.PreviewAsync("Application", path, AppLogPattern, FromDate, ToDate));
            }

            if (CollectEventLogs)
                tasks.Add(_service.PreviewAsync("EventLog", "", "", FromDate, ToDate));

            if (CollectIisLogs)
            {
                var path = string.IsNullOrWhiteSpace(IisLogPath) ? _service.GetDefaultPath("IIS") : IisLogPath;
                tasks.Add(_service.PreviewAsync("IIS", path, "*.log", FromDate, ToDate));
            }

            if (CollectCustomFolder && !string.IsNullOrWhiteSpace(CustomFolderPath))
                tasks.Add(_service.PreviewAsync("Custom", CustomFolderPath, CustomFilePattern, FromDate, ToDate));

            if (tasks.Count == 0)
            {
                StatusText = "Select at least one log source";
                return;
            }

            var results = await Task.WhenAll(tasks);
            foreach (var list in results)
                foreach (var entry in list)
                    PreviewFiles.Add(entry);

            UpdatePreviewStats();
            RefreshTimeline();
            StatusText = $"Found {PreviewCount} log file(s) ({PreviewSize})";
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Preview failed: {ex.Message}";
            StatusText = "Error";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task CollectAsync()
    {
        if (PreviewFiles.Count == 0)
        {
            StatusText = "Run Preview first to find log files";
            return;
        }

        IsLoading = true;
        HasError = false;

        try
        {
            CollectResult = await _service.CollectAsync(PreviewFiles.ToList());
            HasResult = true;
            StatusText = CollectResult.Success
                ? $"Package created: {CollectResult.PackageSize} ({CollectResult.FileCount} files)"
                : CollectResult.Message;
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Collection failed: {ex.Message}";
            StatusText = "Error";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void OpenPackageFolder()
    {
        if (CollectResult is not null && !string.IsNullOrEmpty(CollectResult.ZipPath))
            _service.OpenFolder(CollectResult.ZipPath);
    }

    [RelayCommand]
    private void OpenFolderBrowser(string sourceType)
    {
        // Handled in code-behind via FolderBrowserDialog
    }

    [RelayCommand]
    private void ClearAll()
    {
        PreviewFiles.Clear();
        DroppedFiles.Clear();
        TimelineDays.Clear();
        HasResult = false;
        CollectResult = null;
        HasError = false;
        ErrorMessage = "";
        PreviewCount = 0;
        PreviewSize = "";
        StatusText = "Ready";
    }

    public void SetAppLogPath(string path) => AppLogPath = path;
    public void SetIisLogPath(string path) => IisLogPath = path;
    public void SetCustomFolderPath(string path) => CustomFolderPath = path;

    public void AddDroppedFile(LogFileEntry entry)
    {
        entry.Source = "Dropped";
        entry.Selected = true;
        DroppedFiles.Add(entry);
        PreviewFiles.Add(entry);
        UpdatePreviewStats();
    }

    public void ClearDroppedFiles()
    {
        foreach (var f in DroppedFiles)
            PreviewFiles.Remove(f);
        DroppedFiles.Clear();
        UpdatePreviewStats();
    }

    public void RefreshTimeline()
    {
        TimelineDays.Clear();
        if (PreviewFiles.Count == 0) return;

        var groups = PreviewFiles
            .GroupBy(f => f.LastModified.Date)
            .OrderBy(g => g.Key)
            .ToList();

        var maxCount = groups.Max(g => g.Count());
        const double maxBarWidth = 200;

        foreach (var g in groups)
        {
            TimelineDays.Add(new TimelineDay
            {
                Date = g.Key,
                Count = g.Count(),
                Label = g.Key.ToString("MMM dd"),
                DayOfWeek = g.Key.ToString("ddd"),
                BarWidth = maxCount > 0 ? (double)g.Count() / maxCount * maxBarWidth : 0,
            });
        }
    }

    public void UpdatePreviewStats()
    {
        PreviewCount = PreviewFiles.Count;
        var total = PreviewFiles.Sum(f => f.SizeBytes);
        PreviewSize = total switch
        {
            < 1024 => $"{total} B",
            < 1048576 => $"{total / 1024.0:F1} KB",
            _ => $"{total / 1048576.0:F1} MB",
        };
    }
}

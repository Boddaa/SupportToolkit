using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetworkDiscoveryTool.UI.Models;
using NetworkDiscoveryTool.UI.Services;

namespace NetworkDiscoveryTool.UI.ViewModels;

public sealed partial class ScreenshotViewModel : ObservableObject
{
    private readonly IScreenshotService _service;
    private readonly IOperationHistoryService _history;
    private readonly CurrentUserService _currentUser;

    public ScreenshotViewModel(IScreenshotService service, IOperationHistoryService history, CurrentUserService currentUser)
    {
        _service = service;
        _history = history;
        _currentUser = currentUser;
        SaveFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        _historyList = [.. _service.LoadHistory()];
    }

    // === Capture ===
    [ObservableProperty] private int _selectedCaptureMode; // 0=FullScreen, 1=ActiveWindow
    [ObservableProperty] private bool _isCapturing;

    // === Preview ===
    [ObservableProperty] private ImageSource? _previewImage;
    [ObservableProperty] private bool _hasPreview;
    [ObservableProperty] private string _imageDimensions = "";
    [ObservableProperty] private string _imageSize = "";

    private byte[]? _currentCaptureData;

    // === Notes ===
    [ObservableProperty] private string _notes = "";

    // === Save ===
    [ObservableProperty] private string _saveFolder = "";

    // === Status ===
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _errorMessage = "";

    // === History ===
    private readonly List<ScreenshotEntry> _historyList;
    public ObservableCollection<ScreenshotEntry> History { get; } = [];

    [ObservableProperty] private ScreenshotEntry? _selectedHistoryEntry;

    [RelayCommand]
    private async Task CaptureAsync()
    {
        IsCapturing = true;
        HasError = false;
        ErrorMessage = "";
        StatusText = "Capturing...";
        var sw = Stopwatch.StartNew();

        try
        {
            _currentCaptureData = SelectedCaptureMode switch
            {
                1 => await _service.CaptureActiveWindowAsync(),
                _ => await _service.CaptureFullScreenAsync(),
            };
            sw.Stop();

            using var ms = new MemoryStream(_currentCaptureData);
            ms.Position = 0;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.StreamSource = ms;
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.EndInit();
            bi.Freeze();

            PreviewImage = bi;
            HasPreview = true;
            ImageDimensions = $"{bi.PixelWidth} × {bi.PixelHeight} px";
            ImageSize = FormatSize(_currentCaptureData.Length);
            StatusText = "Capture complete";
            var modeName = SelectedCaptureMode == 1 ? "ActiveWindow" : "FullScreen";
            await _history.LogAsync("Screenshot", $"{modeName} | {ImageDimensions}", "Success", sw.ElapsedMilliseconds, _currentUser.Username);
        }
        catch (Exception ex)
        {
            sw.Stop();
            HasError = true;
            ErrorMessage = ex.Message;
            StatusText = "Capture failed";
        }
        finally
        {
            IsCapturing = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_currentCaptureData is null)
        {
            StatusText = "Capture a screenshot first";
            return;
        }

        try
        {
            var path = await _service.SaveScreenshotWithHistoryAsync(
                _currentCaptureData, SaveFolder, Notes, (CaptureMode)SelectedCaptureMode);
            StatusText = $"Saved: {Path.GetFileName(path)}";

            _historyList.Clear();
            _historyList.AddRange(_service.LoadHistory());
            RefreshHistory();

            Notes = "";
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
            StatusText = "Save failed";
        }
    }

    [RelayCommand]
    private async Task CopyToClipboardAsync()
    {
        if (_currentCaptureData is null)
        {
            StatusText = "Capture a screenshot first";
            return;
        }

        try
        {
            var msg = await _service.CopyToClipboardAsync(_currentCaptureData);
            StatusText = msg;
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
            StatusText = "Copy failed";
        }
    }

    [RelayCommand]
    private void BrowseSaveFolder()
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog();
        dlg.SelectedPath = SaveFolder;
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            SaveFolder = dlg.SelectedPath;
    }

    [RelayCommand]
    private void OpenSaveFolder()
    {
        if (!string.IsNullOrWhiteSpace(SaveFolder) && Directory.Exists(SaveFolder))
            System.Diagnostics.Process.Start("explorer.exe", SaveFolder);
    }

    [RelayCommand]
    private void ClearPreview()
    {
        PreviewImage = null;
        HasPreview = false;
        ImageDimensions = "";
        ImageSize = "";
        _currentCaptureData = null;
        Notes = "";
        StatusText = "Ready";
    }

    [RelayCommand]
    private void OpenHistoryEntry()
    {
        if (SelectedHistoryEntry is null) return;
        try
        {
            var dir = Path.GetDirectoryName(SelectedHistoryEntry.FullPath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                System.Diagnostics.Process.Start("explorer.exe", dir);
        }
        catch { }
    }

    [RelayCommand]
    private void DeleteHistoryEntry()
    {
        if (SelectedHistoryEntry is null) return;
        try
        {
            _historyList.Remove(SelectedHistoryEntry);
            _service.SaveHistory(_historyList);
            RefreshHistory();
            StatusText = "Entry deleted";
        }
        catch (Exception ex)
        {
            StatusText = $"Delete failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ClearHistory()
    {
        _historyList.Clear();
        _service.SaveHistory(_historyList);
        RefreshHistory();
        StatusText = "History cleared";
    }

    public void RefreshHistory()
    {
        History.Clear();
        foreach (var entry in _historyList)
            History.Add(entry);
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1048576 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / 1048576.0:F1} MB",
    };
}

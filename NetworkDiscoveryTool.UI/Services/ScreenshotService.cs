using System.IO;
using System.Text.Json;
using NetworkDiscoveryTool.UI.Models;

namespace NetworkDiscoveryTool.UI.Services;

public interface IScreenshotService
{
    Task<byte[]> CaptureFullScreenAsync();
    Task<byte[]> CaptureActiveWindowAsync();
    Task SaveScreenshotAsync(byte[] data, string filePath);
    Task<string> SaveScreenshotWithHistoryAsync(byte[] data, string folder, string notes, CaptureMode mode);
    List<ScreenshotEntry> LoadHistory();
    void SaveHistory(List<ScreenshotEntry> entries);
    Task<string> CopyToClipboardAsync(byte[] data);
}

public sealed class ScreenshotService : IScreenshotService
{
    private static readonly string HistoryDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SupportToolKit");
    private static readonly string HistoryFile = Path.Combine(HistoryDirectory, "screenshot_history.json");

    public Task<byte[]> CaptureFullScreenAsync()
    {
        return Task.Run(() =>
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen ?? System.Windows.Forms.Screen.AllScreens.FirstOrDefault();
            var bounds = screen?.Bounds ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
            using var bitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
            using var g = System.Drawing.Graphics.FromImage(bitmap);
            g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size);
            return BitmapToBytes(bitmap);
        });
    }

    public Task<byte[]> CaptureActiveWindowAsync()
    {
        return Task.Run(() =>
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                throw new InvalidOperationException("No active window found");

            NativeMethods.GetWindowRect(hwnd, out var rect);
            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;

            if (width <= 0 || height <= 0)
                throw new InvalidOperationException("Invalid window dimensions");

            using var bitmap = new System.Drawing.Bitmap(width, height);
            using var g = System.Drawing.Graphics.FromImage(bitmap);
            g.CopyFromScreen(rect.Left, rect.Top, 0, 0, new System.Drawing.Size(width, height));
            return BitmapToBytes(bitmap);
        });
    }

    public Task SaveScreenshotAsync(byte[] data, string filePath)
    {
        return Task.Run(() =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            using var ms = new MemoryStream(data);
            using var img = System.Drawing.Image.FromStream(ms);
            img.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
        });
    }

    public async Task<string> SaveScreenshotWithHistoryAsync(byte[] data, string folder, string notes, CaptureMode mode)
    {
        var timestamp = DateTime.Now;
        var fileName = $"Screenshot_{timestamp:yyyyMMdd_HHmmss}.png";
        var fullPath = Path.Combine(folder, fileName);

        await SaveScreenshotAsync(data, fullPath);

        var fi = new FileInfo(fullPath);
        var entry = new ScreenshotEntry
        {
            FileName = fileName,
            FullPath = fullPath,
            Notes = notes,
            CaptureMode = mode.ToString(),
            SizeBytes = fi.Length,
            CapturedAt = timestamp,
        };

        var history = LoadHistory();
        history.Insert(0, entry);
        SaveHistory(history);

        return fullPath;
    }

    public Task<string> CopyToClipboardAsync(byte[] data)
    {
        var tcs = new TaskCompletionSource<string>();
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        void PerformCopy()
        {
            try
            {
                using var ms = new MemoryStream(data);
                var bi = new System.Windows.Media.Imaging.BitmapImage();
                bi.BeginInit();
                bi.StreamSource = ms;
                bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bi.EndInit();
                bi.Freeze();
                System.Windows.Clipboard.SetImage(bi);
                tcs.TrySetResult("Copied to clipboard");
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }

        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.InvokeAsync(PerformCopy);
        }
        else
        {
            PerformCopy();
        }

        return tcs.Task;
    }

    public List<ScreenshotEntry> LoadHistory()
    {
        try
        {
            if (!File.Exists(HistoryFile))
                return [];
            var json = File.ReadAllText(HistoryFile);
            return JsonSerializer.Deserialize<List<ScreenshotEntry>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void SaveHistory(List<ScreenshotEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(HistoryDirectory);
            var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(HistoryFile, json);
        }
        catch { }
    }

    private static byte[] BitmapToBytes(System.Drawing.Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}

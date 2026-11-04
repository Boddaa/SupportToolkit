using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NetworkDiscoveryTool.UI.Helpers;

public static class ProcessIconHelper
{
    private static readonly ConcurrentDictionary<string, ImageSource?> _iconCache = new(StringComparer.OrdinalIgnoreCase);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static ImageSource? GetIcon(string? executablePath, string processName)
    {
        string? path = executablePath;

        if (string.IsNullOrWhiteSpace(path) || path == "N/A" || !File.Exists(path))
        {
            if (!string.IsNullOrWhiteSpace(processName))
            {
                var sys32 = Environment.SystemDirectory;
                var candidate = Path.Combine(sys32, processName);
                if (File.Exists(candidate))
                {
                    path = candidate;
                }
                else
                {
                    var sysName = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        ? processName
                        : processName + ".exe";
                    candidate = Path.Combine(sys32, sysName);
                    if (File.Exists(candidate))
                    {
                        path = candidate;
                    }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        return _iconCache.GetOrAdd(path, p =>
        {
            try
            {
                using var ico = System.Drawing.Icon.ExtractAssociatedIcon(p);
                if (ico != null)
                {
                    var hIcon = ico.Handle;
                    var bs = Imaging.CreateBitmapSourceFromHIcon(
                        hIcon,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    bs.Freeze();
                    DestroyIcon(hIcon);
                    return bs;
                }
            }
            catch
            {
                // Access denied on protected system files or 64-bit redirection
            }
            return null;
        });
    }
}

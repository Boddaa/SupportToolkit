using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using WpfMessageBox = System.Windows.MessageBox;
using WpfClipboard = System.Windows.Clipboard;
using NetworkDiscoveryTool.UI.Models;

namespace NetworkDiscoveryTool.UI.Services;

public sealed class ProcessControlService : IProcessControlService
{
    public async Task<(bool Success, string Message)> TerminateProcessAsync(int pid)
    {
        if (pid <= 4)
            return (false, "Cannot terminate critical system process.");

        return await Task.Run(() =>
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                p.Kill(entireProcessTree: false);
                p.WaitForExit(2000);
                return (true, $"Process {p.ProcessName} (PID {pid}) terminated successfully.");
            }
            catch (ArgumentException)
            {
                return (true, "Process has already exited.");
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
            {
                return (false, "Access denied. Administrator privileges are required to terminate this process.");
            }
            catch (Exception ex)
            {
                return (false, $"Unable to terminate process: {ex.Message}");
            }
        });
    }

    public async Task<(bool Success, string Message)> TerminateProcessTreeAsync(int pid)
    {
        if (pid <= 4)
            return (false, "Cannot terminate critical system process tree.");

        return await Task.Run(() =>
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                p.Kill(entireProcessTree: true);
                p.WaitForExit(3000);
                return (true, $"Process tree for {p.ProcessName} (PID {pid}) terminated successfully.");
            }
            catch (ArgumentException)
            {
                return (true, "Process has already exited.");
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
            {
                return (false, "Access denied. Administrator privileges are required to terminate this process tree.");
            }
            catch (Exception ex)
            {
                return (false, $"Unable to terminate process tree: {ex.Message}");
            }
        });
    }

    public void OpenFileLocation(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "N/A" || !File.Exists(path))
        {
            WpfMessageBox.Show("Executable path is not accessible or does not exist on disk.", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"Could not open file location: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void ShowFileProperties(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "N/A" || !File.Exists(path))
        {
            WpfMessageBox.Show("Executable path is not accessible or does not exist on disk.", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var sei = new SHELLEXECUTEINFO
            {
                cbSize = Marshal.SizeOf(typeof(SHELLEXECUTEINFO)),
                lpVerb = "properties",
                lpFile = path,
                nShow = 5 /* SW_SHOW */,
                fMask = 0x0000000C /* SEE_MASK_INVOKEIDLIST */
            };

            ShellExecuteEx(ref sei);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"Could not show file properties: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void CopyProcessInfo(ProcessItemModel p)
    {
        if (p == null) return;

        var sb = new StringBuilder();
        sb.AppendLine($"Process Name: {p.Name}");
        sb.AppendLine($"PID: {p.Id}");
        sb.AppendLine($"Status: {p.Status}");
        sb.AppendLine($"CPU Usage: {p.CpuUsageText}");
        sb.AppendLine($"Memory: {p.MemoryUsageText} ({p.MemoryPercent}%)");
        sb.AppendLine($"Threads: {p.ThreadCount}");
        sb.AppendLine($"Handles: {p.HandleCount}");
        sb.AppendLine($"User: {p.UserName}");
        sb.AppendLine($"Priority: {p.Priority}");
        sb.AppendLine($"Executable Path: {p.ExecutablePath}");
        sb.AppendLine($"Company: {p.Company}");
        sb.AppendLine($"File Version: {p.FileVersion}");
        sb.AppendLine($"Session ID: {p.SessionId}");
        if (p.ParentProcessId.HasValue)
            sb.AppendLine($"Parent Process: {p.ParentProcessName} ({p.ParentProcessId})");

        try
        {
            WpfClipboard.SetText(sb.ToString());
        }
        catch { }
    }

    public void SearchOnline(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return;
        try
        {
            var cleanName = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? processName[..^4]
                : processName;
            var url = $"https://www.google.com/search?q={Uri.EscapeDataString(cleanName + " Windows process")}";
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"Could not launch browser: {ex.Message}", "Search Online", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public async Task<(bool Success, string Message)> SetProcessPriorityAsync(int pid, ProcessPriorityClass priority)
    {
        if (pid <= 4)
            return (false, "Cannot modify priority of system kernel process.");

        return await Task.Run(() =>
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                p.PriorityClass = priority;
                return (true, $"Process '{p.ProcessName}' priority changed to {priority}.");
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
            {
                return (false, "Access denied. Administrator privileges required to change process priority.");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to change priority: {ex.Message}");
            }
        });
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPTStr)] public string lpVerb;
        [MarshalAs(UnmanagedType.LPTStr)] public string lpFile;
        [MarshalAs(UnmanagedType.LPTStr)] public string lpParameters;
        [MarshalAs(UnmanagedType.LPTStr)] public string lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        [MarshalAs(UnmanagedType.LPTStr)] public string lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);
}

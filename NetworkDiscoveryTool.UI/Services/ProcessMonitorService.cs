using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NetworkDiscoveryTool.UI.Models;

namespace NetworkDiscoveryTool.UI.Services;

public sealed class ProcessMonitorService : IProcessMonitorService
{
    public event Action<SystemPerformanceSnapshot>? SnapshotUpdated;

    private readonly ConcurrentDictionary<int, (TimeSpan CpuTime, DateTime SampleTime)> _prevProcessTimes = new();
    private readonly ConcurrentDictionary<int, string> _userCache = new();
    private readonly ConcurrentDictionary<string, (string Description, string Company, string Version)> _fileMetaCache = new();

    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;
    private bool _disposed;

    private ulong _prevSysKernel;
    private ulong _prevSysUser;
    private ulong _prevSysIdle;
    private bool _hasPrevSysTimes;

    public bool IsMonitoring { get; private set; }
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(1);

    public void StartMonitoring()
    {
        if (IsMonitoring) return;
        IsMonitoring = true;
        _monitorCts = new CancellationTokenSource();
        _monitorTask = Task.Run(() => MonitoringLoopAsync(_monitorCts.Token));
    }

    public void StopMonitoring()
    {
        if (!IsMonitoring) return;
        IsMonitoring = false;
        try
        {
            _monitorCts?.Cancel();
            _monitorCts?.Dispose();
        }
        catch { }
        _monitorCts = null;
    }

    private async Task MonitoringLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var snapshot = await CollectSnapshotAsync(ct);
                SnapshotUpdated?.Invoke(snapshot);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProcessMonitorService] Loop error: {ex.Message}");
            }

            try
            {
                await Task.Delay(Interval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task<SystemPerformanceSnapshot> CollectSnapshotAsync(CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var snapshot = new SystemPerformanceSnapshot();

            // 1. Overall System CPU
            snapshot.CpuUsagePercent = CalculateSystemCpuUsage();
            snapshot.CpuSpeedGhz = GetCpuSpeedString();

            // 2. System Memory
            GetSystemMemoryMetrics(snapshot);

            // 3. Parent PIDs snapshot via ToolHelp32
            var parentPidMap = GetParentProcessMap();

            // 4. Enumerate Processes
            var processList = Process.GetProcesses();
            var activePids = new HashSet<int>(processList.Length);
            var items = new List<ProcessItemModel>(processList.Length);

            int totalThreads = 0;
            int totalHandles = 0;

            foreach (var p in processList)
            {
                if (ct.IsCancellationRequested) break;

                int pid = p.Id;
                activePids.Add(pid);

                try
                {
                    var item = CreateOrUpdateProcessItem(p, parentPidMap, snapshot.TotalMemoryBytes);
                    if (item != null)
                    {
                        totalThreads += item.ThreadCount;
                        totalHandles += item.HandleCount;
                        items.Add(item);
                    }
                }
                catch
                {
                    // Process disappeared or access denied
                }
                finally
                {
                    p.Dispose();
                }
            }

            // Cleanup dead processes from CPU cache
            var deadPids = _prevProcessTimes.Keys.Where(k => !activePids.Contains(k)).ToList();
            foreach (var dp in deadPids)
            {
                _prevProcessTimes.TryRemove(dp, out _);
                _userCache.TryRemove(dp, out _);
            }

            snapshot.TotalProcesses = items.Count;
            snapshot.TotalThreads = totalThreads;
            snapshot.TotalHandles = totalHandles;
            snapshot.Processes = items;

            return snapshot;
        }, ct);
    }

    private ProcessItemModel? CreateOrUpdateProcessItem(Process p, Dictionary<int, int> parentPidMap, long totalPhysMem)
    {
        int pid = p.Id;
        string name = p.ProcessName + ".exe";

        // Memory
        long memBytes = 0;
        try { memBytes = p.WorkingSet64; } catch { }

        // Threads & Handles
        int threadCount = 0;
        try { threadCount = p.Threads.Count; } catch { }

        int handleCount = 0;
        try { handleCount = p.HandleCount; } catch { }

        // CPU Usage calculation (Delta sampling)
        double cpuPercent = 0.0;
        try
        {
            TimeSpan currCpu = p.TotalProcessorTime;
            DateTime currTime = DateTime.UtcNow;

            if (_prevProcessTimes.TryGetValue(pid, out var prev))
            {
                double cpuDeltaMs = (currCpu - prev.CpuTime).TotalMilliseconds;
                double timeDeltaMs = (currTime - prev.SampleTime).TotalMilliseconds;

                if (timeDeltaMs > 0)
                {
                    cpuPercent = (cpuDeltaMs / (timeDeltaMs * Environment.ProcessorCount)) * 100.0;
                    cpuPercent = Math.Clamp(Math.Round(cpuPercent, 1), 0.0, 100.0);
                }
            }

            _prevProcessTimes[pid] = (currCpu, currTime);
        }
        catch
        {
            // Access denied to TotalProcessorTime on system/idle processes
            if (pid == 0)
            {
                // System Idle Process: idle% = 100 - overall CPU%
                // We'll leave it 0 or computed
            }
        }

        // Executable Path
        string exePath = "N/A";
        try
        {
            if (pid > 4)
            {
                exePath = GetProcessPathSafe(p);
            }
        }
        catch { }

        // Metadata (Company, Description, Version)
        string description = p.MainWindowTitle;
        string company = "Unknown";
        string version = "N/A";

        if (!string.IsNullOrWhiteSpace(exePath) && exePath != "N/A")
        {
            if (_fileMetaCache.TryGetValue(exePath, out var meta))
            {
                if (string.IsNullOrWhiteSpace(description)) description = meta.Description;
                company = meta.Company;
                version = meta.Version;
            }
            else
            {
                try
                {
                    if (File.Exists(exePath))
                    {
                        var info = FileVersionInfo.GetVersionInfo(exePath);
                        var desc = info.FileDescription ?? "";
                        var comp = info.CompanyName ?? "Unknown";
                        var ver = info.FileVersion ?? "N/A";

                        _fileMetaCache[exePath] = (desc, comp, ver);

                        if (string.IsNullOrWhiteSpace(description)) description = desc;
                        company = comp;
                        version = ver;
                    }
                }
                catch { }
            }
        }

        if (string.IsNullOrWhiteSpace(description))
            description = p.ProcessName;

        // User Name
        string userName = _userCache.GetOrAdd(pid, id => GetProcessUserSafe(p));

        // Priority
        string priority = "Normal";
        try { priority = p.PriorityClass.ToString(); } catch { }

        // Start Time
        DateTime? startTime = null;
        string startTimeText = "N/A";
        try
        {
            startTime = p.StartTime;
            startTimeText = startTime.Value.ToString("HH:mm:ss");
        }
        catch { }

        // Parent PID
        int? parentId = parentPidMap.TryGetValue(pid, out int parent) ? parent : null;

        // Session ID
        int sessionId = 0;
        try { sessionId = p.SessionId; } catch { }

        // Is Application & System Classification
        bool hasWindow = false;
        try { hasWindow = p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(p.MainWindowTitle); } catch { }

        bool isSystem = pid <= 4 ||
            userName.Equals("SYSTEM", StringComparison.OrdinalIgnoreCase) ||
            userName.Contains("LOCAL SERVICE", StringComparison.OrdinalIgnoreCase) ||
            userName.Contains("NETWORK SERVICE", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("smss.exe", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("csrss.exe", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("wininit.exe", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("services.exe", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("lsass.exe", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("winlogon.exe", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("svchost.exe", StringComparison.OrdinalIgnoreCase);

        bool isApp = hasWindow && !isSystem;

        // Real executable icon
        var icon = Helpers.ProcessIconHelper.GetIcon(exePath, name);

        // Memory text & percent
        string memText = FormatBytes(memBytes);
        double memPercent = totalPhysMem > 0 ? Math.Round((double)memBytes / totalPhysMem * 100.0, 1) : 0.0;

        return new ProcessItemModel
        {
            Id = pid,
            Name = name,
            Description = description,
            Status = "Running",
            CpuUsage = cpuPercent,
            CpuUsageText = $"{cpuPercent:F1}%",
            MemoryBytes = memBytes,
            MemoryUsageText = memText,
            MemoryPercent = memPercent,
            ThreadCount = threadCount,
            HandleCount = handleCount,
            UserName = userName,
            Priority = priority,
            ExecutablePath = exePath,
            StartTime = startTime,
            StartTimeText = startTimeText,
            ParentProcessId = parentId,
            Company = company,
            FileVersion = version,
            SessionId = sessionId,
            IsApplication = isApp,
            IsSystemProcess = isSystem,
            Icon = icon,
            HasCustomIcon = icon != null
        };
    }

    public List<StartupItemModel> GetStartupItems()
    {
        var list = new List<StartupItemModel>();
        try
        {
            using var cuKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (cuKey != null)
            {
                foreach (var valName in cuKey.GetValueNames())
                {
                    var cmd = cuKey.GetValue(valName)?.ToString() ?? string.Empty;
                    var path = ExtractExePathFromCommand(cmd);
                    var icon = Helpers.ProcessIconHelper.GetIcon(path, Path.GetFileName(path));
                    var publisher = GetPublisherFromExe(path);
                    list.Add(new StartupItemModel
                    {
                        Name = valName,
                        Publisher = publisher,
                        Command = cmd,
                        Location = "HKCU: Run",
                        Status = "Enabled",
                        Impact = "Medium",
                        Icon = icon
                    });
                }
            }

            using var lmKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (lmKey != null)
            {
                foreach (var valName in lmKey.GetValueNames())
                {
                    if (list.Any(x => x.Name.Equals(valName, StringComparison.OrdinalIgnoreCase))) continue;
                    var cmd = lmKey.GetValue(valName)?.ToString() ?? string.Empty;
                    var path = ExtractExePathFromCommand(cmd);
                    var icon = Helpers.ProcessIconHelper.GetIcon(path, Path.GetFileName(path));
                    var publisher = GetPublisherFromExe(path);
                    list.Add(new StartupItemModel
                    {
                        Name = valName,
                        Publisher = publisher,
                        Command = cmd,
                        Location = "HKLM: Run",
                        Status = "Enabled",
                        Impact = "High",
                        Icon = icon
                    });
                }
            }
        }
        catch { }

        return list;
    }

    private static string ExtractExePathFromCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return string.Empty;
        var trimmed = command.Trim();
        if (trimmed.StartsWith("\""))
        {
            int nextQuote = trimmed.IndexOf('"', 1);
            if (nextQuote > 1)
                return trimmed.Substring(1, nextQuote - 1);
        }
        int space = trimmed.IndexOf(' ');
        return space > 0 ? trimmed.Substring(0, space) : trimmed;
    }

    private static string GetPublisherFromExe(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var vi = FileVersionInfo.GetVersionInfo(path);
                if (!string.IsNullOrWhiteSpace(vi.CompanyName))
                    return vi.CompanyName;
            }
        }
        catch { }
        return "Unknown";
    }

    private static string GetProcessPathSafe(Process p)
    {
        try
        {
            var buffer = new StringBuilder(1024);
            int capacity = buffer.Capacity;
            IntPtr hProcess = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, p.Id);
            if (hProcess != IntPtr.Zero)
            {
                try
                {
                    if (QueryFullProcessImageName(hProcess, 0, buffer, ref capacity))
                    {
                        return buffer.ToString();
                    }
                }
                finally
                {
                    CloseHandle(hProcess);
                }
            }
        }
        catch { }

        try
        {
            return p.MainModule?.FileName ?? "N/A";
        }
        catch
        {
            return "N/A";
        }
    }

    private static string GetProcessUserSafe(Process p)
    {
        if (p.Id == 0) return "SYSTEM";
        if (p.Id == 4) return "SYSTEM";

        IntPtr hProcess = IntPtr.Zero;
        IntPtr hToken = IntPtr.Zero;

        try
        {
            hProcess = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, p.Id);
            if (hProcess != IntPtr.Zero && OpenProcessToken(hProcess, 0x0008 /* TOKEN_QUERY */, out hToken))
            {
                using var identity = new WindowsIdentity(hToken);
                var full = identity.Name;
                if (!string.IsNullOrWhiteSpace(full))
                {
                    int slash = full.IndexOf('\\');
                    return slash >= 0 ? full[(slash + 1)..] : full;
                }
            }
        }
        catch { }
        finally
        {
            if (hToken != IntPtr.Zero) CloseHandle(hToken);
            if (hProcess != IntPtr.Zero) CloseHandle(hProcess);
        }

        return p.SessionId == 0 ? "SYSTEM" : Environment.UserName;
    }

    private double CalculateSystemCpuUsage()
    {
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
            return 0.0;

        ulong idle = FileTimeToUInt64(idleTime);
        ulong kernel = FileTimeToUInt64(kernelTime);
        ulong user = FileTimeToUInt64(userTime);

        if (!_hasPrevSysTimes)
        {
            _prevSysIdle = idle;
            _prevSysKernel = kernel;
            _prevSysUser = user;
            _hasPrevSysTimes = true;
            return 0.0;
        }

        ulong deltaKernel = kernel - _prevSysKernel;
        ulong deltaUser = user - _prevSysUser;
        ulong deltaIdle = idle - _prevSysIdle;

        _prevSysIdle = idle;
        _prevSysKernel = kernel;
        _prevSysUser = user;

        ulong totalSys = deltaKernel + deltaUser;
        if (totalSys == 0) return 0.0;

        ulong totalBusy = totalSys > deltaIdle ? totalSys - deltaIdle : 0;
        double percent = (double)totalBusy / totalSys * 100.0;
        return Math.Clamp(Math.Round(percent, 1), 0.0, 100.0);
    }

    private static void GetSystemMemoryMetrics(SystemPerformanceSnapshot s)
    {
        var memStatus = new MEMORYSTATUSEX();
        if (GlobalMemoryStatusEx(memStatus))
        {
            s.TotalMemoryBytes = (long)memStatus.ullTotalPhys;
            s.FreeMemoryBytes = (long)memStatus.ullAvailPhys;
            s.UsedMemoryBytes = s.TotalMemoryBytes - s.FreeMemoryBytes;
            s.MemoryUsagePercent = Math.Round((double)memStatus.dwMemoryLoad, 1);
        }
        else
        {
            s.TotalMemoryBytes = 16L * 1024 * 1024 * 1024;
            s.UsedMemoryBytes = 8L * 1024 * 1024 * 1024;
            s.MemoryUsagePercent = 50.0;
        }
    }

    private static string GetCpuSpeedString()
    {
        try
        {
            // Approximate clock speed or fallback to logical processors
            return $"{Environment.ProcessorCount} Cores";
        }
        catch
        {
            return "Multi-Core";
        }
    }

    private static Dictionary<int, int> GetParentProcessMap()
    {
        var map = new Dictionary<int, int>();
        IntPtr hSnapshot = CreateToolhelp32Snapshot(0x00000002 /* TH32CS_SNAPPROCESS */, 0);
        if (hSnapshot == IntPtr.Zero || hSnapshot == new IntPtr(-1))
            return map;

        try
        {
            var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32)) };
            if (Process32First(hSnapshot, ref entry))
            {
                do
                {
                    map[(int)entry.th32ProcessID] = (int)entry.th32ParentProcessID;
                }
                while (Process32Next(hSnapshot, ref entry));
            }
        }
        catch { }
        finally
        {
            CloseHandle(hSnapshot);
        }

        return map;
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{kb:F0} KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:F0} MB";
        double gb = mb / 1024.0;
        return $"{gb:F2} GB";
    }

    private static ulong FileTimeToUInt64(System.Runtime.InteropServices.ComTypes.FILETIME ft)
    {
        return ((ulong)(uint)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopMonitoring();
    }

    #region Win32 P/Invoke

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(
        out System.Runtime.InteropServices.ComTypes.FILETIME lpIdleTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME lpKernelTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME lpUserTime);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public MEMORYSTATUSEX()
        {
            dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, [Out] StringBuilder lpExeName, ref int lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    #endregion
}

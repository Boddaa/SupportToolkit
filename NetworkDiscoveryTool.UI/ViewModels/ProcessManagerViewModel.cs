using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using WpfMessageBox = System.Windows.MessageBox;
using WpfApplication = System.Windows.Application;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetworkDiscoveryTool.UI.Models;
using NetworkDiscoveryTool.UI.Services;

namespace NetworkDiscoveryTool.UI.ViewModels;

public sealed partial class ProcessManagerViewModel : ObservableObject, IDisposable
{
    private readonly IProcessMonitorService _monitorService;
    private readonly IProcessControlService _controlService;
    private readonly IOperationHistoryService _historyService;
    private readonly CurrentUserService _currentUser;

    private readonly Dictionary<int, ProcessItemModel> _processLookup = new();
    private readonly List<ProcessItemModel> _allProcesses = new();
    private readonly List<double> _sysCpuHistory = new(65);
    private readonly List<double> _sysMemHistory = new(65);

    private int? _inspectedProcessId;
    private bool _isSyncingProcesses;

    // ========================================================
    // Header & Auto Refresh State
    // ========================================================
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string _statusBadgeText = "Live Monitoring";
    [ObservableProperty] private string _selectedIntervalOption = "Auto Refresh: 1s";
    public ObservableCollection<string> IntervalOptions { get; } =
    [
        "Auto Refresh: 1s",
        "Auto Refresh: 2s",
        "Auto Refresh: 5s",
        "Auto Refresh: 10s",
        "Manual"
    ];

    partial void OnSelectedIntervalOptionChanged(string value)
    {
        switch (value)
        {
            case "Auto Refresh: 1s":
                _monitorService.Interval = TimeSpan.FromSeconds(1);
                _monitorService.StartMonitoring();
                StatusBadgeText = "Live Monitoring";
                break;
            case "Auto Refresh: 2s":
                _monitorService.Interval = TimeSpan.FromSeconds(2);
                _monitorService.StartMonitoring();
                StatusBadgeText = "Live Monitoring (2s)";
                break;
            case "Auto Refresh: 5s":
                _monitorService.Interval = TimeSpan.FromSeconds(5);
                _monitorService.StartMonitoring();
                StatusBadgeText = "Live Monitoring (5s)";
                break;
            case "Auto Refresh: 10s":
                _monitorService.Interval = TimeSpan.FromSeconds(10);
                _monitorService.StartMonitoring();
                StatusBadgeText = "Live Monitoring (10s)";
                break;
            case "Manual":
                _monitorService.StopMonitoring();
                StatusBadgeText = "Paused (Manual)";
                break;
        }
    }

    // ========================================================
    // Top 5 KPI Cards & Interactive KPI Filtering
    // ========================================================
    [ObservableProperty] private string _cpuUsageText = "0%";
    [ObservableProperty] private string _cpuSpeedText = "Multi-Core";
    [ObservableProperty] private string _cpuSparklinePoints = "0,15 50,15 100,15";

    [ObservableProperty] private string _memoryUsageText = "0 GB / 0 GB (0%)";
    [ObservableProperty] private string _memorySparklinePoints = "0,15 50,15 100,15";

    [ObservableProperty] private string _processesCountText = "0 Active";
    [ObservableProperty] private string _processesSparklinePoints = "0,15 50,15 100,15";

    [ObservableProperty] private string _threadsCountText = "0 Total";
    [ObservableProperty] private string _threadsSparklinePoints = "0,15 50,15 100,15";

    [ObservableProperty] private string _handlesCountText = "0 Total";
    [ObservableProperty] private string _handlesSparklinePoints = "0,15 50,15 100,15";

    [ObservableProperty] private string _activeKpiFilter = "None"; // None, CPU, Memory, Processes, Threads, Handles

    [RelayCommand]
    public void FilterByCpuCard()
    {
        ActiveKpiFilter = ActiveKpiFilter == "CPU" ? "None" : "CPU";
        SortColumn = "CpuUsage";
        IsSortAscending = false;
        ApplyFilters();
    }

    [RelayCommand]
    public void FilterByMemoryCard()
    {
        ActiveKpiFilter = ActiveKpiFilter == "Memory" ? "None" : "Memory";
        SortColumn = "MemoryBytes";
        IsSortAscending = false;
        ApplyFilters();
    }

    [RelayCommand]
    public void FilterByProcessesCard()
    {
        ActiveKpiFilter = "None";
        SelectedCategoryFilter = "All Processes";
        SearchText = string.Empty;
        ApplyFilters();
    }

    [RelayCommand]
    public void FilterByThreadsCard()
    {
        ActiveKpiFilter = ActiveKpiFilter == "Threads" ? "None" : "Threads";
        SortColumn = "ThreadCount";
        IsSortAscending = false;
        ApplyFilters();
    }

    [RelayCommand]
    public void FilterByHandlesCard()
    {
        ActiveKpiFilter = ActiveKpiFilter == "Handles" ? "None" : "Handles";
        SortColumn = "HandleCount";
        IsSortAscending = false;
        ApplyFilters();
    }

    // ========================================================
    // Tabs Navigation (Processes, Applications, Performance, Startup)
    // ========================================================
    [ObservableProperty] private int _selectedMainTabIndex = 0;
    [ObservableProperty] private bool _isProcessesTab = true;
    [ObservableProperty] private bool _isApplicationsTab;
    [ObservableProperty] private bool _isPerformanceTab;
    [ObservableProperty] private bool _isStartupTab;

    [RelayCommand]
    public void SelectTab(string tabName)
    {
        IsProcessesTab = tabName == "Processes";
        IsApplicationsTab = tabName == "Applications";
        IsPerformanceTab = tabName == "Performance";
        IsStartupTab = tabName == "Startup";

        SelectedMainTabIndex = tabName switch
        {
            "Processes" => 0,
            "Applications" => 1,
            "Performance" => 2,
            "Startup" => 3,
            _ => 0
        };

        if (IsApplicationsTab)
        {
            SelectedCategoryFilter = "Applications";
        }
        else if (IsProcessesTab && SelectedCategoryFilter == "Applications")
        {
            SelectedCategoryFilter = "All Processes";
        }
        else if (IsStartupTab)
        {
            LoadStartupItems();
        }
        else
        {
            ApplyFilters();
        }
    }

    // ========================================================
    // Filter & Search Controls
    // ========================================================
    [ObservableProperty] private string _searchText = string.Empty;
    partial void OnSearchTextChanged(string value) => ApplyFilters();

    [ObservableProperty] private string _selectedCategoryFilter = "All Processes";
    public ObservableCollection<string> CategoryFilters { get; } =
    [
        "All Processes",
        "Applications",
        "Background Processes",
        "System Processes"
    ];
    partial void OnSelectedCategoryFilterChanged(string value) => ApplyFilters();

    [ObservableProperty] private string _selectedUserFilter = "All Users";
    public ObservableCollection<string> UserFilters { get; } = ["All Users"];
    partial void OnSelectedUserFilterChanged(string value) => ApplyFilters();

    [ObservableProperty] private bool _isTreeView = false;
    [ObservableProperty] private string _viewModeButtonText = "Tree View";

    [RelayCommand]
    public void ToggleViewMode()
    {
        IsTreeView = !IsTreeView;
        ViewModeButtonText = IsTreeView ? "List View" : "Tree View";
        ApplyFilters();
    }

    // ========================================================
    // Sorting
    // ========================================================
    [ObservableProperty] private string _sortColumn = "CpuUsage";
    [ObservableProperty] private bool _isSortAscending = false;

    [RelayCommand]
    public void SortByColumn(string columnName)
    {
        if (string.Equals(SortColumn, columnName, StringComparison.OrdinalIgnoreCase))
        {
            IsSortAscending = !IsSortAscending;
        }
        else
        {
            SortColumn = columnName;
            IsSortAscending = columnName != "CpuUsage" && columnName != "MemoryBytes";
        }
        ApplyFilters();
    }

    // ========================================================
    // Process Collections & Display
    // ========================================================
    public ObservableCollection<ProcessItemModel> Processes { get; } = new();
    public ObservableCollection<ProcessItemModel> TopProcesses { get; } = new();
    public ObservableCollection<StartupItemModel> StartupItems { get; } = new();

    [ObservableProperty] private string _displayCountText = "Showing 0 processes";
    [ObservableProperty] private string _topProcessesMetric = "CPU"; // "CPU" or "Memory"

    [RelayCommand]
    public void ToggleTopMetric()
    {
        TopProcessesMetric = TopProcessesMetric == "CPU" ? "Memory" : "CPU";
        UpdateTopProcesses();
    }

    // ========================================================
    // Process Selection & Details Drawer (Stays Open)
    // ========================================================
    [ObservableProperty] private ProcessItemModel? _selectedProcess;
    [ObservableProperty] private bool _hasSelectedProcess;
    [ObservableProperty] private string _endTaskButtonText = "End Task (0)";
    [ObservableProperty] private int _selectedDrawerTab = 0; // 0=Details, 1=Performance, 2=Modules, 3=Handles, 4=Threads

    // Drawer sub-tabs
    [ObservableProperty] private bool _isDrawerDetailsSelected = true;
    [ObservableProperty] private bool _isDrawerPerformanceSelected;
    [ObservableProperty] private bool _isDrawerModulesSelected;
    [ObservableProperty] private bool _isDrawerHandlesSelected;
    [ObservableProperty] private bool _isDrawerThreadsSelected;

    [ObservableProperty] private string _drawerCpuSparkline = "";
    [ObservableProperty] private string _drawerMemorySparkline = "";

    partial void OnSelectedProcessChanged(ProcessItemModel? value)
    {
        if (_isSyncingProcesses)
        {
            // Do NOT allow background collection sync to wipe out the open drawer!
            return;
        }

        if (value != null)
        {
            _inspectedProcessId = value.Id;
            HasSelectedProcess = true;
            EndTaskButtonText = $"End Task ({value.Id})";
            UpdateDrawerSparklines(value);
        }
        else
        {
            if (_inspectedProcessId.HasValue && _processLookup.TryGetValue(_inspectedProcessId.Value, out var existing))
            {
                // Process is still active; maintain drawer inspection
                WpfApplication.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (_inspectedProcessId.HasValue && SelectedProcess == null)
                    {
                        SelectedProcess = existing;
                        HasSelectedProcess = true;
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
            else
            {
                HasSelectedProcess = false;
                EndTaskButtonText = "End Task (0)";
            }
        }
    }

    [RelayCommand]
    public void SelectProcess(ProcessItemModel? item)
    {
        if (item == null) return;
        _inspectedProcessId = item.Id;
        SelectedProcess = item;
        HasSelectedProcess = true;
        EndTaskButtonText = $"End Task ({item.Id})";
        UpdateDrawerSparklines(item);
    }

    [RelayCommand]
    public void CloseDrawer()
    {
        _inspectedProcessId = null;
        SelectedProcess = null;
        HasSelectedProcess = false;
        EndTaskButtonText = "End Task (0)";
    }

    [RelayCommand]
    public void SelectDrawerTab(string tabName)
    {
        IsDrawerDetailsSelected = tabName == "Details";
        IsDrawerPerformanceSelected = tabName == "Performance";
        IsDrawerModulesSelected = tabName == "Modules";
        IsDrawerHandlesSelected = tabName == "Handles";
        IsDrawerThreadsSelected = tabName == "Threads";

        SelectedDrawerTab = tabName switch
        {
            "Details" => 0,
            "Performance" => 1,
            "Modules" => 2,
            "Handles" => 3,
            "Threads" => 4,
            _ => 0
        };
    }

    // ========================================================
    // Bottom Section Live Metrics
    // ========================================================
    [ObservableProperty] private string _overallCpuPercentText = "0%";
    [ObservableProperty] private string _overallCpu60sPoints = "";
    [ObservableProperty] private string _overallMemoryText = "0 GB / 0 GB (0%)";
    [ObservableProperty] private string _overallMemory60sPoints = "";

    // ========================================================
    // Confirmation Dialog
    // ========================================================
    [ObservableProperty] private bool _isConfirmDialogOpen;
    [ObservableProperty] private string _confirmDialogTitle = string.Empty;
    [ObservableProperty] private string _confirmDialogMessage = string.Empty;
    [ObservableProperty] private bool _isCriticalWarning;
    private Func<Task>? _confirmedAction;

    [RelayCommand]
    public void PromptEndProcess(ProcessItemModel? target = null)
    {
        var proc = target ?? SelectedProcess;
        if (proc == null) return;

        bool isCritical = proc.Id <= 4 || proc.IsSystemProcess ||
                          proc.Name.Equals("csrss.exe", StringComparison.OrdinalIgnoreCase) ||
                          proc.Name.Equals("smss.exe", StringComparison.OrdinalIgnoreCase) ||
                          proc.Name.Equals("lsass.exe", StringComparison.OrdinalIgnoreCase) ||
                          proc.Name.Equals("services.exe", StringComparison.OrdinalIgnoreCase);

        ConfirmDialogTitle = isCritical ? "⚠️ Critical Process Termination Warning" : "Terminate Process";
        ConfirmDialogMessage = isCritical
            ? $"Terminating '{proc.Name}' (PID {proc.Id}) can cause Windows to crash or become unresponsive immediately. Are you sure you want to proceed?"
            : $"Are you sure you want to terminate '{proc.Name}' (PID {proc.Id})?";

        IsCriticalWarning = isCritical;
        _confirmedAction = async () =>
        {
            var res = await _controlService.TerminateProcessAsync(proc.Id);
            await LogOperationAsync("End Process", $"{proc.Name} (PID {proc.Id})", res.Success ? "Success" : "Failed");
            if (!res.Success)
            {
                WpfMessageBox.Show(res.Message, "Unable to Terminate", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            if (_inspectedProcessId == proc.Id)
            {
                CloseDrawer();
            }
            await RefreshNowAsync();
        };

        IsConfirmDialogOpen = true;
    }

    [RelayCommand]
    public void PromptEndProcessTree(ProcessItemModel? target = null)
    {
        var proc = target ?? SelectedProcess;
        if (proc == null) return;

        ConfirmDialogTitle = "Terminate Process Tree";
        ConfirmDialogMessage = $"Are you sure you want to terminate '{proc.Name}' (PID {proc.Id}) and all of its child processes?";
        IsCriticalWarning = proc.IsSystemProcess;

        _confirmedAction = async () =>
        {
            var res = await _controlService.TerminateProcessTreeAsync(proc.Id);
            await LogOperationAsync("End Process Tree", $"{proc.Name} (PID {proc.Id})", res.Success ? "Success" : "Failed");
            if (!res.Success)
            {
                WpfMessageBox.Show(res.Message, "Unable to Terminate Tree", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            if (_inspectedProcessId == proc.Id)
            {
                CloseDrawer();
            }
            await RefreshNowAsync();
        };

        IsConfirmDialogOpen = true;
    }

    [RelayCommand]
    public async Task ConfirmActionAsync()
    {
        IsConfirmDialogOpen = false;
        if (_confirmedAction != null)
        {
            await _confirmedAction();
            _confirmedAction = null;
        }
    }

    // Alias for XAML binding compatibility
    public IAsyncRelayCommand ConfirmActionAsyncCommand => ConfirmActionCommand;

    [RelayCommand]
    public void CancelAction()
    {
        IsConfirmDialogOpen = false;
        _confirmedAction = null;
    }

    // ========================================================
    // Context Menu & Quick Action Commands (Windows Task Manager features)
    // ========================================================
    [RelayCommand]
    public void OpenFileLocation(ProcessItemModel? p = null)
    {
        var item = p ?? SelectedProcess;
        if (item != null)
            _controlService.OpenFileLocation(item.ExecutablePath);
    }

    [RelayCommand]
    public void ShowFileProperties(ProcessItemModel? p = null)
    {
        var item = p ?? SelectedProcess;
        if (item != null)
            _controlService.ShowFileProperties(item.ExecutablePath);
    }

    [RelayCommand]
    public void CopyProcessInfo(ProcessItemModel? p = null)
    {
        var item = p ?? SelectedProcess;
        if (item != null)
            _controlService.CopyProcessInfo(item);
    }

    [RelayCommand]
    public void SearchOnline(ProcessItemModel? p = null)
    {
        var item = p ?? SelectedProcess;
        if (item != null)
            _controlService.SearchOnline(item.Name);
    }

    [RelayCommand]
    public async Task ChangePriorityAsync(string priorityStr)
    {
        if (SelectedProcess == null) return;
        if (Enum.TryParse<ProcessPriorityClass>(priorityStr, true, out var prio))
        {
            var res = await _controlService.SetProcessPriorityAsync(SelectedProcess.Id, prio);
            if (res.Success)
            {
                SelectedProcess.Priority = priorityStr;
                await LogOperationAsync("Change Priority", $"{SelectedProcess.Name} -> {priorityStr}", "Success");
            }
            else
            {
                WpfMessageBox.Show(res.Message, "Change Priority", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    [RelayCommand]
    public async Task RefreshNowAsync()
    {
        IsLoading = true;
        try
        {
            var snapshot = await _monitorService.CollectSnapshotAsync();
            OnSnapshotReceived(snapshot);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProcessManagerViewModel] Refresh error: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    // Alias for XAML binding compatibility
    public IAsyncRelayCommand RefreshNowAsyncCommand => RefreshNowCommand;

    // ========================================================
    // Constructor & Initialization
    // ========================================================
    public ProcessManagerViewModel(
        IProcessMonitorService monitorService,
        IProcessControlService controlService,
        IOperationHistoryService historyService,
        CurrentUserService currentUser)
    {
        _monitorService = monitorService;
        _controlService = controlService;
        _historyService = historyService;
        _currentUser = currentUser;

        _monitorService.SnapshotUpdated += OnSnapshotReceived;
        _monitorService.StartMonitoring();
    }

    public void OnNavigatedTo()
    {
        if (SelectedIntervalOption != "Manual")
        {
            _monitorService.StartMonitoring();
        }
        _ = RefreshNowAsync();
    }

    public void OnNavigatedFrom()
    {
        _monitorService.StopMonitoring();
    }

    private void OnSnapshotReceived(SystemPerformanceSnapshot snapshot)
    {
        WpfApplication.Current.Dispatcher.InvokeAsync(() =>
        {
            // 1. Update KPI Values
            CpuUsageText = $"{snapshot.CpuUsagePercent:F0}%";
            CpuSpeedText = snapshot.CpuSpeedGhz;
            OverallCpuPercentText = $"{snapshot.CpuUsagePercent:F1}%";

            double totalMemGb = snapshot.TotalMemoryBytes / (1024.0 * 1024 * 1024);
            double usedMemGb = snapshot.UsedMemoryBytes / (1024.0 * 1024 * 1024);
            MemoryUsageText = $"{usedMemGb:F1} GB / {totalMemGb:F0} GB ({snapshot.MemoryUsagePercent:F0}%)";
            OverallMemoryText = $"{usedMemGb:F1} GB / {totalMemGb:F0} GB ({snapshot.MemoryUsagePercent:F0}%)";

            ProcessesCountText = $"{snapshot.TotalProcesses} Active";
            ThreadsCountText = $"{snapshot.TotalThreads:N0} Total";
            HandlesCountText = $"{snapshot.TotalHandles:N0} Total";

            // 2. Update Sparklines History
            _sysCpuHistory.Add(snapshot.CpuUsagePercent);
            if (_sysCpuHistory.Count > 30) _sysCpuHistory.RemoveAt(0);
            CpuSparklinePoints = GeneratePolylinePoints(_sysCpuHistory, 100, 24, 100);
            OverallCpu60sPoints = GeneratePolylinePoints(_sysCpuHistory, 280, 70, 100);

            _sysMemHistory.Add(snapshot.MemoryUsagePercent);
            if (_sysMemHistory.Count > 30) _sysMemHistory.RemoveAt(0);
            MemorySparklinePoints = GeneratePolylinePoints(_sysMemHistory, 100, 24, 100);
            OverallMemory60sPoints = GeneratePolylinePoints(_sysMemHistory, 280, 70, 100);

            ProcessesSparklinePoints = GeneratePolylinePoints(_sysCpuHistory, 100, 24, 100);
            ThreadsSparklinePoints = GeneratePolylinePoints(_sysMemHistory, 100, 24, 100);
            HandlesSparklinePoints = GeneratePolylinePoints(_sysCpuHistory, 100, 24, 100);

            // 3. Sync Process List incrementally with Drawer Preservation Guard
            _isSyncingProcesses = true;
            try
            {
                SyncProcesses(snapshot.Processes);

                // 4. Update Top Processes
                UpdateTopProcesses();

                // 5. Update Selected Process if currently inspected (ensures drawer STAYS OPEN)
                if (_inspectedProcessId.HasValue)
                {
                    if (_processLookup.TryGetValue(_inspectedProcessId.Value, out var updated))
                    {
                        SelectedProcess = updated;
                        HasSelectedProcess = true;
                        UpdateDrawerSparklines(updated);
                    }
                    else
                    {
                        // Process actually terminated
                        _inspectedProcessId = null;
                        SelectedProcess = null;
                        HasSelectedProcess = false;
                        EndTaskButtonText = "End Task (0)";
                    }
                }
            }
            finally
            {
                _isSyncingProcesses = false;
            }

            IsLoading = false;
        });
    }

    private void SyncProcesses(List<ProcessItemModel> incomingList)
    {
        var incomingPids = new HashSet<int>(incomingList.Count);
        var distinctUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "All Users" };

        foreach (var incoming in incomingList)
        {
            incomingPids.Add(incoming.Id);
            distinctUsers.Add(incoming.UserName);

            if (_processLookup.TryGetValue(incoming.Id, out var existing))
            {
                existing.CpuUsage = incoming.CpuUsage;
                existing.CpuUsageText = incoming.CpuUsageText;
                existing.MemoryBytes = incoming.MemoryBytes;
                existing.MemoryUsageText = incoming.MemoryUsageText;
                existing.MemoryPercent = incoming.MemoryPercent;
                existing.ThreadCount = incoming.ThreadCount;
                existing.HandleCount = incoming.HandleCount;
                existing.Status = incoming.Status;
                existing.Priority = incoming.Priority;
                existing.Description = incoming.Description;
                existing.IsApplication = incoming.IsApplication;
                existing.IsSystemProcess = incoming.IsSystemProcess;

                if (existing.Icon == null && incoming.Icon != null)
                {
                    existing.Icon = incoming.Icon;
                    existing.HasCustomIcon = true;
                }

                existing.AddCpuSample(incoming.CpuUsage);
                existing.AddMemorySample(incoming.MemoryBytes);
            }
            else
            {
                incoming.AddCpuSample(incoming.CpuUsage);
                incoming.AddMemorySample(incoming.MemoryBytes);

                _processLookup[incoming.Id] = incoming;
                _allProcesses.Add(incoming);
            }
        }

        // Remove dead processes
        for (int i = _allProcesses.Count - 1; i >= 0; i--)
        {
            var p = _allProcesses[i];
            if (!incomingPids.Contains(p.Id))
            {
                _processLookup.Remove(p.Id);
                _allProcesses.RemoveAt(i);
            }
        }

        // Update User Filters list if needed
        foreach (var u in distinctUsers)
        {
            if (!UserFilters.Contains(u))
                UserFilters.Add(u);
        }

        // Resolve Parent-Child hierarchy
        ResolveHierarchy();

        // Apply Search, Filters, and Sorting to UI Collection
        ApplyFilters();
    }

    private void ResolveHierarchy()
    {
        foreach (var p in _allProcesses)
        {
            p.Children.Clear();
            p.HasChildren = false;
            p.IndentLevel = 0;
        }

        foreach (var p in _allProcesses)
        {
            if (p.ParentProcessId.HasValue &&
                _processLookup.TryGetValue(p.ParentProcessId.Value, out var parent) &&
                parent.Id != p.Id)
            {
                parent.Children.Add(p);
                parent.HasChildren = true;
                p.ParentProcessName = parent.Name;
            }
        }
    }

    private void ApplyFilters()
    {
        IEnumerable<ProcessItemModel> query = _allProcesses;

        // 1. Search Query
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            query = query.Where(p =>
                p.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                p.Description.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                p.Id.ToString().Contains(term) ||
                p.UserName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                p.ExecutablePath.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                p.Company.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        // 2. Category Filter
        if (SelectedCategoryFilter == "Applications")
        {
            query = query.Where(p => p.IsApplication);
        }
        else if (SelectedCategoryFilter == "System Processes")
        {
            query = query.Where(p => p.IsSystemProcess);
        }
        else if (SelectedCategoryFilter == "Background Processes")
        {
            query = query.Where(p => !p.IsApplication && !p.IsSystemProcess);
        }

        // 3. User Filter
        if (!string.IsNullOrWhiteSpace(SelectedUserFilter) && SelectedUserFilter != "All Users")
        {
            query = query.Where(p => string.Equals(p.UserName, SelectedUserFilter, StringComparison.OrdinalIgnoreCase));
        }

        // 4. Interactive KPI Card Filters
        if (ActiveKpiFilter == "CPU")
        {
            query = query.Where(p => p.CpuUsage > 0.0);
        }
        else if (ActiveKpiFilter == "Memory")
        {
            query = query.Where(p => p.MemoryBytes > 50 * 1024 * 1024); // > 50MB
        }
        else if (ActiveKpiFilter == "Threads")
        {
            query = query.Where(p => p.ThreadCount >= 5);
        }
        else if (ActiveKpiFilter == "Handles")
        {
            query = query.Where(p => p.HandleCount >= 100);
        }

        List<ProcessItemModel> filtered;

        if (IsTreeView)
        {
            var roots = query.Where(p => !p.ParentProcessId.HasValue || !_processLookup.ContainsKey(p.ParentProcessId.Value)).ToList();
            var flattenedTree = new List<ProcessItemModel>();
            foreach (var r in roots)
            {
                FlattenTree(r, 0, flattenedTree);
            }
            filtered = flattenedTree;
        }
        else
        {
            // Flat List View Sorting
            filtered = SortColumn switch
            {
                "Name" => IsSortAscending ? query.OrderBy(p => p.Name).ToList() : query.OrderByDescending(p => p.Name).ToList(),
                "Id" => IsSortAscending ? query.OrderBy(p => p.Id).ToList() : query.OrderByDescending(p => p.Id).ToList(),
                "MemoryBytes" => IsSortAscending ? query.OrderBy(p => p.MemoryBytes).ToList() : query.OrderByDescending(p => p.MemoryBytes).ToList(),
                "ThreadCount" => IsSortAscending ? query.OrderBy(p => p.ThreadCount).ToList() : query.OrderByDescending(p => p.ThreadCount).ToList(),
                "HandleCount" => IsSortAscending ? query.OrderBy(p => p.HandleCount).ToList() : query.OrderByDescending(p => p.HandleCount).ToList(),
                "UserName" => IsSortAscending ? query.OrderBy(p => p.UserName).ToList() : query.OrderByDescending(p => p.UserName).ToList(),
                "Priority" => IsSortAscending ? query.OrderBy(p => p.Priority).ToList() : query.OrderByDescending(p => p.Priority).ToList(),
                _ => IsSortAscending ? query.OrderBy(p => p.CpuUsage).ToList() : query.OrderByDescending(p => p.CpuUsage).ToList()
            };
        }

        // Sync to ObservableCollection without destroying existing items
        SyncObservableCollection(Processes, filtered);
        DisplayCountText = $"Showing {Processes.Count} of {_allProcesses.Count} processes";
    }

    private static void FlattenTree(ProcessItemModel item, int level, List<ProcessItemModel> result)
    {
        item.IndentLevel = level;
        result.Add(item);
        if (item.IsExpanded && item.HasChildren)
        {
            foreach (var child in item.Children)
            {
                FlattenTree(child, level + 1, result);
            }
        }
    }

    private void SyncObservableCollection(ObservableCollection<ProcessItemModel> target, List<ProcessItemModel> source)
    {
        int min = Math.Min(target.Count, source.Count);
        for (int i = 0; i < min; i++)
        {
            if (!ReferenceEquals(target[i], source[i]))
            {
                target[i] = source[i];
            }
        }

        while (target.Count > source.Count)
        {
            target.RemoveAt(target.Count - 1);
        }

        for (int i = target.Count; i < source.Count; i++)
        {
            target.Add(source[i]);
        }
    }

    private void UpdateTopProcesses()
    {
        var top = TopProcessesMetric == "CPU"
            ? _allProcesses.OrderByDescending(p => p.CpuUsage).Take(5).ToList()
            : _allProcesses.OrderByDescending(p => p.MemoryBytes).Take(5).ToList();

        SyncObservableCollection(TopProcesses, top);
    }

    private void LoadStartupItems()
    {
        try
        {
            var items = _monitorService.GetStartupItems();
            StartupItems.Clear();
            foreach (var it in items)
            {
                StartupItems.Add(it);
            }
        }
        catch { }
    }

    private void UpdateDrawerSparklines(ProcessItemModel p)
    {
        if (p.CpuHistory.Count > 0)
        {
            DrawerCpuSparkline = GeneratePolylinePoints(p.CpuHistory, 160, 36, 100);
        }
        if (p.MemoryHistory.Count > 0)
        {
            double maxMem = p.MemoryHistory.Max();
            if (maxMem <= 0) maxMem = 1;
            DrawerMemorySparkline = GeneratePolylinePoints(p.MemoryHistory, 160, 36, maxMem);
        }
    }

    public static string GeneratePolylinePoints(List<double> values, double width, double height, double maxValue)
    {
        if (values.Count < 2 || maxValue <= 0)
            return $"0,{height} {width},{height}";

        var sb = new StringBuilder();
        double stepX = width / (values.Count - 1);

        for (int i = 0; i < values.Count; i++)
        {
            double x = i * stepX;
            double norm = Math.Clamp(values[i] / maxValue, 0.0, 1.0);
            double y = height - (norm * (height - 4)) - 2;

            if (i > 0) sb.Append(' ');
            sb.Append(x.ToString("F1", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(y.ToString("F1", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private async Task LogOperationAsync(string operation, string description, string result)
    {
        try
        {
            await _historyService.LogAsync(
                operation,
                description,
                result,
                0,
                _currentUser.Username ?? "Administrator");
        }
        catch { }
    }

    public void Dispose()
    {
        _monitorService.SnapshotUpdated -= OnSnapshotReceived;
        _monitorService.StopMonitoring();
    }
}

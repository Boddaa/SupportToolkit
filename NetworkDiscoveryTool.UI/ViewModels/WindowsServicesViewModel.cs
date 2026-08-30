using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security.Principal;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetworkDiscoveryTool.UI.Models;
using NetworkDiscoveryTool.UI.Services;

namespace NetworkDiscoveryTool.UI.ViewModels;

public sealed partial class WindowsServicesViewModel : ObservableObject
{
    private readonly IWindowsServiceManager _manager;
    private readonly IOperationHistoryService _history;
    private readonly CurrentUserService _currentUser;
    private List<ServiceItemModel> _allServices = [];

    public WindowsServicesViewModel(IWindowsServiceManager manager, IOperationHistoryService history, CurrentUserService currentUser)
    {
        _manager = manager;
        _history = history;
        _currentUser = currentUser;
        IsAdmin = currentUser.IsAdmin;
        _currentUser.UserChanged += () => IsAdmin = _currentUser.IsAdmin;
    }

    [ObservableProperty] private bool _isAdmin;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private ServiceFilter _statusFilter = ServiceFilter.All;
    [ObservableProperty] private StartupTypeFilter _startupFilter = StartupTypeFilter.All;
    [ObservableProperty] private string _sortColumn = "DisplayName";
    [ObservableProperty] private bool _sortAscending = true;
    [ObservableProperty] private bool _showDetail;
    [ObservableProperty] private ServiceDetailModel? _selectedServiceDetail;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _runningCount;
    [ObservableProperty] private int _stoppedCount;
    [ObservableProperty] private int _disabledCount;

    public ObservableCollection<ServiceItemModel> FilteredServices { get; } = [];

    partial void OnSearchTextChanged(string value) => ApplyFilters();
    partial void OnStatusFilterChanged(ServiceFilter value) => ApplyFilters();
    partial void OnStartupFilterChanged(StartupTypeFilter value) => ApplyFilters();

    [RelayCommand]
    private async Task LoadServicesAsync()
    {
        IsLoading = true;
        HasError = false;
        ErrorMessage = "";

        try
        {
            _allServices = await _manager.GetAllAsync();
            TotalCount = _allServices.Count;
            RunningCount = _allServices.Count(s => s.IsRunning);
            StoppedCount = _allServices.Count(s => s.IsStopped);
            DisabledCount = _allServices.Count(s =>
                s.StartupType.Equals("Disabled", StringComparison.OrdinalIgnoreCase));
            ApplyFilters();
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Failed to load services: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = "";
        StatusFilter = ServiceFilter.All;
        StartupFilter = StartupTypeFilter.All;
        HasError = false;
        ErrorMessage = "";
    }

    [RelayCommand]
    private void FilterAll()
    {
        StatusFilter = ServiceFilter.All;
        StartupFilter = StartupTypeFilter.All;
    }

    [RelayCommand]
    private void FilterRunning()
    {
        StatusFilter = ServiceFilter.Running;
        StartupFilter = StartupTypeFilter.All;
    }

    [RelayCommand]
    private void FilterStopped()
    {
        StatusFilter = ServiceFilter.Stopped;
        StartupFilter = StartupTypeFilter.All;
    }

    [RelayCommand]
    private void FilterDisabled()
    {
        StatusFilter = ServiceFilter.All;
        StartupFilter = StartupTypeFilter.Disabled;
    }

    [RelayCommand]
    private void CopyText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        try
        {
            System.Windows.Clipboard.SetText(text);
        }
        catch { /* ignore clipboard locks */ }
    }

    private static bool IsElevated =>
        WindowsIdentity.GetCurrent().Owner?.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) == true;

    private static string FormatServiceError(string action, string name, Exception ex)
    {
        var msg = ex.Message;
        if (msg.Contains("cannot open", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("access is denied", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("access denied", StringComparison.OrdinalIgnoreCase))
        {
            return IsElevated
                ? $"Failed to {action} '{name}': service may not exist or is protected. {msg}"
                : $"Failed to {action} '{name}': administrator privileges required. Run the app as Administrator.";
        }
        return $"Failed to {action} '{name}': {msg}";
    }

    [RelayCommand]
    private async Task StartServiceAsync(ServiceItemModel? item)
    {
        if (item is null || item.IsRunning) return;
        if (!IsAdmin)
        {
            HasError = true;
            ErrorMessage = "Permission Denied: Administrator role required to start Windows services.";
            return;
        }
        HasError = false;
        ErrorMessage = "";
        var sw = Stopwatch.StartNew();
        try
        {
            await _manager.StartAsync(item.Name);
            sw.Stop();
            await _history.LogAsync("Windows Service Start", $"Started '{item.DisplayName}'", "Success", sw.ElapsedMilliseconds, _currentUser.Username);
            await LoadServicesAsync();
        }
        catch (Exception ex)
        {
            sw.Stop();
            HasError = true;
            ErrorMessage = FormatServiceError("start", item.Name, ex);
            await _history.LogAsync("Windows Service Start", $"Start '{item.DisplayName}' failed: {ex.Message}", "Failed", sw.ElapsedMilliseconds, _currentUser.Username);
        }
    }

    [RelayCommand]
    private async Task StopServiceAsync(ServiceItemModel? item)
    {
        if (item is null || item.IsStopped || !item.CanStop) return;
        if (!IsAdmin)
        {
            HasError = true;
            ErrorMessage = "Permission Denied: Administrator role required to stop Windows services.";
            return;
        }
        HasError = false;
        ErrorMessage = "";
        var sw = Stopwatch.StartNew();
        try
        {
            await _manager.StopAsync(item.Name);
            sw.Stop();
            await _history.LogAsync("Windows Service Stop", $"Stopped '{item.DisplayName}'", "Success", sw.ElapsedMilliseconds, _currentUser.Username);
            await LoadServicesAsync();
        }
        catch (Exception ex)
        {
            sw.Stop();
            HasError = true;
            ErrorMessage = FormatServiceError("stop", item.Name, ex);
            await _history.LogAsync("Windows Service Stop", $"Stop '{item.DisplayName}' failed: {ex.Message}", "Failed", sw.ElapsedMilliseconds, _currentUser.Username);
        }
    }

    [RelayCommand]
    private async Task RestartServiceAsync(ServiceItemModel? item)
    {
        if (item is null) return;
        if (!IsAdmin)
        {
            HasError = true;
            ErrorMessage = "Permission Denied: Administrator role required to restart Windows services.";
            return;
        }
        if (item.IsRunning && !item.CanStop)
        {
            HasError = true;
            ErrorMessage = $"Cannot restart '{item.DisplayName}': the service does not support stop";
            return;
        }
        HasError = false;
        ErrorMessage = "";
        var sw = Stopwatch.StartNew();
        try
        {
            await _manager.RestartAsync(item.Name);
            sw.Stop();
            await _history.LogAsync("Windows Service Restart", $"Restarted '{item.DisplayName}'", "Success", sw.ElapsedMilliseconds, _currentUser.Username);
            await LoadServicesAsync();
        }
        catch (Exception ex)
        {
            sw.Stop();
            HasError = true;
            ErrorMessage = FormatServiceError("restart", item.Name, ex);
            await _history.LogAsync("Windows Service Restart", $"Restart '{item.DisplayName}' failed: {ex.Message}", "Failed", sw.ElapsedMilliseconds, _currentUser.Username);
        }
    }

    [RelayCommand]
    private async Task ShowDetailAsync(ServiceItemModel? item)
    {
        if (item is null) return;
        IsLoading = true;
        HasError = false;
        ErrorMessage = "";
        try
        {
            SelectedServiceDetail = await _manager.GetDetailAsync(item.Name);
            if (SelectedServiceDetail is null)
            {
                HasError = true;
                ErrorMessage = $"Failed to load details for '{item.Name}' — service may not exist or access denied";
            }
            else
            {
                ShowDetail = true;
            }
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Failed to load details for '{item.Name}': {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void CloseDetail()
    {
        ShowDetail = false;
        SelectedServiceDetail = null;
    }

    [RelayCommand]
    private void SortBy(string column)
    {
        if (string.IsNullOrEmpty(column)) return;
        if (SortColumn == column)
            SortAscending = !SortAscending;
        else
        {
            SortColumn = column;
            SortAscending = true;
        }
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var query = _allServices.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            query = query.Where(s =>
                s.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                s.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        query = StatusFilter switch
        {
            ServiceFilter.Running => query.Where(s => s.IsRunning),
            ServiceFilter.Stopped => query.Where(s => s.IsStopped),
            _ => query,
        };

        if (StartupFilter != StartupTypeFilter.All)
        {
            var val = StartupFilter.ToString();
            query = query.Where(s =>
                s.StartupType.Equals(val, StringComparison.OrdinalIgnoreCase));
        }

        query = (SortColumn, SortAscending) switch
        {
            ("Name", true) => query.OrderBy(s => s.Name),
            ("Name", false) => query.OrderByDescending(s => s.Name),
            ("Status", true) => query.OrderBy(s => s.StatusCode),
            ("Status", false) => query.OrderByDescending(s => s.StatusCode),
            ("StartupType", true) => query.OrderBy(s => s.StartupType),
            ("StartupType", false) => query.OrderByDescending(s => s.StartupType),
            ("DisplayName", true) => query.OrderBy(s => s.DisplayName),
            ("DisplayName", false) => query.OrderByDescending(s => s.DisplayName),
            _ => query.OrderBy(s => s.DisplayName),
        };

        FilteredServices.Clear();
        foreach (var s in query)
            FilteredServices.Add(s);
    }
}

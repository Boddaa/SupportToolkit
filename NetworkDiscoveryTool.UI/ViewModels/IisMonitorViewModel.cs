using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetworkDiscoveryTool.UI.Models;
using NetworkDiscoveryTool.UI.Services;

namespace NetworkDiscoveryTool.UI.ViewModels;

public sealed partial class IisMonitorViewModel : ObservableObject
{
    private readonly IIisService _service;
    private readonly IOperationHistoryService _history;
    private readonly CurrentUserService _currentUser;

    public IisMonitorViewModel(IIisService service, IOperationHistoryService history, CurrentUserService currentUser)
    {
        _service = service;
        _history = history;
        _currentUser = currentUser;
        IsAdmin = currentUser.IsAdmin;
        _currentUser.UserChanged += () => IsAdmin = _currentUser.IsAdmin;
    }

    [ObservableProperty] private bool _isAdmin;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _iisAvailable;
    [ObservableProperty] private bool _requiresAdmin;
    [ObservableProperty] private bool _isW3SvcRunning = true;

    // Search and filtering
    [ObservableProperty] private string _searchWebsitesText = "";
    [ObservableProperty] private string _searchPoolsText = "";

    // Inspector
    [ObservableProperty] private IisSiteModel? _selectedSite;
    [ObservableProperty] private IisAppPoolModel? _selectedPool;
    [ObservableProperty] private bool _isInspectorOpen;

    public ObservableCollection<IisSiteModel> Sites { get; } = [];
    public ObservableCollection<IisSiteModel> FilteredSites { get; } = [];

    public ObservableCollection<IisAppPoolModel> AppPools { get; } = [];
    public ObservableCollection<IisAppPoolModel> FilteredAppPools { get; } = [];

    [ObservableProperty] private int _siteCount;
    [ObservableProperty] private int _siteRunning;
    [ObservableProperty] private int _siteStopped;
    [ObservableProperty] private int _poolCount;
    [ObservableProperty] private int _poolRunning;
    [ObservableProperty] private int _poolStopped;

    // Metrics for Cards 3 & 4
    [ObservableProperty] private int _workerProcessCount = 18;
    [ObservableProperty] private string _cpuUsageText = "23%";
    [ObservableProperty] private string _ramUsageText = "1.8 GB";

    [ObservableProperty] private int _sslCertCount = 8;
    [ObservableProperty] private int _sslValidCount = 6;
    [ObservableProperty] private int _sslExpiringCount = 1;
    [ObservableProperty] private int _sslExpiredCount = 1;

    partial void OnSearchWebsitesTextChanged(string value) => ApplySiteFilter();
    partial void OnSearchPoolsTextChanged(string value) => ApplyPoolFilter();

    private void ApplySiteFilter()
    {
        FilteredSites.Clear();
        var query = SearchWebsitesText?.Trim() ?? "";
        foreach (var s in Sites)
        {
            if (string.IsNullOrEmpty(query) ||
                s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.Bindings.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.PhysicalPath.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                FilteredSites.Add(s);
            }
        }
    }

    private void ApplyPoolFilter()
    {
        FilteredAppPools.Clear();
        var query = SearchPoolsText?.Trim() ?? "";
        foreach (var p in AppPools)
        {
            if (string.IsNullOrEmpty(query) ||
                p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.ManagedPipelineMode.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.ManagedRuntimeVersion.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                FilteredAppPools.Add(p);
            }
        }
    }

    [RelayCommand]
    private void SelectSite(IisSiteModel? site)
    {
        if (site is null) return;
        SelectedSite = site;
        SelectedPool = null;
        IsInspectorOpen = true;
    }

    [RelayCommand]
    private void SelectPool(IisAppPoolModel? pool)
    {
        if (pool is null) return;
        SelectedPool = pool;
        IsInspectorOpen = true;
    }

    [RelayCommand]
    private void CloseInspector()
    {
        IsInspectorOpen = false;
    }

    [RelayCommand]
    private void BrowseSite(IisSiteModel? site)
    {
        if (site is null) return;
        try
        {
            var binding = site.Bindings.Split(',').FirstOrDefault()?.Trim() ?? "http:80";
            var parts = binding.Split(':');
            var proto = parts[0];
            var port = parts.Length > 1 ? parts[1] : (proto == "https" ? "443" : "80");
            var url = port is "80" or "443" ? $"{proto}://localhost" : $"{proto}://localhost:{port}";

            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Failed to browse website: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenSiteFolder(IisSiteModel? site)
    {
        if (site is null || string.IsNullOrWhiteSpace(site.PhysicalPath)) return;
        try
        {
            var path = Environment.ExpandEnvironmentVariables(site.PhysicalPath);
            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                HasError = true;
                ErrorMessage = $"Directory does not exist: {path}";
            }
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Failed to open folder: {ex.Message}";
        }
    }

    [RelayCommand]
    private void RestartAsAdmin()
    {
        try
        {
            var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exePath))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Process.Start(psi);
                System.Windows.Application.Current.Shutdown();
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to elevate: {ex.Message}";
            HasError = true;
        }
    }

    // Site commands
    [RelayCommand]
    private async Task StartSiteAsync(IisSiteModel? site)
    {
        if (site is null || site.IsRunning) return;
        await ExecuteActionAsync(() => _service.StartSiteAsync(site.Name), $"Starting site '{site.Name}'");
    }

    [RelayCommand]
    private async Task StopSiteAsync(IisSiteModel? site)
    {
        if (site is null || !site.IsRunning) return;
        await ExecuteActionAsync(() => _service.StopSiteAsync(site.Name), $"Stopping site '{site.Name}'");
    }

    // Pool commands
    [RelayCommand]
    private async Task StartPoolAsync(IisAppPoolModel? pool)
    {
        if (pool is null || pool.IsRunning) return;
        await ExecuteActionAsync(() => _service.StartPoolAsync(pool.Name), $"Starting pool '{pool.Name}'");
    }

    [RelayCommand]
    private async Task StopPoolAsync(IisAppPoolModel? pool)
    {
        if (pool is null || !pool.IsRunning) return;
        await ExecuteActionAsync(() => _service.StopPoolAsync(pool.Name), $"Stopping pool '{pool.Name}'");
    }

    [RelayCommand]
    private async Task RecyclePoolAsync(IisAppPoolModel? pool)
    {
        if (pool is null) return;
        await ExecuteActionAsync(() => _service.RecyclePoolAsync(pool.Name), $"Recycling pool '{pool.Name}'");
    }

    [RelayCommand]
    private async Task GlobalRestartIisAsync()
    {
        await ExecuteActionAsync(() => _service.GlobalRestartIisAsync(), "Restarting IIS (iisreset)");
    }

    [RelayCommand]
    private async Task LoadIisInfoAsync()
    {
        IsLoading = true;
        HasError = false;
        ErrorMessage = "";
        RequiresAdmin = false;

        try
        {
            if (!_service.IsIisInstalled)
            {
                IisAvailable = false;
                StatusText = "⚠ IIS is not installed on this machine";
                IsLoading = false;
                return;
            }

            IisAvailable = true;
            IsW3SvcRunning = _service.IsW3SvcRunning;

            if (!_service.IsAdmin)
            {
                RequiresAdmin = true;
                StatusText = "🔒 Administrator elevation required to query IIS configuration";
            }

            var sitesTask = _service.GetSitesAsync();
            var poolsTask = _service.GetAppPoolsAsync();

            await Task.WhenAll(sitesTask, poolsTask);

            Sites.Clear();
            foreach (var s in sitesTask.Result)
                Sites.Add(s);

            AppPools.Clear();
            foreach (var p in poolsTask.Result)
                AppPools.Add(p);

            SiteCount = Sites.Count;
            SiteRunning = Sites.Count(s => s.IsRunning);
            SiteStopped = Sites.Count(s => !s.IsRunning);

            PoolCount = AppPools.Count;
            PoolRunning = AppPools.Count(p => p.IsRunning);
            PoolStopped = AppPools.Count(p => !p.IsRunning);

            WorkerProcessCount = Math.Max(AppPools.Sum(p => p.WorkerProcesses), PoolRunning);
            SslCertCount = Math.Max(Sites.Count(s => s.HasSsl), Sites.Count > 0 ? 1 : 0);
            SslValidCount = SslCertCount;
            SslExpiringCount = 0;
            SslExpiredCount = 0;

            ApplySiteFilter();
            ApplyPoolFilter();

            if (SelectedSite == null && Sites.Count > 0)
            {
                SelectedSite = Sites[0];
            }

            StatusText = $"Loaded {SiteCount} sites, {PoolCount} app pools";
        }
        catch (UnauthorizedAccessException uex)
        {
            RequiresAdmin = true;
            HasError = true;
            ErrorMessage = uex.Message;
            StatusText = "Administrator privileges required to query IIS";
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Failed to load IIS info: {ex.Message}";
            StatusText = "Failed to load IIS information";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ExecuteActionAsync(Func<Task> action, string actionName)
    {
        if (!IsAdmin)
        {
            HasError = true;
            ErrorMessage = $"Permission Denied: Administrator role required for {actionName}.";
            StatusText = "Permission Denied";
            return;
        }

        IsLoading = true;
        HasError = false;
        StatusText = actionName;
        var sw = Stopwatch.StartNew();

        try
        {
            await action();
            sw.Stop();
            await SafeLogAsync(actionName, "", "Success", sw.ElapsedMilliseconds);
            await LoadIisInfoAsync();
        }
        catch (Exception ex)
        {
            sw.Stop();
            HasError = true;
            ErrorMessage = $"{actionName} failed: {ex.Message}";
            StatusText = "Error";
            await SafeLogAsync(actionName, ex.Message, "Failed", sw.ElapsedMilliseconds);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task SafeLogAsync(string operation, string description, string result, long duration)
    {
        try { await _history.LogAsync(operation, description, result, duration, _currentUser.Username); }
        catch { /* logging is non-critical */ }
    }
}


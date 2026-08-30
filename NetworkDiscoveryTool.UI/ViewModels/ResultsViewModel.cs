using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using NetworkDiscoveryTool.Core.Models;
using NetworkDiscoveryTool.Data;

namespace NetworkDiscoveryTool.UI.ViewModels;

public sealed partial class ResultsViewModel : ObservableObject
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly Services.CurrentUserService _currentUser;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _statusFilter = "All";

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<Device> Devices { get; } = [];
    public ObservableCollection<Device> FilteredDevices { get; } = [];

    public string[] StatusFilters { get; } = ["All", "Online", "Offline"];

    public ResultsViewModel(IDbContextFactory<AppDbContext> contextFactory, Services.CurrentUserService currentUser)
    {
        _contextFactory = contextFactory;
        _currentUser = currentUser;
        _currentUser.UserChanged += () =>
        {
            Devices.Clear();
            FilteredDevices.Clear();
            _ = LoadLatestResultsAsync();
        };
    }

    [RelayCommand]
    private async Task LoadLatestResultsAsync()
    {
        IsLoading = true;

        await using var context = await _contextFactory.CreateDbContextAsync();

        var query = context.Scans.AsQueryable();
        if (_currentUser.UserId > 0)
        {
            query = query.Where(s => s.UserId == _currentUser.UserId);
        }
        else if (_currentUser.IsAdmin)
        {
            query = query.Where(s => s.UserId == null || s.UserId == 0);
        }
        else
        {
            query = query.Where(s => false);
        }

        var latest = await query
            .OrderByDescending(s => s.Date)
            .FirstOrDefaultAsync();

        Devices.Clear();
        if (latest is not null)
        {
            var devices = await context.Devices
                .Include(d => d.Ports)
                .Where(d => d.ScanId == latest.Id)
                .ToListAsync();

            foreach (var d in devices) Devices.Add(d);
        }

        ApplyFilter();
        IsLoading = false;
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnStatusFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        FilteredDevices.Clear();

        var query = Devices.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.ToLowerInvariant();
            query = query.Where(d =>
                (d.IP?.Contains(search) ?? false) ||
                (d.Hostname?.Contains(search) ?? false) ||
                (d.MAC?.Contains(search) ?? false) ||
                (d.Vendor?.Contains(search) ?? false) ||
                (d.DeviceType?.Contains(search) ?? false));
        }

        if (StatusFilter != "All")
            query = query.Where(d => d.Status == StatusFilter);

        foreach (var d in query)
            FilteredDevices.Add(d);
    }
}

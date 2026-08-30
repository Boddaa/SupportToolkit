using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using NetworkDiscoveryTool.Core.Models;
using NetworkDiscoveryTool.Data;

namespace NetworkDiscoveryTool.UI.ViewModels;

public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly Services.CurrentUserService _currentUser;

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<Scan> Scans { get; } = [];
    public ObservableCollection<Device> SelectedScanDevices { get; } = [];

    public bool IsAdmin => _currentUser.IsAdmin;

    public HistoryViewModel(IDbContextFactory<AppDbContext> contextFactory, Services.CurrentUserService currentUser)
    {
        _contextFactory = contextFactory;
        _currentUser = currentUser;
        _currentUser.UserChanged += () =>
        {
            OnPropertyChanged(nameof(IsAdmin));
            Scans.Clear();
            SelectedScanDevices.Clear();
            _ = LoadHistoryAsync();
        };
    }

    [RelayCommand]
    private async Task LoadHistoryAsync()
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

        var scans = await query
            .OrderByDescending(s => s.Date)
            .ToListAsync();

        Scans.Clear();
        foreach (var s in scans) Scans.Add(s);

        IsLoading = false;
    }

    [RelayCommand]
    private async Task DeleteScanAsync(Scan? scan)
    {
        if (scan is null || !IsAdmin) return;

        await using var context = await _contextFactory.CreateDbContextAsync();
        context.Scans.Remove(scan);
        await context.SaveChangesAsync();
        await LoadHistoryAsync();
    }

    [RelayCommand]
    private async Task ClearAllHistoryAsync()
    {
        if (!IsAdmin) return;

        await using var context = await _contextFactory.CreateDbContextAsync();
        context.Scans.RemoveRange(context.Scans);
        await context.SaveChangesAsync();
        await LoadHistoryAsync();
    }

    [RelayCommand]
    private async Task SelectScanAsync(Scan? scan)
    {
        if (scan is null) return;

        SelectedScanDevices.Clear();

        await using var context = await _contextFactory.CreateDbContextAsync();

        var devices = await context.Devices
            .Include(d => d.Ports)
            .Where(d => d.ScanId == scan.Id)
            .ToListAsync();

        foreach (var d in devices)
            SelectedScanDevices.Add(d);
    }
}

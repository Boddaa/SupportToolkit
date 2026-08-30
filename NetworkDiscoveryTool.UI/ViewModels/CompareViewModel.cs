using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using NetworkDiscoveryTool.Core.Models;
using NetworkDiscoveryTool.Data;

namespace NetworkDiscoveryTool.UI.ViewModels;

public sealed partial class CompareViewModel : ObservableObject
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly Services.CurrentUserService _currentUser;

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<Scan> Scans { get; } = [];

    [ObservableProperty]
    private Scan? _scanA;

    [ObservableProperty]
    private Scan? _scanB;

    public ObservableCollection<Device> ScanADevices { get; } = [];
    public ObservableCollection<Device> ScanBDevices { get; } = [];

    [ObservableProperty]
    private int _newDevices;

    [ObservableProperty]
    private int _missingDevices;

    [ObservableProperty]
    private int _changedHostname;

    [ObservableProperty]
    private int _changedVendor;

    [ObservableProperty]
    private int _changedStatus;

    [ObservableProperty]
    private int _unchangedDevices;

    public CompareViewModel(IDbContextFactory<AppDbContext> contextFactory, Services.CurrentUserService currentUser)
    {
        _contextFactory = contextFactory;
        _currentUser = currentUser;
        _currentUser.UserChanged += () =>
        {
            Scans.Clear();
            ScanA = null;
            ScanB = null;
            ScanADevices.Clear();
            ScanBDevices.Clear();
            _ = LoadScansAsync();
        };
    }

    [RelayCommand]
    private async Task LoadScansAsync()
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

    [ObservableProperty]
    private string _compareStatus = string.Empty;

    [RelayCommand]
    private async Task CompareAsync()
    {
        if (ScanA is null || ScanB is null)
        {
            CompareStatus = "Please select both scans";
            return;
        }

        if (ScanA.Id == ScanB.Id)
        {
            CompareStatus = "Cannot compare a scan with itself";
            return;
        }

        CompareStatus = "Comparing...";
        IsLoading = true;

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var devicesA = await context.Devices
                .Include(d => d.Ports)
                .Where(d => d.ScanId == ScanA.Id)
                .ToListAsync();

            var devicesB = await context.Devices
                .Include(d => d.Ports)
                .Where(d => d.ScanId == ScanB.Id)
                .ToListAsync();

            ScanADevices.Clear();
            ScanBDevices.Clear();
            foreach (var d in devicesA) ScanADevices.Add(d);
            foreach (var d in devicesB) ScanBDevices.Add(d);

            var dictA = devicesA.ToDictionary(d => d.IP);
            var dictB = devicesB.ToDictionary(d => d.IP);

            var ipsA = dictA.Keys.ToHashSet();
            var ipsB = dictB.Keys.ToHashSet();

            NewDevices = ipsB.Except(ipsA).Count();
            MissingDevices = ipsA.Except(ipsB).Count();
            var common = ipsA.Intersect(ipsB);

            int hostnameChanged = 0, vendorChanged = 0, statusChanged = 0, unchanged = 0;

            foreach (var ip in common)
            {
                var a = dictA[ip];
                var b = dictB[ip];

                bool changed = false;
                if (a.Hostname != b.Hostname) { hostnameChanged++; changed = true; }
                if (a.Vendor != b.Vendor) { vendorChanged++; changed = true; }
                if (a.Status != b.Status) { statusChanged++; changed = true; }

                if (!changed) unchanged++;
            }

            ChangedHostname = hostnameChanged;
            ChangedVendor = vendorChanged;
            ChangedStatus = statusChanged;
            UnchangedDevices = unchanged;

            CompareStatus = "Comparison complete";
        }
        catch (Exception ex)
        {
            CompareStatus = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}

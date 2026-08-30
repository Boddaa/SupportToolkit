using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetworkDiscoveryTool.Core.Models;
using NetworkDiscoveryTool.UI.Services;

namespace NetworkDiscoveryTool.UI.ViewModels;

public sealed partial class OperationHistoryViewModel : ObservableObject
{
    private readonly IOperationHistoryService _history;
    private readonly CurrentUserService _currentUser;
    private List<OperationLog> _allEntries = [];

    public OperationHistoryViewModel(IOperationHistoryService history, CurrentUserService currentUser)
    {
        _history = history;
        _currentUser = currentUser;
        IsAdmin = currentUser.IsAdmin;
        _currentUser.UserChanged += () =>
        {
            IsAdmin = _currentUser.IsAdmin;
            _allEntries.Clear();
            Entries.Clear();
            _ = LoadHistoryAsync();
        };
    }

    [ObservableProperty] private bool _isAdmin;

    // === Filters ===
    [ObservableProperty] private string? _filterOperation;
    [ObservableProperty] private string? _filterUsername;
    [ObservableProperty] private string? _filterResult;
    [ObservableProperty] private DateTime? _filterFrom;
    [ObservableProperty] private DateTime? _filterTo;

    public ObservableCollection<string> OperationNames { get; } = [];
    public ObservableCollection<string> ResultOptions { get; } = ["All", "Success", "Failed"];

    // === History ===
    public ObservableCollection<OperationLog> Entries { get; } = [];

    // === Stats ===
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _filteredCount;
    [ObservableProperty] private string _operationStatsText = "";

    // === UI State ===
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private OperationLog? _selectedEntry;

    // === Load ===

    [RelayCommand]
    private async Task LoadHistoryAsync()
    {
        IsLoading = true;

        try
        {
            var raw = await _history.GetAllAsync();
            if (!IsAdmin)
            {
                _allEntries = raw.Where(e => string.Equals(e.Username, _currentUser.Username, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            else
            {
                _allEntries = raw;
            }

            TotalCount = _allEntries.Count;

            OperationNames.Clear();
            foreach (var name in _allEntries.Select(e => e.OperationName).Distinct().Order())
                OperationNames.Add(name);

            ApplyFilters();
            await LoadStatsAsync();
            StatusText = $"Loaded {TotalCount} operation(s)";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // === Search / Filter ===

    [RelayCommand]
    private async Task SearchAsync()
    {
        IsLoading = true;

        try
        {
            var operation = string.IsNullOrWhiteSpace(FilterOperation) || FilterOperation == "All" ? null : FilterOperation;
            var username = string.IsNullOrWhiteSpace(FilterUsername) ? null : FilterUsername;
            var result = string.IsNullOrWhiteSpace(FilterResult) || FilterResult == "All" ? null : FilterResult;

            _allEntries = await _history.SearchAsync(operation, username, result, FilterFrom, FilterTo);
            TotalCount = _allEntries.Count;
            ApplyFilters();
            StatusText = $"Found {FilteredCount} operation(s)";
        }
        catch (Exception ex)
        {
            StatusText = $"Search error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        FilterOperation = null;
        FilterUsername = null;
        FilterResult = null;
        FilterFrom = null;
        FilterTo = null;
        _ = LoadHistoryAsync();
    }

    // === Actions ===

    [RelayCommand]
    private async Task DeleteEntryAsync()
    {
        if (SelectedEntry is null) return;
        if (!IsAdmin)
        {
            StatusText = "Permission denied: Administrator role required.";
            return;
        }

        try
        {
            await _history.DeleteAsync(SelectedEntry.Id);
            Entries.Remove(SelectedEntry);
            FilteredCount = Entries.Count;
            TotalCount = _allEntries.Count;
            StatusText = "Entry deleted";
        }
        catch (Exception ex)
        {
            StatusText = $"Delete failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ClearAllAsync()
    {
        if (!IsAdmin)
        {
            StatusText = "Permission denied: Administrator role required.";
            return;
        }

        var result = System.Windows.MessageBox.Show(
            "Delete all operation history? This cannot be undone.",
            "Confirm Clear",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            await _history.ClearAllAsync();
            _allEntries.Clear();
            Entries.Clear();
            TotalCount = 0;
            FilteredCount = 0;
            OperationStatsText = "";
            StatusText = "History cleared";
        }
        catch (Exception ex)
        {
            StatusText = $"Clear failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportToExcelAsync()
    {
        if (Entries.Count == 0)
        {
            StatusText = "No entries to export";
            return;
        }

        var dlg = new System.Windows.Forms.SaveFileDialog
        {
            Filter = "Excel Files|*.xlsx",
            FileName = $"OperationHistory_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
        };

        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        try
        {
            await _history.ExportToExcelAsync([.. Entries], dlg.FileName);
            StatusText = $"Exported to {dlg.FileName}";
        }
        catch (Exception ex)
        {
            StatusText = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Refresh() => _ = LoadHistoryAsync();

    // === Internal ===

    private void ApplyFilters()
    {
        Entries.Clear();
        foreach (var entry in _allEntries)
            Entries.Add(entry);
        FilteredCount = Entries.Count;
    }

    private async Task LoadStatsAsync()
    {
        try
        {
            var stats = await _history.GetOperationStatsAsync();
            OperationStatsText = string.Join(" · ", stats.OrderByDescending(s => s.Value).Select(s => $"{s.Key}: {s.Value}"));
        }
        catch
        {
            OperationStatsText = "";
        }
    }
}

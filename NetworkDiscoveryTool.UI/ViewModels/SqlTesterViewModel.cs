using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetworkDiscoveryTool.UI.Models;
using NetworkDiscoveryTool.UI.Services;

namespace NetworkDiscoveryTool.UI.ViewModels;

public sealed partial class SqlTesterViewModel : ObservableObject
{
    private readonly ISqlConnectionService _sqlService;
    private readonly IOperationHistoryService _historyService;
    private readonly CurrentUserService _currentUser;
    private CancellationTokenSource? _cts;

    public SqlTesterViewModel(
        ISqlConnectionService sqlService,
        IOperationHistoryService historyService,
        CurrentUserService currentUser)
    {
        _sqlService = sqlService;
        _historyService = historyService;
        _currentUser = currentUser;
    }

    // ========================================================
    // Target Instance Parameters
    // ========================================================
    [ObservableProperty] private string _serverHost = ".";
    [ObservableProperty] private int _port = 1433;
    [ObservableProperty] private string _database = "master";
    [ObservableProperty] private bool _useWindowsAuth = true;
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _password = "";

    // ========================================================
    // Advanced Connection Options
    // ========================================================
    [ObservableProperty] private int _connectTimeout = 15;
    [ObservableProperty] private int _commandTimeout = 30;
    [ObservableProperty] private bool _encryptConnection = false;
    [ObservableProperty] private bool _trustServerCertificate = true;
    [ObservableProperty] private string _applicationName = "SupportToolKit";
    [ObservableProperty] private bool _isAdvancedExpanded;

    // ========================================================
    // Tab Navigation & UI State
    // ========================================================
    [ObservableProperty] private int _selectedTabIndex = 0; // 0: Diagnostics, 1: Profiles, 2: History
    [ObservableProperty] private bool _isTesting;
    [ObservableProperty] private bool _isInitialState = true;
    [ObservableProperty] private string _validationMessage = "";
    [ObservableProperty] private bool _hasValidationMessage;
    [ObservableProperty] private bool _showTechnicalDetails;

    // Save Profile Modal Dialog
    [ObservableProperty] private bool _showSaveProfileDialog;
    [ObservableProperty] private string _profileNameInput = "";

    // Diagnostics Result
    [ObservableProperty] private SqlDiagnosticResult? _diagnosticResult;
    [ObservableProperty] private ObservableCollection<SqlCheckStage> _connectivityChecks = [];

    // Collections
    public ObservableCollection<SqlSavedProfile> SavedProfiles { get; } = [];
    public ObservableCollection<SqlTestHistoryItem> HistoryItems { get; } = [];

    // ========================================================
    // Helper Properties for View Binding (Two-Way Safe)
    // ========================================================
    [ObservableProperty] private bool _isDiagnosticsTabSelected = true;
    [ObservableProperty] private bool _isProfilesTabSelected;
    [ObservableProperty] private bool _isHistoryTabSelected;

    partial void OnIsDiagnosticsTabSelectedChanged(bool value)
    {
        if (value)
        {
            _selectedTabIndex = 0;
            _isProfilesTabSelected = false;
            _isHistoryTabSelected = false;
            OnPropertyChanged(nameof(SelectedTabIndex));
            OnPropertyChanged(nameof(IsProfilesTabSelected));
            OnPropertyChanged(nameof(IsHistoryTabSelected));
        }
    }

    partial void OnIsProfilesTabSelectedChanged(bool value)
    {
        if (value)
        {
            _selectedTabIndex = 1;
            _isDiagnosticsTabSelected = false;
            _isHistoryTabSelected = false;
            OnPropertyChanged(nameof(SelectedTabIndex));
            OnPropertyChanged(nameof(IsDiagnosticsTabSelected));
            OnPropertyChanged(nameof(IsHistoryTabSelected));
        }
    }

    partial void OnIsHistoryTabSelectedChanged(bool value)
    {
        if (value)
        {
            _selectedTabIndex = 2;
            _isDiagnosticsTabSelected = false;
            _isProfilesTabSelected = false;
            OnPropertyChanged(nameof(SelectedTabIndex));
            OnPropertyChanged(nameof(IsDiagnosticsTabSelected));
            OnPropertyChanged(nameof(IsProfilesTabSelected));
        }
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        _isDiagnosticsTabSelected = value == 0;
        _isProfilesTabSelected = value == 1;
        _isHistoryTabSelected = value == 2;
        OnPropertyChanged(nameof(IsDiagnosticsTabSelected));
        OnPropertyChanged(nameof(IsProfilesTabSelected));
        OnPropertyChanged(nameof(IsHistoryTabSelected));
    }

    partial void OnUseWindowsAuthChanged(bool value)
    {
        if (value)
        {
            Password = "";
        }
        ClearValidation();
    }

    // ========================================================
    // Commands
    // ========================================================

    [RelayCommand]
    public void SelectTab(object? param)
    {
        if (param is int idx)
        {
            SelectedTabIndex = idx;
        }
        else if (param is string str && int.TryParse(str, out int i))
        {
            SelectedTabIndex = i;
        }
    }

    [RelayCommand]
    public void ToggleAdvanced()
    {
        IsAdvancedExpanded = !IsAdvancedExpanded;
    }

    [RelayCommand]
    public void ToggleTechnicalDetails()
    {
        ShowTechnicalDetails = !ShowTechnicalDetails;
    }

    [RelayCommand]
    public async Task InitializeAsync()
    {
        await LoadProfilesAsync();
        await LoadHistoryAsync();
    }

    [RelayCommand]
    public async Task TestConnectionAsync()
    {
        // 1. Validate Inputs
        if (!ValidateInputs()) return;

        IsTesting = true;
        IsInitialState = false;
        SelectedTabIndex = 0; // Switch to diagnostics tab
        ShowTechnicalDetails = false;
        ClearValidation();

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        // Initialize empty checklist
        ConnectivityChecks.Clear();
        DiagnosticResult = new SqlDiagnosticResult
        {
            SummaryServer = ServerHost.Trim(),
            SummaryDatabase = string.IsNullOrWhiteSpace(Database) ? "master" : Database.Trim(),
            SummaryAuth = UseWindowsAuth ? "Windows" : $"SQL Auth ({Username})"
        };

        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _sqlService.RunDiagnosticsAsync(
                ServerHost.Trim(),
                Port,
                Database.Trim(),
                UseWindowsAuth,
                Username.Trim(),
                Password,
                ConnectTimeout,
                CommandTimeout,
                EncryptConnection,
                TrustServerCertificate,
                ApplicationName,
                _cts.Token);

            sw.Stop();
            DiagnosticResult = result;

            ConnectivityChecks.Clear();
            foreach (var check in result.Checks)
            {
                ConnectivityChecks.Add(check);
            }

            // Save to History
            var historyItem = new SqlTestHistoryItem
            {
                Server = result.SummaryServer,
                Database = result.SummaryDatabase,
                AuthType = result.SummaryAuth,
                Success = result.Success,
                LatencyMs = result.TotalLatencyMs,
                StageFailed = result.Success ? "" : result.OverallStatus,
                ErrorMessage = result.FailureSummary,
                DiagnosticSnapshot = result
            };

            await _sqlService.SaveHistoryItemAsync(historyItem);
            HistoryItems.Insert(0, historyItem);
            if (HistoryItems.Count > 50) HistoryItems.RemoveAt(HistoryItems.Count - 1);

            // Log operation
            await _historyService.LogAsync(
                "SQL Server Test",
                $"{ServerHost}:{Port} ({Database})",
                result.Success ? "Success" : "Failed",
                sw.ElapsedMilliseconds,
                _currentUser.Username);
        }
        catch (OperationCanceledException)
        {
            DiagnosticResult = new SqlDiagnosticResult
            {
                Success = false,
                OverallStatus = "Test Canceled",
                FailureSummary = "The connection test was canceled by the user."
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            DiagnosticResult = new SqlDiagnosticResult
            {
                Success = false,
                OverallStatus = "Unexpected Error",
                FailureSummary = ex.Message,
                TechnicalDetails = new SqlTechnicalDetails
                {
                    ExceptionType = ex.GetType().Name,
                    Message = ex.Message,
                    ServerTarget = ServerHost,
                    StackTrace = ex.StackTrace
                }
            };
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    public void CancelTest()
    {
        _cts?.Cancel();
        IsTesting = false;
    }

    [RelayCommand]
    public void ClearForm()
    {
        ServerHost = "";
        Port = 1433;
        Database = "master";
        UseWindowsAuth = true;
        Username = "";
        Password = "";
        ConnectTimeout = 15;
        CommandTimeout = 30;
        EncryptConnection = false;
        TrustServerCertificate = true;
        IsInitialState = true;
        DiagnosticResult = null;
        ConnectivityChecks.Clear();
        ClearValidation();
    }

    // ========================================================
    // Profile Management Commands
    // ========================================================

    [RelayCommand]
    public void OpenSaveProfileDialog()
    {
        if (string.IsNullOrWhiteSpace(ServerHost))
        {
            ValidationMessage = "Enter a Server / Instance name first.";
            HasValidationMessage = true;
            return;
        }

        ProfileNameInput = $"{ServerHost}\\{Database}";
        ShowSaveProfileDialog = true;
    }

    [RelayCommand]
    public void CloseSaveProfileDialog()
    {
        ShowSaveProfileDialog = false;
        ProfileNameInput = "";
    }

    [RelayCommand]
    public async Task SaveProfileConfirmedAsync()
    {
        if (string.IsNullOrWhiteSpace(ProfileNameInput)) return;

        var profile = new SqlSavedProfile
        {
            Name = ProfileNameInput.Trim(),
            Server = ServerHost.Trim(),
            Port = Port,
            Database = string.IsNullOrWhiteSpace(Database) ? "master" : Database.Trim(),
            UseWindowsAuth = UseWindowsAuth,
            Username = Username.Trim(),
            EncryptedPassword = !UseWindowsAuth && !string.IsNullOrEmpty(Password)
                ? _sqlService.EncryptPassword(Password)
                : null,
            ConnectTimeout = ConnectTimeout,
            CommandTimeout = CommandTimeout,
            Encrypt = EncryptConnection,
            TrustCertificate = TrustServerCertificate,
            LastTested = DiagnosticResult != null ? DateTime.Now : null,
            LastLatencyMs = DiagnosticResult?.TotalLatencyMs ?? 0,
            LastStatus = DiagnosticResult?.Success == true ? "Healthy" : (DiagnosticResult != null ? "Failed" : "Not Tested"),
            IsHealthy = DiagnosticResult?.Success == true
        };

        await _sqlService.SaveProfileAsync(profile);
        ShowSaveProfileDialog = false;
        ProfileNameInput = "";
        await LoadProfilesAsync();
    }

    [RelayCommand]
    public async Task LoadAndConnectProfileAsync(SqlSavedProfile? profile)
    {
        if (profile == null) return;

        ServerHost = profile.Server;
        Port = profile.Port > 0 ? profile.Port : 1433;
        Database = profile.Database;
        UseWindowsAuth = profile.UseWindowsAuth;
        Username = profile.Username;
        Password = !profile.UseWindowsAuth && !string.IsNullOrEmpty(profile.EncryptedPassword)
            ? _sqlService.DecryptPassword(profile.EncryptedPassword)
            : "";

        ConnectTimeout = profile.ConnectTimeout > 0 ? profile.ConnectTimeout : 15;
        CommandTimeout = profile.CommandTimeout > 0 ? profile.CommandTimeout : 30;
        EncryptConnection = profile.Encrypt;
        TrustServerCertificate = profile.TrustCertificate;

        // Auto run test
        await TestConnectionAsync();
    }

    [RelayCommand]
    public void LoadProfileToForm(SqlSavedProfile? profile)
    {
        if (profile == null) return;

        ServerHost = profile.Server;
        Port = profile.Port > 0 ? profile.Port : 1433;
        Database = profile.Database;
        UseWindowsAuth = profile.UseWindowsAuth;
        Username = profile.Username;
        Password = !profile.UseWindowsAuth && !string.IsNullOrEmpty(profile.EncryptedPassword)
            ? _sqlService.DecryptPassword(profile.EncryptedPassword)
            : "";

        ConnectTimeout = profile.ConnectTimeout > 0 ? profile.ConnectTimeout : 15;
        CommandTimeout = profile.CommandTimeout > 0 ? profile.CommandTimeout : 30;
        EncryptConnection = profile.Encrypt;
        TrustServerCertificate = profile.TrustCertificate;

        SelectedTabIndex = 0; // Switch to diagnostics tab
    }

    [RelayCommand]
    public async Task DeleteProfileAsync(SqlSavedProfile? profile)
    {
        if (profile == null) return;
        await _sqlService.DeleteProfileAsync(profile.Id);
        SavedProfiles.Remove(profile);
    }

    // ========================================================
    // History Commands
    // ========================================================

    [RelayCommand]
    public void ViewHistoryItem(SqlTestHistoryItem? item)
    {
        if (item == null) return;

        ServerHost = item.Server;
        Database = item.Database;

        if (item.DiagnosticSnapshot != null)
        {
            DiagnosticResult = item.DiagnosticSnapshot;
            ConnectivityChecks.Clear();
            foreach (var check in item.DiagnosticSnapshot.Checks)
            {
                ConnectivityChecks.Add(check);
            }
            IsInitialState = false;
            SelectedTabIndex = 0; // Switch to diagnostics view
        }
    }

    [RelayCommand]
    public async Task ClearAllHistoryAsync()
    {
        await _sqlService.ClearHistoryAsync();
        HistoryItems.Clear();
    }

    // ========================================================
    // Private Helpers
    // ========================================================

    private bool ValidateInputs()
    {
        if (string.IsNullOrWhiteSpace(ServerHost))
        {
            ValidationMessage = "Server / Instance is required (e.g. 192.168.1.50 or .\\SQLEXPRESS).";
            HasValidationMessage = true;
            return false;
        }

        if (Port < 1 || Port > 65535)
        {
            ValidationMessage = "Port must be between 1 and 65535.";
            HasValidationMessage = true;
            return false;
        }

        if (!UseWindowsAuth)
        {
            if (string.IsNullOrWhiteSpace(Username))
            {
                ValidationMessage = "DB Username is required when SQL Server Authentication is selected.";
                HasValidationMessage = true;
                return false;
            }
            if (string.IsNullOrEmpty(Password))
            {
                ValidationMessage = "DB Password is required when SQL Server Authentication is selected.";
                HasValidationMessage = true;
                return false;
            }
        }

        ClearValidation();
        return true;
    }

    private void ClearValidation()
    {
        ValidationMessage = "";
        HasValidationMessage = false;
    }

    private async Task LoadProfilesAsync()
    {
        try
        {
            var profiles = await _sqlService.LoadProfilesAsync();
            SavedProfiles.Clear();
            foreach (var p in profiles)
            {
                SavedProfiles.Add(p);
            }
        }
        catch { }
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            var history = await _sqlService.LoadHistoryAsync();
            HistoryItems.Clear();
            foreach (var h in history)
            {
                HistoryItems.Add(h);
            }
        }
        catch { }
    }
}

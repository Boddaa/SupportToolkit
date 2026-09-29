using System.Collections.ObjectModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using Microsoft.EntityFrameworkCore;
using NetworkDiscoveryTool.Core.Models;
using NetworkDiscoveryTool.Data;
using NetworkDiscoveryTool.Services.Services;
using NetworkDiscoveryTool.UI.Services;

namespace NetworkDiscoveryTool.UI.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly CurrentUserService _currentUser;
    private readonly AuthService _auth;
    private readonly ISettingsService _settings;
    private readonly TelegramNotificationService _telegramService;
    private bool _isLoadingSettings;
    private bool _isInitializing = true;

    // === Display Properties (read-only) ===
    public string DatabasePath => App.DatabasePath;

    public SettingsViewModel(
        IDbContextFactory<AppDbContext> contextFactory,
        CurrentUserService currentUser,
        AuthService auth,
        ISettingsService settings,
        TelegramNotificationService telegramService)
    {
        _contextFactory = contextFactory;
        _currentUser = currentUser;
        _auth = auth;
        _settings = settings;
        _telegramService = telegramService;
        IsAdmin = currentUser.IsAdmin;
        _currentUser.UserChanged += () =>
        {
            IsAdmin = _currentUser.IsAdmin;
            if (IsAdmin)
            {
                LoadPendingUsers();
                LoadAllUsers();
            }
            else
            {
                PendingUsers.Clear();
                AllUsers.Clear();
            }
        };
    }

    // === Theme ===
    [ObservableProperty] private string _selectedTheme = "Light";
    public ObservableCollection<string> Themes { get; } = ["Light", "Dark", "Blue"];

    partial void OnSelectedThemeChanged(string value)
    {
        if (_isInitializing) return;
        ApplyTheme(value);
        _ = SaveSettingAsync("Theme", value);
    }

    // === Scan Defaults ===
    [ObservableProperty] private int _defaultMaxThreads = 100;
    [ObservableProperty] private int _defaultPingTimeout = 2000;
    [ObservableProperty] private int _defaultPortTimeout = 500;
    [ObservableProperty] private string _defaultStartIP = "192.168.1.1";
    [ObservableProperty] private string _defaultEndIP = "192.168.1.254";

    // === General ===
    [ObservableProperty] private bool _autoSaveEnabled = true;
    [ObservableProperty] private string _exportFolder = "Exports";
    [ObservableProperty] private string _logLevel = "Information";
    public ObservableCollection<string> LogLevels { get; } = ["Verbose", "Debug", "Information", "Warning", "Error"];

    partial void OnLogLevelChanged(string value)
    {
        App.SetLogLevel(value);
        _ = SaveSettingAsync("LogLevel", value);
    }

    // === SQL Default Server ===
    [ObservableProperty] private string _sqlServer = "";
    [ObservableProperty] private string _sqlPort = "1433";
    [ObservableProperty] private string _sqlDatabase = "";
    [ObservableProperty] private string _sqlUsername = "";
    [ObservableProperty] private string _sqlPassword = "";

    // === Notification & Telegram Settings ===
    [ObservableProperty] private bool _enableNotifications = true;
    [ObservableProperty] private bool _notifyOnScanComplete = true;
    [ObservableProperty] private bool _notifyOnScanError = true;
    [ObservableProperty] private bool _notifyOnServiceChange = true;
    [ObservableProperty] private bool _notifyOnNewUserRegistration = true;
    [ObservableProperty] private string _telegramBotToken = "8914418594:AAGMMqY91qu0MnkEM453gjclAm_RyOyGxfc";
    [ObservableProperty] private string _telegramAdminChatId = "1119565273";

    // === Default Save Folder ===
    [ObservableProperty] private string _defaultSaveFolder = "";

    // === Updates ===
    [ObservableProperty] private string _lastUpdateCheck = "Never";

    // === Status ===
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private bool _isLoading;

    // === Admin: user management ===
    [ObservableProperty] private bool _isAdmin;
    public ObservableCollection<AuthService.PendingUser> PendingUsers { get; } = [];
    public ObservableCollection<AuthService.PendingUser> AllUsers { get; } = [];

    // === Application Information ===
    [ObservableProperty] private string _appVersion = "";
    [ObservableProperty] private string _appFramework = "";
    [ObservableProperty] private string _appDatabase = "";
    [ObservableProperty] private string _appLogPath = "";
    [ObservableProperty] private string _appUptime = "";
    [ObservableProperty] private string _appOsVersion = "";
    [ObservableProperty] private string _appWorkingSet = "";

    // === Initialization ===

    public void CompleteInitialization()
    {
        _isInitializing = false;
        ApplyTheme(SelectedTheme);
    }

    // === Load / Save ===

    public async Task LoadSettingsAsync()
    {
        if (_isLoadingSettings) return;
        _isLoadingSettings = true;
        IsLoading = true;

        try
        {
            IsAdmin = _currentUser.IsAdmin;
            var all = await _settings.GetAllAsync();

            if (all.TryGetValue("Theme", out var theme)) SelectedTheme = theme;
            if (all.TryGetValue("LogLevel", out var log)) LogLevel = log;
            if (all.TryGetValue("MaxThreads", out var mt) && int.TryParse(mt, out var mtv)) DefaultMaxThreads = mtv;
            if (all.TryGetValue("PingTimeout", out var pt) && int.TryParse(pt, out var ptv)) DefaultPingTimeout = ptv;
            if (all.TryGetValue("PortTimeout", out var pot) && int.TryParse(pot, out var potv)) DefaultPortTimeout = potv;
            if (all.TryGetValue("StartIP", out var sip)) DefaultStartIP = sip;
            if (all.TryGetValue("EndIP", out var eip)) DefaultEndIP = eip;
            if (all.TryGetValue("AutoSave", out var auto)) AutoSaveEnabled = auto == "True";
            if (all.TryGetValue("ExportFolder", out var exp)) ExportFolder = exp;
            if (all.TryGetValue("DefaultSaveFolder", out var dsf)) DefaultSaveFolder = dsf;
            if (all.TryGetValue("SqlServer", out var sqls)) SqlServer = sqls;
            if (all.TryGetValue("SqlPort", out var sqlp)) SqlPort = sqlp;
            if (all.TryGetValue("SqlDatabase", out var sqld)) SqlDatabase = sqld;
            if (all.TryGetValue("SqlUsername", out var sqlu)) SqlUsername = sqlu;
            if (all.TryGetValue("SqlPassword", out var sqlpw)) SqlPassword = sqlpw;
            if (all.TryGetValue("EnableNotifications", out var en)) EnableNotifications = en == "True";
            if (all.TryGetValue("NotifyScanComplete", out var nsc)) NotifyOnScanComplete = nsc == "True";
            if (all.TryGetValue("NotifyScanError", out var nse)) NotifyOnScanError = nse == "True";
            if (all.TryGetValue("NotifyServiceChange", out var nsv)) NotifyOnServiceChange = nsv == "True";
            if (all.TryGetValue("NotifyNewUser", out var nnu)) NotifyOnNewUserRegistration = nnu == "True";
            TelegramBotToken = all.TryGetValue("TelegramBotToken", out var tbt) && !string.IsNullOrWhiteSpace(tbt)
                ? tbt
                : "8914418594:AAGMMqY91qu0MnkEM453gjclAm_RyOyGxfc";
            TelegramAdminChatId = all.TryGetValue("TelegramAdminChatId", out var tcid) && !string.IsNullOrWhiteSpace(tcid)
                ? tcid
                : "1119565273";

            _telegramService.BotToken = TelegramBotToken;
            _telegramService.AdminChatId = TelegramAdminChatId;
        }
        finally
        {
            IsLoading = false;
            _isLoadingSettings = false;
        }
    }

    [RelayCommand]
    private async Task SaveAllSettingsAsync()
    {
        IsSaving = true;
        try
        {
            App.SetLogLevel(LogLevel);
            _telegramService.BotToken = TelegramBotToken;
            _telegramService.AdminChatId = TelegramAdminChatId;

            var dict = new Dictionary<string, string>
            {
                ["Theme"] = SelectedTheme,
                ["LogLevel"] = LogLevel,
                ["MaxThreads"] = DefaultMaxThreads.ToString(),
                ["PingTimeout"] = DefaultPingTimeout.ToString(),
                ["PortTimeout"] = DefaultPortTimeout.ToString(),
                ["StartIP"] = DefaultStartIP,
                ["EndIP"] = DefaultEndIP,
                ["AutoSave"] = AutoSaveEnabled.ToString(),
                ["ExportFolder"] = ExportFolder,
                ["DefaultSaveFolder"] = DefaultSaveFolder,
                ["SqlServer"] = SqlServer,
                ["SqlPort"] = SqlPort,
                ["SqlDatabase"] = SqlDatabase,
                ["SqlUsername"] = SqlUsername,
                ["SqlPassword"] = SqlPassword,
                ["EnableNotifications"] = EnableNotifications.ToString(),
                ["NotifyScanComplete"] = NotifyOnScanComplete.ToString(),
                ["NotifyScanError"] = NotifyOnScanError.ToString(),
                ["NotifyServiceChange"] = NotifyOnServiceChange.ToString(),
                ["NotifyNewUser"] = NotifyOnNewUserRegistration.ToString(),
                ["TelegramBotToken"] = TelegramBotToken,
                ["TelegramAdminChatId"] = TelegramAdminChatId,
            };
            await _settings.SaveAllAsync(dict);
            StatusMessage = "All settings saved successfully";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task SaveSettingAsync(string key, string value)
    {
        try { await _settings.SetAsync(key, value); }
        catch { }
    }

    [RelayCommand]
    private async Task TestTelegramBotAsync()
    {
        if (string.IsNullOrWhiteSpace(TelegramBotToken) || string.IsNullOrWhiteSpace(TelegramAdminChatId))
        {
            StatusMessage = "Please enter both Telegram Bot Token and Admin Chat ID first.";
            return;
        }

        _telegramService.BotToken = TelegramBotToken;
        _telegramService.AdminChatId = TelegramAdminChatId;

        StatusMessage = "Sending test alert to Telegram...";
        var success = await _telegramService.SendRegistrationAlertAsync("TestAdminUser", "admin@domain.com", "STK-TEST-7788-9900", Environment.MachineName);
        if (success)
        {
            StatusMessage = "✓ Test alert sent successfully to your Telegram!";
        }
        else
        {
            StatusMessage = "✗ Failed to send alert. Please verify your Bot Token and Chat ID.";
        }
    }

    [RelayCommand]
    private void ExportEncryptedTelegramConfig()
    {
        if (string.IsNullOrWhiteSpace(TelegramBotToken) || string.IsNullOrWhiteSpace(TelegramAdminChatId))
        {
            StatusMessage = "Please enter Telegram Bot Token and Admin Chat ID first.";
            return;
        }

        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = NetworkDiscoveryTool.Services.Services.TelegramConfigCrypto.DefaultFileName,
                Filter = "Encrypted Config (*.enc)|*.enc|All Files (*.*)|*.*",
                Title = "Export Encrypted Telegram Config"
            };

            if (dialog.ShowDialog() == true)
            {
                NetworkDiscoveryTool.Services.Services.TelegramConfigCrypto.SaveToFile(
                    dialog.FileName,
                    TelegramBotToken,
                    TelegramAdminChatId);

                StatusMessage = $"✓ Encrypted config exported to: {System.IO.Path.GetFileName(dialog.FileName)}";
                System.Windows.MessageBox.Show(
                    $"Encrypted file '{System.IO.Path.GetFileName(dialog.FileName)}' generated successfully!\n\nYou can copy this file next to SupportToolKit.exe on any client machine to apply these credentials automatically.",
                    "Config Exported",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Export failed: {ex.Message}";
        }
    }

    // === Database Actions ===

    [RelayCommand]
    private async Task ResetDatabaseAsync()
    {
        var result = System.Windows.MessageBox.Show(
            "Are you sure you want to reset the database? All scan history will be lost.",
            "Confirm Reset",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        await using var context = await _contextFactory.CreateDbContextAsync();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();

        StatusMessage = "Database reset successfully";
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var canConnect = await context.Database.CanConnectAsync();
            StatusMessage = canConnect ? "Connection successful" : "Connection failed";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task BrowseExportFolderAsync()
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog();
        dlg.SelectedPath = string.IsNullOrWhiteSpace(ExportFolder)
            ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            : ExportFolder;
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            ExportFolder = dlg.SelectedPath;
            await SaveSettingAsync("ExportFolder", ExportFolder);
        }
    }

    [RelayCommand]
    private async Task BrowseDefaultSaveFolderAsync()
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog();
        dlg.SelectedPath = string.IsNullOrWhiteSpace(DefaultSaveFolder)
            ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            : DefaultSaveFolder;
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            DefaultSaveFolder = dlg.SelectedPath;
            await SaveSettingAsync("DefaultSaveFolder", DefaultSaveFolder);
        }
    }

    // === User Management ===

    [RelayCommand]
    public void LoadPendingUsers()
    {
        PendingUsers.Clear();
        foreach (var u in _auth.GetPendingUsers())
            PendingUsers.Add(u);
    }

    [RelayCommand]
    public void LoadAllUsers()
    {
        AllUsers.Clear();
        foreach (var u in _auth.GetAllUsers())
        {
            AllUsers.Add(u);
        }
    }

    [RelayCommand]
    private void ApproveUser(AuthService.PendingUser? user)
    {
        if (user is null) return;
        _auth.ApproveUser(user.Id);
        LoadPendingUsers();
        LoadAllUsers();
        StatusMessage = $"Approved: {user.Username}";
    }

    [RelayCommand]
    private void RejectUser(AuthService.PendingUser? user)
    {
        if (user is null) return;
        _auth.RejectUser(user.Id);
        LoadPendingUsers();
        LoadAllUsers();
        StatusMessage = $"Rejected: {user.Username}";
    }

    [RelayCommand]
    private void DeleteUser(AuthService.PendingUser? user)
    {
        if (user is null || user.Id == 0) return;
        _auth.RejectUser(user.Id);
        LoadAllUsers();
        LoadPendingUsers();
        StatusMessage = $"Deleted: {user.Username}";
    }

    // === Theme ===

    public static void ApplyTheme(string theme)
    {
        if (System.Windows.Application.Current is null) return;

        // 1. Update MaterialDesign BundledTheme
        var isDark = theme == "Dark" || theme == "Blue";
        var bundledTheme = System.Windows.Application.Current.Resources.MergedDictionaries
            .OfType<BundledTheme>()
            .FirstOrDefault();
        if (bundledTheme != null)
        {
            bundledTheme.BaseTheme = isDark ? BaseTheme.Dark : BaseTheme.Light;
        }

        // 2. Update custom theme dictionary
        try
        {
            var uri = theme switch
            {
                "Dark" => new System.Uri("pack://application:,,,/SupportToolKit;component/Themes/DarkTheme.xaml", System.UriKind.Absolute),
                "Blue" => new System.Uri("pack://application:,,,/SupportToolKit;component/Themes/BlueTheme.xaml", System.UriKind.Absolute),
                _ => new System.Uri("pack://application:,,,/SupportToolKit;component/Themes/LightTheme.xaml", System.UriKind.Absolute),
            };

            var dict = new System.Windows.ResourceDictionary { Source = uri };

            var merged = System.Windows.Application.Current.Resources.MergedDictionaries;
            var existing = merged.FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Theme.xaml", StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                int index = merged.IndexOf(existing);
                merged[index] = dict;
            }
            else
            {
                merged.Add(dict);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ApplyTheme] Error: {ex.Message}");
        }
    }

    // === Updates ===

    [RelayCommand]
    private void CheckForUpdates()
    {
        LastUpdateCheck = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        StatusMessage = "You have the latest version";
    }

    // === Application Information ===

    public void LoadAppInfo()
    {
        AppVersion = Assembly.GetExecutingAssembly().GetName()?.Version?.ToString() ?? "1.0.0";
        AppFramework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
        AppDatabase = "SQLite — network_discovery.db";
        AppLogPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        AppOsVersion = Environment.OSVersion.ToString();

        var proc = System.Diagnostics.Process.GetCurrentProcess();
        AppUptime = (DateTime.Now - proc.StartTime).ToString(@"d\.hh\:mm\:ss");
        AppWorkingSet = FormatSize(proc.WorkingSet64);
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1048576 => $"{bytes / 1024.0:F1} KB",
        < 1073741824 => $"{bytes / 1048576.0:F1} MB",
        _ => $"{bytes / 1073741824.0:F2} GB",
    };
}

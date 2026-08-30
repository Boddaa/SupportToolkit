using System;
using System.IO;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetworkDiscoveryTool.Core.Interfaces;
using NetworkDiscoveryTool.Data;
using NetworkDiscoveryTool.Services;
using NetworkDiscoveryTool.Services.Services;
using NetworkDiscoveryTool.UI.Services;
using NetworkDiscoveryTool.UI.ViewModels;
using NetworkDiscoveryTool.UI.Views.Dashboard;
using NetworkDiscoveryTool.UI.Views.DeviceDetails;
using NetworkDiscoveryTool.UI.Views.IisMonitor;
using NetworkDiscoveryTool.UI.Views.LogCollector;
using NetworkDiscoveryTool.UI.Views.Login;
using NetworkDiscoveryTool.UI.Views.OperationHistory;
using NetworkDiscoveryTool.UI.Views.PingTool;
using NetworkDiscoveryTool.UI.Views.PortChecker;
using NetworkDiscoveryTool.UI.Views.Scan;
using NetworkDiscoveryTool.UI.Views.Screenshot;
using NetworkDiscoveryTool.UI.Views.Settings;
using NetworkDiscoveryTool.UI.Views.SqlTester;
using NetworkDiscoveryTool.UI.Views.SystemInfo;
using NetworkDiscoveryTool.UI.Views.WindowsServices;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace NetworkDiscoveryTool.UI;

public partial class App : System.Windows.Application
{
    public static ServiceProvider ServiceProvider { get; private set; } = null!;

    public static string DatabasePath { get; private set; } = string.Empty;

    public static LoggingLevelSwitch LogLevelSwitch { get; } = new(LogEventLevel.Information);

    public static void SetLogLevel(string level)
    {
        LogLevelSwitch.MinimumLevel = level switch
        {
            "Verbose" => LogEventLevel.Verbose,
            "Debug" => LogEventLevel.Debug,
            "Warning" => LogEventLevel.Warning,
            "Error" => LogEventLevel.Error,
            _ => LogEventLevel.Information
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            var isDropShadowCrash = args.Exception is InvalidOperationException ex
                && ex.Message.Contains("DropShadowEffect", StringComparison.OrdinalIgnoreCase);
            try { File.AppendAllText("crash.log", $"{DateTime.Now}: {(isDropShadowCrash ? "[Suppressed] " : "")}Unhandled exception: {args.Exception}\r\n"); } catch { }
            if (!isDropShadowCrash)
                System.Windows.MessageBox.Show($"Unhandled exception: {args.Exception.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        Directory.CreateDirectory("logs");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(LogLevelSwitch)
            .WriteTo.File("logs/network-discovery-.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        Log.Information("Application starting...");

        var services = new ServiceCollection();
        ConfigureServices(services);

        ServiceProvider = services.BuildServiceProvider();

        // Ensure database is created
        try
        {
            using var scope = ServiceProvider.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            using var ctx = factory.CreateDbContext();
            ctx.Database.EnsureCreated();

            // Migrate existing database: add Users table and UserId column if missing
            try { ctx.Database.ExecuteSqlRaw("SELECT COUNT(*) FROM Users"); }
            catch
            {
                ctx.Database.ExecuteSqlRaw("""
                    CREATE TABLE IF NOT EXISTS "Users" (
                        "Id" INTEGER NOT NULL CONSTRAINT "PK_Users" PRIMARY KEY AUTOINCREMENT,
                        "Username" TEXT NOT NULL,
                        "PasswordHash" TEXT NOT NULL,
                        "Role" TEXT NOT NULL
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_Username" ON "Users" ("Username");
                    """);
                try { ctx.Database.ExecuteSqlRaw("ALTER TABLE \"Scans\" ADD COLUMN \"UserId\" INTEGER NULL"); }
                catch { /* column already exists */ }
            }

            // Always try to add IsApproved, Email, HardwareId, CreatedAt columns
            try { ctx.Database.ExecuteSqlRaw("ALTER TABLE \"Users\" ADD COLUMN \"IsApproved\" INTEGER NOT NULL DEFAULT 0"); } catch { }
            try { ctx.Database.ExecuteSqlRaw("ALTER TABLE \"Users\" ADD COLUMN \"Email\" TEXT"); } catch { }
            try { ctx.Database.ExecuteSqlRaw("ALTER TABLE \"Users\" ADD COLUMN \"HardwareId\" TEXT"); } catch { }
            try { ctx.Database.ExecuteSqlRaw("ALTER TABLE \"Users\" ADD COLUMN \"CreatedAt\" TEXT"); } catch { }

            // Ensure existing rows do not have NULL values
            try { ctx.Database.ExecuteSqlRaw("UPDATE \"Users\" SET \"Email\" = '' WHERE \"Email\" IS NULL"); } catch { }
            try { ctx.Database.ExecuteSqlRaw("UPDATE \"Users\" SET \"HardwareId\" = '' WHERE \"HardwareId\" IS NULL"); } catch { }
            try { ctx.Database.ExecuteSqlRaw("UPDATE \"Users\" SET \"CreatedAt\" = CURRENT_TIMESTAMP WHERE \"CreatedAt\" IS NULL"); } catch { }

            // Ensure AppSettings table exists
            try { ctx.Database.ExecuteSqlRaw("SELECT COUNT(*) FROM AppSettings"); }
            catch
            {
                ctx.Database.ExecuteSqlRaw("""
                    CREATE TABLE IF NOT EXISTS "AppSettings" (
                        "Key" TEXT NOT NULL CONSTRAINT "PK_AppSettings" PRIMARY KEY,
                        "Value" TEXT NOT NULL
                    );
                    """);
            }

            // Ensure OperationLogs table exists
            try { ctx.Database.ExecuteSqlRaw("SELECT COUNT(*) FROM OperationLogs"); }
            catch
            {
                ctx.Database.ExecuteSqlRaw("""
                    CREATE TABLE IF NOT EXISTS "OperationLogs" (
                        "Id" INTEGER NOT NULL CONSTRAINT "PK_OperationLogs" PRIMARY KEY AUTOINCREMENT,
                        "OperationName" TEXT NOT NULL,
                        "Description" TEXT,
                        "Timestamp" TEXT NOT NULL,
                        "Result" TEXT,
                        "DurationMs" INTEGER NOT NULL DEFAULT 0,
                        "Username" TEXT
                    );
                    CREATE INDEX IF NOT EXISTS "IX_OperationLogs_Timestamp" ON "OperationLogs" ("Timestamp");
                    CREATE INDEX IF NOT EXISTS "IX_OperationLogs_OperationName" ON "OperationLogs" ("OperationName");
                    """);
            }

            // Add OS and Notes columns to Devices (if missing)
            try { ctx.Database.ExecuteSqlRaw("ALTER TABLE \"Devices\" ADD COLUMN \"OS\" TEXT"); }
            catch { }
            try { ctx.Database.ExecuteSqlRaw("ALTER TABLE \"Devices\" ADD COLUMN \"Notes\" TEXT"); }
            catch { }
            try { ctx.Database.ExecuteSqlRaw("ALTER TABLE \"Devices\" ADD COLUMN \"IsFavorite\" INTEGER NOT NULL DEFAULT 0"); }
            catch { }

            Log.Information("Database ready at: {Path}", DatabasePath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create database");
            System.Windows.MessageBox.Show($"Database error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        // Apply saved theme and settings
        try
        {
            using var themeScope = ServiceProvider.CreateScope();
            var settingsService = themeScope.ServiceProvider.GetRequiredService<Services.ISettingsService>();
            var savedTheme = settingsService.GetAsync("Theme").GetAwaiter().GetResult();
            ViewModels.SettingsViewModel.ApplyTheme(savedTheme ?? "Blue");

            var savedLogLevel = settingsService.GetAsync("LogLevel").GetAwaiter().GetResult();
            if (!string.IsNullOrEmpty(savedLogLevel))
                SetLogLevel(savedLogLevel);

            var telegramService = themeScope.ServiceProvider.GetRequiredService<NetworkDiscoveryTool.Services.Services.TelegramNotificationService>();
            var dbToken = settingsService.GetAsync("TelegramBotToken").GetAwaiter().GetResult();
            var dbChatId = settingsService.GetAsync("TelegramAdminChatId").GetAwaiter().GetResult();

            telegramService.BotToken = !string.IsNullOrWhiteSpace(dbToken) ? dbToken : NetworkDiscoveryTool.Services.Services.TelegramNotificationService.DefaultBotToken;
            telegramService.AdminChatId = !string.IsNullOrWhiteSpace(dbChatId) ? dbChatId : NetworkDiscoveryTool.Services.Services.TelegramNotificationService.DefaultAdminChatId;
        }
        catch
        {
            ViewModels.SettingsViewModel.ApplyTheme("Blue");
        }

        // Show login first
        var loginWindow = ServiceProvider.GetRequiredService<LoginWindow>();
        loginWindow.Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Database (next to the exe for portability)
        var dbFolder = Path.GetDirectoryName(Environment.ProcessPath)
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        DatabasePath = Path.Combine(dbFolder, "network_discovery.db");

        // Migrate old DB from AppData if it exists and no local DB yet
        var oldDb = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetworkDiscoveryTool", "network_discovery.db");
        if (File.Exists(oldDb) && !File.Exists(DatabasePath))
            File.Copy(oldDb, DatabasePath);
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseSqlite($"Data Source={DatabasePath}"));

        // Core services
        services.AddSingleton<INetworkScanner, NetworkScannerService>();

        // App services
        services.AddSingleton<LicenseService>();
        services.AddSingleton<TelegramNotificationService>();
        services.AddSingleton<AuthService>();
        services.AddSingleton<CurrentUserService>();
        services.AddSingleton<Services.INavigationService, Services.NavigationService>();
        services.AddSingleton<Services.ISystemInformationService, Services.SystemInformationService>();
        services.AddSingleton<Services.IWindowsServiceManager, Services.WindowsServiceManager>();
        services.AddSingleton<Services.ISqlConnectionService, Services.SqlConnectionService>();
        services.AddSingleton<Services.IIisService, Services.IisService>();
        services.AddSingleton<Services.ILogCollectionService, Services.LogCollectionService>();
        services.AddSingleton<Services.IScreenshotService, Services.ScreenshotService>();
        services.AddSingleton<Services.ISettingsService, Services.SettingsService>();
        services.AddSingleton<Services.IOperationHistoryService, Services.OperationHistoryService>();

        // ViewModels
        services.AddTransient<LoginViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<ScanViewModel>();
        services.AddSingleton<TopologyViewModel>();
        services.AddSingleton<PingToolViewModel>();
        services.AddSingleton<PortCheckerViewModel>();
        services.AddSingleton<SystemInfoViewModel>();
        services.AddSingleton<WindowsServicesViewModel>();
        services.AddTransient<SqlTesterViewModel>();
        services.AddSingleton<IisMonitorViewModel>();
        services.AddSingleton<LogCollectorViewModel>();
        services.AddSingleton<ScreenshotViewModel>();
        services.AddSingleton<OperationHistoryViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddTransient<DeviceDetailsViewModel>();

        // Windows / Pages
        services.AddTransient<LoginWindow>();
        services.AddTransient<DeviceDetailsPage>();
        services.AddTransient<DashboardPage>();
        services.AddTransient<ScanPage>();
        services.AddTransient<NetworkDiscoveryTool.UI.Views.Topology.TopologyPage>();
        services.AddTransient<PingToolPage>();
        services.AddTransient<PortCheckerPage>();
        services.AddTransient<SystemInfoPage>();
        services.AddTransient<WindowsServicesPage>();
        services.AddTransient<SqlTesterPage>();
        services.AddTransient<IisMonitorPage>();
        services.AddTransient<LogCollectorPage>();
        services.AddTransient<ScreenshotPage>();
        services.AddTransient<OperationHistoryPage>();
        services.AddTransient<SettingsPage>();
        services.AddTransient<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NetworkDiscoveryTool.Core.Models;
using NetworkDiscoveryTool.Data;
using NetworkDiscoveryTool.Services.Services;

namespace NetworkDiscoveryTool.UI.Services;

public sealed class AuthService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly LicenseService _licenseService;
    private readonly TelegramNotificationService _telegramService;
    private readonly CloudAuthService _cloudAuth;

    public AuthService(
        IDbContextFactory<AppDbContext> contextFactory,
        LicenseService licenseService,
        TelegramNotificationService telegramService,
        CloudAuthService? cloudAuth = null)
    {
        _contextFactory = contextFactory;
        _licenseService = licenseService;
        _telegramService = telegramService;
        _cloudAuth = cloudAuth ?? new CloudAuthService();
    }

    public sealed record AppUser(
        int Id,
        string Username,
        string Role,
        string Status = "Trial",
        DateTime? ExpiresAt = null,
        double? RemainingHours = null);

    public void EnsureDefaultAdminExists()
    {
        try
        {
            using var context = _contextFactory.CreateDbContext();
            bool hasAdmin = context.Users.Any(u => u.Role == "Administrator" || u.Role == "Admin");
            if (!hasAdmin)
            {
                var defaultAdmin = new User
                {
                    Username = "admin",
                    PasswordHash = PasswordHasher.HashPassword("159357"),
                    Email = "admin@local",
                    HardwareId = HardwareIdService.GetHardwareId(),
                    Role = "Administrator",
                    IsApproved = true,
                    CreatedAt = DateTime.UtcNow
                };

                context.Users.Add(defaultAdmin);
                context.SaveChanges();
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to initialize default admin account.");
        }
    }

    public async Task<(bool Success, AppUser? User, string? Error)> ValidateUserAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return (false, null, "Username and password are required");

        var cleanUsername = username.Trim();

        // 1. Try Cloud Authentication (Central Database & Subscription Engine)
        try
        {
            var cloudRes = await _cloudAuth.LoginAsync(cleanUsername, password);
            if (cloudRes.Success)
            {
                var appUser = new AppUser(
                    cloudRes.UserId ?? 1,
                    cleanUsername,
                    cloudRes.Role ?? "User",
                    cloudRes.Status ?? "Trial",
                    cloudRes.ExpiresAt,
                    cloudRes.RemainingHours);

                _licenseService.SaveApprovedLicense(cleanUsername);
                return (true, appUser, null);
            }
            else if (!string.IsNullOrWhiteSpace(cloudRes.Message) &&
                     !cloudRes.Message.Contains("unreachable", StringComparison.OrdinalIgnoreCase) &&
                     !cloudRes.Message.Contains("connection error", StringComparison.OrdinalIgnoreCase))
            {
                // Explicit rejection from Cloud (wrong password, blocked, expired, etc.)
                return (false, null, cloudRes.Message);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Cloud authentication skipped or failed. Falling back to local offline verification.");
        }

        // 2. Offline Fallback: ONLY permitted if cryptographically verified offline license exists for this user
        if (_licenseService.IsLicenseValidOffline(cleanUsername, password, out var offlineReason))
        {
            return (true, new AppUser(1, cleanUsername, "User", "Subscribed"), null);
        }

        if (offlineReason != null && (offlineReason.Contains("mismatch", StringComparison.OrdinalIgnoreCase) ||
                                     offlineReason.Contains("No offline license", StringComparison.OrdinalIgnoreCase) ||
                                     offlineReason.Contains("No local license", StringComparison.OrdinalIgnoreCase)))
        {
            return (false, null, "Could not connect to the cloud licensing server and no offline license is cached for this username. Please check your internet connection.");
        }

        return (false, null, offlineReason ?? "Cloud server is unreachable. Please verify your internet connection.");
    }

    public bool ValidateUser(string username, string password, out AppUser? user, out string? error)
    {
        var res = ValidateUserAsync(username, password).GetAwaiter().GetResult();
        user = res.User;
        error = res.Error;
        return res.Success;
    }

    public bool ValidateRememberedUser(string username, out AppUser? user, out string? error)
    {
        user = null;
        error = null;

        if (string.IsNullOrWhiteSpace(username))
        {
            error = "No remembered user specified.";
            return false;
        }

        var clean = username.Trim();

        try
        {
            using var context = _contextFactory.CreateDbContext();

            // 1. Check local SQLite Users table (e.g. built-in Administrator)
            var dbUser = context.Users.FirstOrDefault(u => u.Username == clean);
            if (dbUser is not null)
            {
                if (!dbUser.IsApproved)
                {
                    error = "Account approval has been revoked or is pending.";
                    return false;
                }

                user = new AppUser(dbUser.Id, dbUser.Username, dbUser.Role, "Subscribed");
                return true;
            }

            // 2. Check local SQLite AppSettings for remembered workstation session
            var rememberedUser = context.AppSettings.FirstOrDefault(s => s.Key == "Remembered_Username")?.Value;
            var rememberedActive = context.AppSettings.FirstOrDefault(s => s.Key == "Remembered_IsActive")?.Value;
            if (string.Equals(rememberedUser, clean, StringComparison.OrdinalIgnoreCase) && rememberedActive == "true")
            {
                var role = context.AppSettings.FirstOrDefault(s => s.Key == "Remembered_Role")?.Value ?? "User";
                var idStr = context.AppSettings.FirstOrDefault(s => s.Key == "Remembered_UserId")?.Value;
                int.TryParse(idStr, out var uid);
                if (uid <= 0) uid = 1;

                var status = context.AppSettings.FirstOrDefault(s => s.Key == "Remembered_Status")?.Value ?? "Subscribed";

                user = new AppUser(uid, clean, role, status);
                return true;
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Error reading database for remembered user.");
        }

        // 3. Fallback: Offline license token verification
        if (_licenseService.IsLicenseValidOffline(clean, out var offlineReason))
        {
            user = new AppUser(1, clean, "User", "Subscribed");
            return true;
        }

        error = "Remembered workstation profile not found.";
        return false;
    }

    public async Task<(bool Success, string Message, string? Status)> RegisterAsync(string username, string password, string email = "")
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return (false, "Username and password are required", null);

        if (username.Length < 3)
            return (false, "Username must be at least 3 characters", null);

        if (password.Length < 4)
            return (false, "Password must be at least 4 characters", null);

        var cleanUser = username.Trim();
        var cleanPass = password;
        var cleanEmail = email.Trim();

        // Central Cloud Registration (Anti-Cheat & Motherboard HWID enforcement)
        try
        {
            var cloudRes = await _cloudAuth.RegisterAsync(cleanUser, cleanPass, cleanEmail);
            return (cloudRes.Success, cloudRes.Message, cloudRes.Status);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Cloud registration unreachable.");
            return (false, "Could not connect to central licensing server. Please check your internet connection.", null);
        }
    }

    public (bool Success, string Message) Register(string username, string password, string email = "")
    {
        var res = RegisterAsync(username, password, email).GetAwaiter().GetResult();
        return (res.Success, res.Message);
    }

    public sealed record PendingUser(int Id, string Username, string Role, string HardwareId, string Email, DateTime CreatedAt);

    public List<PendingUser> GetPendingUsers()
    {
        try
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Users
                .Where(u => !u.IsApproved)
                .Select(u => new PendingUser(
                    u.Id,
                    u.Username ?? string.Empty,
                    u.Role ?? "User",
                    u.HardwareId ?? string.Empty,
                    u.Email ?? string.Empty,
                    u.CreatedAt ?? DateTime.MinValue))
                .ToList();
        }
        catch
        {
            return new List<PendingUser>();
        }
    }

    public List<PendingUser> GetAllUsers()
    {
        try
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Users
                .Select(u => new PendingUser(
                    u.Id,
                    u.Username ?? string.Empty,
                    u.Role ?? "User",
                    u.HardwareId ?? string.Empty,
                    u.Email ?? string.Empty,
                    u.CreatedAt ?? DateTime.MinValue))
                .ToList();
        }
        catch
        {
            return new List<PendingUser>();
        }
    }

    public void ApproveUser(int userId)
    {
        using var context = _contextFactory.CreateDbContext();
        var user = context.Users.Find(userId);
        if (user is not null)
        {
            user.IsApproved = true;
            context.SaveChanges();
            _licenseService.SaveApprovedLicense(user.Username, user.PasswordHash);
        }
    }

    public void RejectUser(int userId)
    {
        using var context = _contextFactory.CreateDbContext();
        var user = context.Users.Find(userId);
        if (user is not null)
        {
            context.Users.Remove(user);
            context.SaveChanges();
        }
    }

    public async Task<RemoteApprovalStatus> CheckRemoteTelegramApprovalAsync(string username)
    {
        var hwid = HardwareIdService.GetHardwareId();

        // Check central cloud server (receives real-time Telegram bot callbacks via webhook/polling)
        try
        {
            var cloudStatus = await _cloudAuth.CheckApprovalStatusAsync(username, hwid);
            return cloudStatus;
        }
        catch
        {
            return RemoteApprovalStatus.Pending;
        }
    }
}

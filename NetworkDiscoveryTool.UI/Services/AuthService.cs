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
        string Status = "Subscribed",
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
                    1,
                    cleanUsername,
                    cloudRes.Role ?? "User",
                    cloudRes.Status ?? "Subscribed",
                    cloudRes.ExpiresAt,
                    cloudRes.RemainingHours);

                _licenseService.SaveApprovedLicense(cleanUsername);
                return (true, appUser, null);
            }
            else if (cloudRes.Status == "Expired" ||
                     cloudRes.Message.Contains("expired", StringComparison.OrdinalIgnoreCase) ||
                     cloudRes.Message.Contains("blocked", StringComparison.OrdinalIgnoreCase))
            {
                // Explicit rejection from Cloud (License expired or Device blocked)
                return (false, null, cloudRes.Message);
            }
            else if (!string.IsNullOrWhiteSpace(cloudRes.Message) &&
                     !cloudRes.Message.Contains("unreachable", StringComparison.OrdinalIgnoreCase) &&
                     !cloudRes.Message.Contains("connection error", StringComparison.OrdinalIgnoreCase))
            {
                // Server responded with explicit failure (e.g. wrong password)
                return (false, null, cloudRes.Message);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Cloud authentication skipped or failed. Falling back to local verification.");
        }

        // 2. Local Fallback Database
        try
        {
            using var context = _contextFactory.CreateDbContext();
            var dbUser = context.Users.FirstOrDefault(u => u.Username == cleanUsername);
            if (dbUser is not null)
            {
                if (!PasswordHasher.VerifyPassword(password, dbUser.PasswordHash, out bool needsRehash))
                    return (false, null, "Invalid username or password");

                if (needsRehash)
                {
                    try
                    {
                        dbUser.PasswordHash = PasswordHasher.HashPassword(password);
                        context.SaveChanges();
                    }
                    catch { }
                }

                if (!dbUser.IsApproved)
                    return (false, null, "Account pending admin approval.");

                _licenseService.SaveApprovedLicense(dbUser.Username, dbUser.PasswordHash);
                return (true, new AppUser(dbUser.Id, dbUser.Username, dbUser.Role), null);
            }
        }
        catch
        {
            // 3. Fallback to Secure Encrypted Offline License Token
            if (_licenseService.IsLicenseValidOffline(cleanUsername, password, out var offlineReason))
            {
                return (true, new AppUser(1, cleanUsername, "User"), null);
            }

            return (false, null, offlineReason ?? "Offline credentials validation failed.");
        }

        return (false, null, "Invalid username or password");
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
            var dbUser = context.Users.FirstOrDefault(u => u.Username == clean);
            if (dbUser is not null)
            {
                if (!dbUser.IsApproved)
                {
                    error = "Account approval has been revoked or is pending.";
                    return false;
                }

                user = new AppUser(dbUser.Id, dbUser.Username, dbUser.Role);
                return true;
            }
        }
        catch
        {
            if (_licenseService.IsLicenseValidOffline(clean, out var offlineReason))
            {
                user = new AppUser(1, clean, "User");
                return true;
            }

            error = offlineReason ?? "Offline credentials validation failed.";
            return false;
        }

        error = "User not found in system.";
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

        // 1. Try Cloud Registration with Anti-Cheat HWID check
        try
        {
            var cloudRes = await _cloudAuth.RegisterAsync(cleanUser, cleanPass, cleanEmail);
            if (cloudRes.Success)
            {
                return (true, cloudRes.Message, cloudRes.Status);
            }
            else if (cloudRes.Status == "Expired" ||
                     cloudRes.Message.Contains("already used its 1-day free trial", StringComparison.OrdinalIgnoreCase) ||
                     cloudRes.Message.Contains("already registered", StringComparison.OrdinalIgnoreCase) ||
                     cloudRes.Message.Contains("blocked", StringComparison.OrdinalIgnoreCase))
            {
                return (false, cloudRes.Message, cloudRes.Status);
            }
            else if (!string.IsNullOrWhiteSpace(cloudRes.Message) &&
                     !cloudRes.Message.Contains("unreachable", StringComparison.OrdinalIgnoreCase) &&
                     !cloudRes.Message.Contains("connection error", StringComparison.OrdinalIgnoreCase))
            {
                return (false, cloudRes.Message, cloudRes.Status);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Cloud registration unreachable. Falling back to local registration.");
        }

        // 2. Local Database Registration Fallback
        var (localSuccess, localMsg) = RegisterLocal(cleanUser, cleanPass, cleanEmail);
        return (localSuccess, localMsg, "Pending");
    }

    public (bool Success, string Message) Register(string username, string password, string email = "")
    {
        var res = RegisterAsync(username, password, email).GetAwaiter().GetResult();
        return (res.Success, res.Message);
    }

    private (bool Success, string Message) RegisterLocal(string username, string password, string email)
    {
        var hardwareId = HardwareIdService.GetHardwareId();

        try
        {
            using var context = _contextFactory.CreateDbContext();

            if (context.Users.Any(u => u.Username == username))
                return (false, "Username already exists");

            var user = new User
            {
                Username = username,
                PasswordHash = PasswordHasher.HashPassword(password),
                Email = email,
                HardwareId = hardwareId,
                Role = "User",
                IsApproved = false,
                CreatedAt = DateTime.UtcNow
            };

            context.Users.Add(user);
            context.SaveChanges();

            if (_telegramService.IsConfigured)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _telegramService.SendRegistrationAlertAsync(
                            username,
                            string.IsNullOrWhiteSpace(email) ? "N/A" : email,
                            hardwareId,
                            Environment.MachineName);
                    }
                    catch (Exception ex)
                    {
                        Serilog.Log.Warning(ex, "Failed to send Telegram registration alert.");
                    }
                });
            }

            return (true, "Registration request submitted. Pending Administrator approval.");
        }
        catch (Exception ex)
        {
            return (false, $"Registration error: {ex.Message}");
        }
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
        if (!_telegramService.IsConfigured)
            return RemoteApprovalStatus.Pending;

        var hwid = HardwareIdService.GetHardwareId();
        var status = await _telegramService.CheckApprovalStatusAsync(username, hwid);

        if (status == RemoteApprovalStatus.Approved)
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();
                var user = context.Users.FirstOrDefault(u => u.Username == username);
                if (user != null)
                {
                    user.IsApproved = true;
                    context.SaveChanges();
                    _licenseService.SaveApprovedLicense(user.Username, user.PasswordHash);
                }
            }
            catch { }
        }

        return status;
    }
}

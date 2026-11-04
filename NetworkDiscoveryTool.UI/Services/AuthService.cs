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

    public AuthService(
        IDbContextFactory<AppDbContext> contextFactory,
        LicenseService licenseService,
        TelegramNotificationService telegramService)
    {
        _contextFactory = contextFactory;
        _licenseService = licenseService;
        _telegramService = telegramService;
    }

    public sealed record AppUser(int Id, string Username, string Role);

    public void EnsureDefaultAdminExists()
    {
        try
        {
            using var context = _contextFactory.CreateDbContext();
            bool hasAdmin = context.Users.Any(u => u.Role == "Administrator");
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

    public bool ValidateUser(string username, string password, out AppUser? user, out string? error)
    {
        user = null;
        error = null;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            error = "Username and password are required";
            return false;
        }

        try
        {
            using var context = _contextFactory.CreateDbContext();
            var dbUser = context.Users.FirstOrDefault(u => u.Username == username.Trim());
            if (dbUser is not null)
            {
                if (!PasswordHasher.VerifyPassword(password, dbUser.PasswordHash, out bool needsRehash))
                {
                    error = "Invalid username or password";
                    return false;
                }

                // Automatic security upgrade: migrate legacy unsalted SHA-256 to salted PBKDF2
                if (needsRehash)
                {
                    try
                    {
                        dbUser.PasswordHash = PasswordHasher.HashPassword(password);
                        context.SaveChanges();
                    }
                    catch { /* Continue login even if hash upgrade fails */ }
                }

                if (!dbUser.IsApproved)
                {
                    error = "Account pending admin approval.";
                    return false;
                }

                // Cache approved license locally for offline resilience with password hash
                _licenseService.SaveApprovedLicense(dbUser.Username, dbUser.PasswordHash);

                user = new AppUser(dbUser.Id, dbUser.Username, dbUser.Role);
                return true;
            }
        }
        catch
        {
            // DB unreachable -> Fallback to Secure Encrypted Offline License Token
            if (_licenseService.IsLicenseValidOffline(username.Trim(), password, out var offlineReason))
            {
                user = new AppUser(1, username.Trim(), "User");
                return true;
            }

            error = offlineReason ?? "Offline credentials validation failed.";
            return false;
        }

        error = "Invalid username or password";
        return false;
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

        try
        {
            using var context = _contextFactory.CreateDbContext();
            var dbUser = context.Users.FirstOrDefault(u => u.Username == username.Trim());
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
            // DB unreachable -> check offline license
            if (_licenseService.IsLicenseValidOffline(username.Trim(), out var offlineReason))
            {
                user = new AppUser(1, username.Trim(), "User");
                return true;
            }

            error = offlineReason ?? "Offline credentials validation failed.";
            return false;
        }

        error = "User not found in system.";
        return false;
    }

    public (bool Success, string Message) Register(string username, string password, string email = "")
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return (false, "Username and password are required");

        if (username.Length < 3)
            return (false, "Username must be at least 3 characters");

        if (password.Length < 4)
            return (false, "Password must be at least 4 characters");

        var hardwareId = HardwareIdService.GetHardwareId();

        try
        {
            using var context = _contextFactory.CreateDbContext();

            if (context.Users.Any(u => u.Username == username))
                return (false, "Username already exists");

            var user = new User
            {
                Username = username.Trim(),
                PasswordHash = PasswordHasher.HashPassword(password),
                Email = email.Trim(),
                HardwareId = hardwareId,
                Role = "User",
                IsApproved = false,
                CreatedAt = DateTime.UtcNow
            };

            context.Users.Add(user);
            context.SaveChanges();

            // Send async alert to Admin via Telegram ONLY if explicitly configured by admin
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

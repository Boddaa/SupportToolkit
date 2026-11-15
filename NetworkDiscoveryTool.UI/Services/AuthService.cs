using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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

                _licenseService.SaveApprovedLicense(cleanUsername, PasswordHasher.HashPassword(password), cloudRes.ExpiresAt);

                // Now that we have verified internet connectivity, trigger background sync for any unsynced offline accounts!
                _ = Task.Run(() => SyncPendingUsersAsync());

                return (true, appUser, null);
            }
            else if (!string.IsNullOrWhiteSpace(cloudRes.Message) && !IsOfflineError(cloudRes.Message))
            {
                // Explicit rejection from Cloud (wrong password, blocked, expired, etc.)
                return (false, null, cloudRes.Message);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Cloud authentication skipped or failed. Falling back to local offline verification.");
        }

        // 2. Offline Fallback 1: Local SQLite Users Table (includes local admin: admin / 159357, and offline-registered accounts)
        try
        {
            using var context = _contextFactory.CreateDbContext();
            var dbUser = context.Users.FirstOrDefault(u => u.Username.ToLower() == cleanUsername.ToLower());
            if (dbUser != null && dbUser.IsApproved)
            {
                if (PasswordHasher.VerifyPassword(password, dbUser.PasswordHash, out _))
                {
                    // Check subscription expiry (except for built-in Administrator)
                    if (cleanUsername.ToLower() != "admin" && dbUser.SubscriptionExpiresAt.HasValue && dbUser.SubscriptionExpiresAt.Value < DateTime.UtcNow)
                    {
                        return (false, null, "Your 30-day offline subscription has expired. Please connect to the internet to renew.");
                    }

                    double remainingHours = dbUser.SubscriptionExpiresAt.HasValue
                        ? Math.Max(0, (dbUser.SubscriptionExpiresAt.Value - DateTime.UtcNow).TotalHours)
                        : 720.0;

                    var appUser = new AppUser(
                        dbUser.Id,
                        dbUser.Username,
                        dbUser.Role ?? "Administrator",
                        "Trial",
                        dbUser.SubscriptionExpiresAt,
                        Math.Round(remainingHours, 1));

                    return (true, appUser, null);
                }
                else
                {
                    return (false, null, "Invalid password for this account.");
                }
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Local database user verification failed.");
        }

        // 3. Offline Fallback 2: Cached License Token in license.lic
        if (_licenseService.IsLicenseValidOffline(cleanUsername, password, out var offlineReason))
        {
            return (true, new AppUser(1, cleanUsername, "User", "Subscribed"), null);
        }

        if (offlineReason != null && (offlineReason.Contains("mismatch", StringComparison.OrdinalIgnoreCase) ||
                                     offlineReason.Contains("No offline license", StringComparison.OrdinalIgnoreCase) ||
                                     offlineReason.Contains("No local license", StringComparison.OrdinalIgnoreCase)))
        {
            return (false, null, "Could not connect to cloud server and no local offline account matches these credentials.");
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
        var hwid = HardwareIdService.GetHardwareId();

        // 1. Try Central Cloud Registration first
        try
        {
            var cloudRes = await _cloudAuth.RegisterAsync(cleanUser, cleanPass, cleanEmail);
            if (cloudRes.Success)
            {
                var expiry = cloudRes.ExpiresAt ?? DateTime.UtcNow.AddDays(30);
                SaveUserLocally(cleanUser, cleanPass, cleanEmail, hwid, isSynced: true, expiresAt: expiry);
                _licenseService.SaveApprovedLicense(cleanUser, PasswordHasher.HashPassword(cleanPass), expiry);
                return (true, cloudRes.Message, cloudRes.Status ?? "Trial");
            }
            else if (!IsOfflineError(cloudRes.Message))
            {
                // Explicit rejection from server (e.g. username taken, or device already bound)
                return (false, cloudRes.Message, cloudRes.Status);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Cloud registration unreachable. Falling back to offline local account creation.");
        }

        // 2. OFFLINE FALLBACK: No internet connection!
        // Create user locally with 30-Day Subscription (1 month) and mark for sync when internet connects.
        try
        {
            using var context = _contextFactory.CreateDbContext();
            var existing = context.Users.FirstOrDefault(u => u.Username.ToLower() == cleanUser.ToLower());
            if (existing != null)
            {
                return (false, $"Username '{cleanUser}' already exists locally on this machine.", null);
            }

            var subExpiry = DateTime.UtcNow.AddDays(30);
            var passHash = PasswordHasher.HashPassword(cleanPass);
            var encryptedSyncPass = EncryptForSync(cleanPass);

            var localUser = new User
            {
                Username = cleanUser,
                PasswordHash = passHash,
                Email = cleanEmail,
                HardwareId = hwid,
                Role = "Administrator",
                IsApproved = true,
                CreatedAt = DateTime.UtcNow,
                SubscriptionExpiresAt = subExpiry,
                IsSyncedWithCloud = false,
                SyncPayload = encryptedSyncPass
            };

            context.Users.Add(localUser);
            context.SaveChanges();

            // Save active offline license token for 30 days
            _licenseService.SaveApprovedLicense(cleanUser, passHash, subExpiry);

            Serilog.Log.Information("Offline account '{Username}' created successfully with 30-day subscription.", cleanUser);
            return (true, "Account created in Offline Mode with a 30-Day Subscription! It will automatically sync to your cloud database as soon as internet is connected.", "Trial");
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to create local offline account.");
            return (false, $"Local registration error: {ex.Message}", null);
        }
    }

    private void SaveUserLocally(string username, string password, string email, string hwid, bool isSynced, DateTime expiresAt)
    {
        try
        {
            using var context = _contextFactory.CreateDbContext();
            var user = context.Users.FirstOrDefault(u => u.Username.ToLower() == username.ToLower());
            if (user == null)
            {
                user = new User
                {
                    Username = username,
                    PasswordHash = PasswordHasher.HashPassword(password),
                    Email = email,
                    HardwareId = hwid,
                    Role = "Administrator",
                    IsApproved = true,
                    CreatedAt = DateTime.UtcNow,
                    SubscriptionExpiresAt = expiresAt,
                    IsSyncedWithCloud = isSynced
                };
                context.Users.Add(user);
            }
            else
            {
                user.PasswordHash = PasswordHasher.HashPassword(password);
                user.SubscriptionExpiresAt = expiresAt;
                user.IsSyncedWithCloud = isSynced;
                user.IsApproved = true;
            }
            context.SaveChanges();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Could not save/update user locally.");
        }
    }

    public async Task SyncPendingUsersAsync()
    {
        try
        {
            using var context = _contextFactory.CreateDbContext();
            var pending = context.Users
                .Where(u => !u.IsSyncedWithCloud && !string.IsNullOrEmpty(u.SyncPayload) && u.Username.ToLower() != "admin")
                .ToList();

            if (pending.Count == 0) return;

            Serilog.Log.Information("Found {Count} pending offline user(s) to sync to cloud.", pending.Count);

            foreach (var user in pending)
            {
                var plainPass = DecryptForSync(user.SyncPayload);
                if (string.IsNullOrWhiteSpace(plainPass)) continue;

                var res = await _cloudAuth.SyncOfflineUserAsync(
                    user.Username,
                    plainPass,
                    user.Email,
                    user.HardwareId ?? HardwareIdService.GetHardwareId(),
                    Environment.MachineName);

                if (res.Success || (res.Message != null && res.Message.Contains("already", StringComparison.OrdinalIgnoreCase)))
                {
                    user.IsSyncedWithCloud = true;
                    user.SyncPayload = null;
                    if (res.ExpiresAt.HasValue) user.SubscriptionExpiresAt = res.ExpiresAt;
                    Serilog.Log.Information("User '{Username}' successfully synced to cloud server.", user.Username);
                }
            }

            await context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Error syncing pending offline users to cloud.");
        }
    }

    private static string EncryptForSync(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;
        var bytes = Encoding.UTF8.GetBytes(plainText);
        var enc = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(enc);
    }

    private static string DecryptForSync(string? cipherText)
    {
        if (string.IsNullOrWhiteSpace(cipherText)) return string.Empty;
        try
        {
            var bytes = Convert.FromBase64String(cipherText);
            var dec = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(dec);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsOfflineError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return true;
        return message.Contains("unreachable", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("connection error", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("No such host", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("refused", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("reset", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Could not connect", StringComparison.OrdinalIgnoreCase);
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

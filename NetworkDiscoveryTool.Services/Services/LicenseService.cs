using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NetworkDiscoveryTool.Services.Services;

public sealed class LicenseService
{
    private readonly string _licenseFilePath;

    public LicenseService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "SupportToolKit");
        Directory.CreateDirectory(dir);
        _licenseFilePath = Path.Combine(dir, "license.lic");
    }

    public sealed record LicenseToken(
        string Username,
        string HardwareId,
        DateTime ApprovedDate,
        bool IsActive,
        string? PasswordHash = null,
        DateTime? ExpiresAt = null);

    private sealed class LicenseStore
    {
        public Dictionary<string, LicenseToken> Users { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private Dictionary<string, LicenseToken> LoadTokens()
    {
        var result = new Dictionary<string, LicenseToken>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(_licenseFilePath))
            return result;

        try
        {
            var encrypted = File.ReadAllText(_licenseFilePath);
            var json = DecryptData(encrypted);
            if (string.IsNullOrWhiteSpace(json)) return result;

            // Try multi-user store format
            try
            {
                var store = JsonSerializer.Deserialize<LicenseStore>(json);
                if (store?.Users != null && store.Users.Count > 0)
                {
                    return new Dictionary<string, LicenseToken>(store.Users, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch { }

            // Fallback: Legacy single LicenseToken
            var single = JsonSerializer.Deserialize<LicenseToken>(json);
            if (single != null && !string.IsNullOrWhiteSpace(single.Username))
            {
                result[single.Username] = single;
            }
        }
        catch { }

        return result;
    }

    public void SaveApprovedLicense(string username, string? passwordHash = null, DateTime? expiresAt = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(username)) return;

            var clean = username.Trim();
            var hardwareId = HardwareIdService.GetHardwareId();
            var tokens = LoadTokens();

            var expiry = expiresAt ?? DateTime.UtcNow.AddDays(30);
            tokens[clean] = new LicenseToken(clean, hardwareId, DateTime.UtcNow, true, passwordHash, expiry);

            var store = new LicenseStore { Users = tokens };
            var json = JsonSerializer.Serialize(store);
            var encrypted = EncryptData(json);
            File.WriteAllText(_licenseFilePath, encrypted);
        }
        catch { /* Ignore write errors */ }
    }

    public bool IsLicenseValidOffline(string username, string password, out string? reason)
    {
        reason = null;
        if (!File.Exists(_licenseFilePath))
        {
            reason = "No local license found.";
            return false;
        }

        try
        {
            var clean = (username ?? string.Empty).Trim();
            var tokens = LoadTokens();

            if (tokens.Count == 0)
            {
                reason = "License data is unreadable or corrupted.";
                return false;
            }

            if (!tokens.TryGetValue(clean, out var token))
            {
                reason = $"No offline license cached for '{clean}'. Please connect to the internet to log in.";
                return false;
            }

            if (!token.IsActive)
            {
                reason = "License is invalid or revoked.";
                return false;
            }

            if (token.ExpiresAt.HasValue && token.ExpiresAt.Value < DateTime.UtcNow)
            {
                reason = "Your 30-day offline license has expired. Please connect to the internet to renew.";
                return false;
            }

            var currentHwid = HardwareIdService.GetHardwareId();
            if (!string.Equals(token.HardwareId, currentHwid, StringComparison.OrdinalIgnoreCase))
            {
                reason = "License does not match this machine's Hardware ID.";
                return false;
            }

            // Verify password against cached license token hash if present and password provided
            if (!string.IsNullOrEmpty(token.PasswordHash) && !string.IsNullOrEmpty(password))
            {
                if (!PasswordHasher.VerifyPassword(password, token.PasswordHash, out _))
                {
                    reason = "Invalid password for offline credentials.";
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            reason = $"License validation error: {ex.Message}";
            return false;
        }
    }

    public bool IsLicenseValidOffline(string username, out string? reason)
    {
        return IsLicenseValidOffline(username, string.Empty, out reason);
    }

    private static string EncryptData(string plainText)
    {
        var bytes = Encoding.UTF8.GetBytes(plainText);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    private static string DecryptData(string cipherText)
    {
        if (string.IsNullOrWhiteSpace(cipherText)) return string.Empty;

        try
        {
            var buffer = Convert.FromBase64String(cipherText);
            var decrypted = ProtectedData.Unprotect(buffer, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            return string.Empty;
        }
    }
}

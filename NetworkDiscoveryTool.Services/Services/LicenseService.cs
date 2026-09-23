using System;
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
        string? PasswordHash = null);

    public void SaveApprovedLicense(string username, string? passwordHash = null)
    {
        try
        {
            var hardwareId = HardwareIdService.GetHardwareId();
            var token = new LicenseToken(username, hardwareId, DateTime.UtcNow, true, passwordHash);
            var json = JsonSerializer.Serialize(token);
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
            var encrypted = File.ReadAllText(_licenseFilePath);
            var json = DecryptData(encrypted);
            if (string.IsNullOrWhiteSpace(json))
            {
                reason = "License data is unreadable or corrupted.";
                return false;
            }

            var token = JsonSerializer.Deserialize<LicenseToken>(json);
            if (token is null || !token.IsActive)
            {
                reason = "License is invalid or revoked.";
                return false;
            }

            var currentHwid = HardwareIdService.GetHardwareId();
            if (!string.Equals(token.HardwareId, currentHwid, StringComparison.OrdinalIgnoreCase))
            {
                reason = "License does not match this machine's Hardware ID.";
                return false;
            }

            if (!string.Equals(token.Username, username, StringComparison.OrdinalIgnoreCase))
            {
                reason = "License username mismatch.";
                return false;
            }

            // Verify password against cached license token hash
            if (!string.IsNullOrEmpty(token.PasswordHash))
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

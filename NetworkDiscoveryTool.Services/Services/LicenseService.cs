using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NetworkDiscoveryTool.Services.Services;

public sealed class LicenseService
{
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("SupportToolKitSecKey2026!#$89012"); // 32 bytes AES Key
    private static readonly byte[] IV = Encoding.UTF8.GetBytes("STKLicenseIV2026!"); // 16 bytes IV

    private readonly string _licenseFilePath;

    public LicenseService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "SupportToolKit");
        Directory.CreateDirectory(dir);
        _licenseFilePath = Path.Combine(dir, "license.lic");
    }

    public sealed record LicenseToken(string Username, string HardwareId, DateTime ApprovedDate, bool IsActive);

    public void SaveApprovedLicense(string username)
    {
        try
        {
            var hardwareId = HardwareIdService.GetHardwareId();
            var token = new LicenseToken(username, hardwareId, DateTime.UtcNow, true);
            var json = JsonSerializer.Serialize(token);
            var encrypted = EncryptString(json);
            File.WriteAllText(_licenseFilePath, encrypted);
        }
        catch { /* Ignore write errors */ }
    }

    public bool IsLicenseValidOffline(string username, out string? reason)
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
            var json = DecryptString(encrypted);
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

            return true;
        }
        catch (Exception ex)
        {
            reason = $"License validation error: {ex.Message}";
            return false;
        }
    }

    private static string EncryptString(string plainText)
    {
        using var aes = Aes.Create();
        aes.Key = Key;
        aes.IV = IV;
        var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);

        using var ms = new MemoryStream();
        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        using (var writer = new StreamWriter(cs))
        {
            writer.Write(plainText);
        }

        return Convert.ToBase64String(ms.ToArray());
    }

    private static string DecryptString(string cipherText)
    {
        var buffer = Convert.FromBase64String(cipherText);
        using var aes = Aes.Create();
        aes.Key = Key;
        aes.IV = IV;
        var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);

        using var ms = new MemoryStream(buffer);
        using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        using var reader = new StreamReader(cs);
        return reader.ReadToEnd();
    }
}

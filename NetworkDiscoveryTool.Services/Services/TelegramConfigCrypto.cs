using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NetworkDiscoveryTool.Services.Services;

/// <summary>
/// Provides secure AES-256 encryption and decryption for distributed Telegram Bot configurations.
/// Enables distributing an encrypted 'telegram.enc' file next to the application executable,
/// allowing administrators to rotate or change bot tokens simply by replacing this file.
/// </summary>
public static class TelegramConfigCrypto
{
    public const string DefaultFileName = "telegram.enc";

    // Dedicated cryptographic salt and master secret for SupportToolKit configuration payloads
    private static readonly byte[] MasterSalt = new byte[]
    {
        0x53, 0x75, 0x70, 0x70, 0x6F, 0x72, 0x74, 0x54,
        0x6F, 0x6F, 0x6C, 0x4B, 0x69, 0x74, 0x32, 0x36
    };

    private const string MasterSecret = "STK_TELEGRAM_SECURE_ENCRYPTION_KEY_2026#ENTERPRISE";

    public sealed class ConfigPayload
    {
        public string BotToken { get; set; } = string.Empty;
        public string AdminChatId { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public string ToolVersion { get; set; } = "1.0";
    }

    /// <summary>
    /// Encrypts the provided Telegram Bot Token and Admin Chat ID into an AES-256 encrypted base64 payload.
    /// </summary>
    public static string Encrypt(string botToken, string adminChatId)
    {
        var payload = new ConfigPayload
        {
            BotToken = (botToken ?? string.Empty).Trim(),
            AdminChatId = (adminChatId ?? string.Empty).Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(payload);
        var plainBytes = Encoding.UTF8.GetBytes(json);

        var derivedBytes = Rfc2898DeriveBytes.Pbkdf2(MasterSecret, MasterSalt, 50000, HashAlgorithmName.SHA256, 48);
        var key = derivedBytes[..32]; // 256 bits
        var iv = derivedBytes[32..48]; // 128 bits

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        return Convert.ToBase64String(cipherBytes);
    }

    /// <summary>
    /// Attempts to decrypt the cipher text payload into the Bot Token and Admin Chat ID.
    /// Returns true if successful; false if corrupted, tampered with, or invalid.
    /// </summary>
    public static bool TryDecrypt(string cipherText, out string botToken, out string adminChatId)
    {
        botToken = string.Empty;
        adminChatId = string.Empty;

        if (string.IsNullOrWhiteSpace(cipherText))
            return false;

        try
        {
            var cipherBytes = Convert.FromBase64String(cipherText.Trim());

            var derivedBytes = Rfc2898DeriveBytes.Pbkdf2(MasterSecret, MasterSalt, 50000, HashAlgorithmName.SHA256, 48);
            var key = derivedBytes[..32];
            var iv = derivedBytes[32..48];

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor();
            var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);

            var json = Encoding.UTF8.GetString(plainBytes);
            var payload = JsonSerializer.Deserialize<ConfigPayload>(json);

            if (payload != null && !string.IsNullOrWhiteSpace(payload.BotToken))
            {
                botToken = payload.BotToken;
                adminChatId = payload.AdminChatId;
                return true;
            }
        }
        catch
        {
            // Decryption failed (invalid base64, incorrect key/salt, tampered payload)
        }

        return false;
    }

    /// <summary>
    /// Attempts to load and decrypt credentials from the specified file path.
    /// </summary>
    public static bool TryLoadFromFile(string filePath, out string botToken, out string adminChatId)
    {
        botToken = string.Empty;
        adminChatId = string.Empty;

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return false;

        try
        {
            var content = File.ReadAllText(filePath);
            return TryDecrypt(content, out botToken, out adminChatId);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Encrypts and writes the Telegram configuration to the target file path.
    /// </summary>
    public static void SaveToFile(string filePath, string botToken, string adminChatId)
    {
        var cipher = Encrypt(botToken, adminChatId);
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(filePath, cipher, Encoding.UTF8);
    }
}

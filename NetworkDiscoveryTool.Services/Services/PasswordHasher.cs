using System;
using System.Security.Cryptography;
using System.Text;

namespace NetworkDiscoveryTool.Services.Services;

public static class PasswordHasher
{
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;
    private const int Iterations = 100_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;
    private const string Prefix = "PBKDF2";

    /// <summary>
    /// Hashes a plain-text password using PBKDF2 with HMAC-SHA256 and a random 128-bit salt.
    /// </summary>
    public static string HashPassword(string password)
    {
        if (password is null) throw new ArgumentNullException(nameof(password));

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            Algorithm,
            HashSizeBytes);

        return $"{Prefix}${Iterations}${Convert.ToHexString(salt)}${Convert.ToHexString(hash)}";
    }

    /// <summary>
    /// Verifies a plain-text password against a stored hash (supporting both new PBKDF2 and legacy SHA-256).
    /// </summary>
    public static bool VerifyPassword(string password, string? storedHash, out bool needsRehash)
    {
        needsRehash = false;
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(storedHash))
            return false;

        // 1. Modern PBKDF2 Hash Format: PBKDF2${iterations}${saltHex}${hashHex}
        if (storedHash.StartsWith(Prefix + "$", StringComparison.OrdinalIgnoreCase))
        {
            var parts = storedHash.Split('$');
            if (parts.Length == 4 &&
                int.TryParse(parts[1], out int iterations) &&
                iterations > 0)
            {
                try
                {
                    byte[] salt = Convert.FromHexString(parts[2]);
                    byte[] expectedHash = Convert.FromHexString(parts[3]);

                    byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(
                        password,
                        salt,
                        iterations,
                        Algorithm,
                        expectedHash.Length);

                    return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
                }
                catch
                {
                    return false;
                }
            }
            return false;
        }

        // 2. Legacy SHA-256 (64 hex characters) - Seamless Migration Path
        if (storedHash.Length == 64)
        {
            try
            {
                byte[] expectedBytes = Convert.FromHexString(storedHash);
                byte[] candidateBytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));

                if (CryptographicOperations.FixedTimeEquals(candidateBytes, expectedBytes))
                {
                    needsRehash = true; // Signal caller to upgrade hash in database
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        return false;
    }
}

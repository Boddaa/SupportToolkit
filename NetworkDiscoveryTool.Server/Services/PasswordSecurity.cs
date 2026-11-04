using System;
using System.Security.Cryptography;

namespace NetworkDiscoveryTool.Server.Services;

public static class PasswordSecurity
{
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;
    private const int Iterations = 100_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;
    private const string Prefix = "PBKDF2";

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

    public static bool VerifyPassword(string password, string? storedHash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(storedHash))
            return false;

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
        }

        // Legacy SHA256 check fallback
        try
        {
            using var sha = SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(password);
            var computed = Convert.ToHexString(sha.ComputeHash(bytes));
            return string.Equals(computed, storedHash, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}


using System.Security.Cryptography;
using System.Text;

namespace AssetManagement.Services;

public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 100_000;
    private const string CurrentPrefix = "PBKDF2";

    public static string Hash(string value)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        return HashWithSalt(value, salt);
    }

    public static bool Verify(string value, string storedHash)
    {
        if (storedHash.StartsWith($"{CurrentPrefix}$", StringComparison.Ordinal))
        {
            var parts = storedHash.Split('$');
            if (parts.Length != 4 || !int.TryParse(parts[1], out var iterations))
                return false;

            var salt = Convert.FromBase64String(parts[2]);
            var expectedKey = Convert.FromBase64String(parts[3]);
            var actualKey = Rfc2898DeriveBytes.Pbkdf2(value, salt, iterations, HashAlgorithmName.SHA256, expectedKey.Length);

            return CryptographicOperations.FixedTimeEquals(actualKey, expectedKey);
        }

        return string.Equals(storedHash, LegacySha256(value), StringComparison.OrdinalIgnoreCase);
    }

    public static bool NeedsRehash(string storedHash)
    {
        return !storedHash.StartsWith($"{CurrentPrefix}${Iterations}$", StringComparison.Ordinal);
    }

    private static string HashWithSalt(string value, byte[] salt)
    {
        var key = Rfc2898DeriveBytes.Pbkdf2(value, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{CurrentPrefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    private static string LegacySha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }
}

using System.Security.Cryptography;
using System.Text;

namespace AssetManagement.Services;

public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 210_000;
    private const string CurrentPrefix = "PBKDF2";

    private static readonly string _dummyHash = HashWithSalt(string.Empty, RandomNumberGenerator.GetBytes(SaltSize));

    public static string Hash(string value)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        return HashWithSalt(value, salt);
    }

    public static bool Verify(string value, string storedHash)
    {
        if (storedHash.StartsWith($"{CurrentPrefix}$", StringComparison.Ordinal))
        {
            try
            {
                var parts = storedHash.Split('$');
                if (parts.Length != 4 || !int.TryParse(parts[1], out var iterations))
                    return false;

                if (iterations <= 0 || iterations > 1_000_000)
                    return false;

                var salt = Convert.FromBase64String(parts[2]);
                var expectedKey = Convert.FromBase64String(parts[3]);
                if (salt.Length < SaltSize || expectedKey.Length < KeySize)
                    return false;

                var actualKey = Rfc2898DeriveBytes.Pbkdf2(value, salt, iterations, HashAlgorithmName.SHA256, expectedKey.Length);

                return CryptographicOperations.FixedTimeEquals(actualKey, expectedKey);
            }
            catch (FormatException)
            {
                return false;
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        try
        {
            var storedBytes = Convert.FromHexString(storedHash);
            var computedBytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return CryptographicOperations.FixedTimeEquals(storedBytes, computedBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    // Verifies the password against a stored hash, or performs a dummy verification
    // when storedHash is null to prevent timing-based username enumeration.
    public static bool VerifyOrFail(string value, string? storedHash)
    {
        return Verify(value, storedHash ?? _dummyHash) && storedHash is not null;
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
}

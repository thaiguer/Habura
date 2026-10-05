using System.Security.Cryptography;

namespace Habura.Api.Security;

public static class PasswordHasher
{
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static (byte[] hash, byte[] salt) CreateHash(string password)
    {
        using var rng = RandomNumberGenerator.Create();
        var salt = new byte[SaltSize];
        rng.GetBytes(salt);

        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256);
        var hash = pbkdf2.GetBytes(HashSize);
        return (hash, salt);
    }

    public static bool VerifyHash(string password, byte[] hash, byte[] salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256);
        var computed = pbkdf2.GetBytes(HashSize);
        return CryptographicOperations.FixedTimeEquals(computed, hash);
    }
}

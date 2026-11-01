using System.Security.Cryptography;
using SIMS.Application.Abstractions;

namespace SIMS.Infrastructure.Security;

/// <summary>
/// PBKDF2-HMAC-SHA256 theo spec: salt 16 bytes, ≥ 100.000 vòng lặp, key 32 bytes.
/// Định dạng lưu: <c>pbkdf2-sha256$iterations$saltBase64$hashBase64</c>.
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    public const int MinimumIterations = 100_000;

    private const int DefaultIterations = 100_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const string Prefix = "pbkdf2-sha256";

    public string Hash(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("Mật khẩu không được rỗng.", nameof(password));
        }

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            DefaultIterations,
            HashAlgorithmName.SHA256,
            KeySize);

        return string.Join(
            '$',
            Prefix,
            DefaultIterations.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(key));
    }

    public bool Verify(string password, string encodedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(encodedHash))
        {
            return false;
        }

        var parts = encodedHash.Split('$');
        if (parts.Length != 4 || !string.Equals(parts[0], Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(parts[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var iterations) ||
            iterations < MinimumIterations)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length < SaltSize || expected.Length < KeySize)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}

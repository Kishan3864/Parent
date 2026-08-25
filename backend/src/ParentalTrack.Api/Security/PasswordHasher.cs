using System.Globalization;
using System.Security.Cryptography;

namespace ParentalTrack.Api.Security;

/// <summary>
/// PBKDF2-HMAC-SHA256 password hashing.
/// Stored format: <c>pbkdf2-sha256$&lt;iterations&gt;$&lt;saltB64&gt;$&lt;hashB64&gt;</c>.
/// </summary>
public static class PasswordHasher
{
    private const string Prefix = "pbkdf2-sha256";
    private const int Iterations = 210_000;
    private const int SaltBytes = 16;
    private const int SubkeyBytes = 32;

    /// <summary>Upper bounds applied to values parsed out of a stored hash so a tampered row cannot
    /// turn a single login attempt into a CPU exhaustion attack.</summary>
    private const int MaxAcceptedIterations = 1_000_000;
    private const int MaxAcceptedSubkeyBytes = 128;

    public static string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var subkey = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, SubkeyBytes);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(subkey)}");
    }

    /// <summary>
    /// Returns false — never throws — for a null/empty/malformed stored hash, so a corrupted row
    /// behaves exactly like a wrong password.
    /// </summary>
    public static bool Verify(string password, string encodedHash)
    {
        if (password is null || string.IsNullOrEmpty(encodedHash))
        {
            return false;
        }

        var parts = encodedHash.Split('$');
        if (parts.Length != 4 || !string.Equals(parts[0], Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations)
            || iterations <= 0
            || iterations > MaxAcceptedIterations)
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

        if (salt.Length == 0 || expected.Length == 0 || expected.Length > MaxAcceptedSubkeyBytes)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}

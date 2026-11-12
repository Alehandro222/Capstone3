using System;
using System.Security.Cryptography;
using System.Text;

namespace KioskApp.Core;

// The staff PIN is stored only as a salted PBKDF2 hash, never as text, so reading
// kiosk.settings.json (visitors use these PCs) does not reveal it.
// Format: "pbkdf2-sha256$<iterations>$<salt, base64>$<hash, base64>"
public static class PinHasher
{
    private const string Scheme = "pbkdf2-sha256";
    private const int Iterations = 100_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    // A 4-digit PIN has only 10,000 values: anyone who can read the settings file could try
    // them all against the hash in minutes. 6+ characters (and a file visitors can't read) helps.
    public const int MinLength = 6;

    public static string Hash(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(pin), salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"{Scheme}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    // False for a wrong PIN and for a stored value that is not a valid hash.
    public static bool Verify(string pin, string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Scheme || !int.TryParse(parts[1], out var iterations) || iterations < 1)
            return false;

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(pin ?? ""), salt, iterations,
                HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static bool LooksHashed(string? stored) => stored?.StartsWith(Scheme + "$", StringComparison.Ordinal) == true;
}

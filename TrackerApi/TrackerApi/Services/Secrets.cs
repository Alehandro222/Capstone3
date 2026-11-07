using System.Security.Cryptography;
using System.Text;

namespace TrackerApi.Services;

// Random values for passes and the waitlist.
public static class Secrets
{
    // A long random token for the QR code (43 characters, safe in a URL).
    // Guessing one is practically impossible.
    public static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    // The database keeps only this hash, never the token itself.
    public static string Hash(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // "000000" to "999999". Only staff can look a pass up by this code.
    public static string NewSixDigitCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
}

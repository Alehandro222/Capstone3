using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrackerApi.Data;
using TrackerApi.Services;

namespace TrackerApi.Auth;

public class KioskAuthOptions : AuthenticationSchemeOptions
{
}

// Recognises the kiosk app on a PC. It sends two headers on every request:
//   X-Kiosk-Computer-Id: 4
//   X-Kiosk-Key:         the secret key PC-04 got when it was paired
// The key is checked against PC-04's stored hash only, so a key copied from
// one PC can never act as another PC.
public class KioskAuthHandler : AuthenticationHandler<KioskAuthOptions>
{
    public const string SchemeName = "Kiosk";
    public const string KeyHeader = "X-Kiosk-Key";
    public const string ComputerHeader = "X-Kiosk-Computer-Id";
    public const string ComputerClaim = "kiosk_computer_id";

    private readonly TrackerDbContext _db;

    public KioskAuthHandler(IOptionsMonitor<KioskAuthOptions> options, ILoggerFactory logger, UrlEncoder encoder,
        TrackerDbContext db) : base(options, logger, encoder)
    {
        _db = db;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(KeyHeader, out var key) || string.IsNullOrEmpty(key))
            return AuthenticateResult.NoResult(); // not a kiosk

        if (!int.TryParse(Request.Headers[ComputerHeader], out var computerId))
            return AuthenticateResult.Fail("Missing computer id.");

        var computer = await _db.Computers.AsNoTracking()
            .Where(c => c.Id == computerId)
            .Select(c => new { c.Id, c.Name, c.KioskKeyHash })
            .FirstOrDefaultAsync();

        if (computer?.KioskKeyHash is null || !SameHash(Secrets.Hash(key.ToString()), computer.KioskKeyHash))
            return AuthenticateResult.Fail("This PC is not paired, or its key is wrong.");

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, Roles.Kiosk),
            new Claim(ClaimTypes.Name, $"Kiosk {computer.Name}"),
            new Claim(ComputerClaim, computer.Id.ToString()),
        }, SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        AuthSetup.WriteError(Response, 401, "This PC is not paired with the tracker, or its key is wrong.");

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        AuthSetup.WriteError(Response, 403, "You do not have permission to do that.");

    // Compares in constant time, so nothing can be learned from how long the answer takes.
    private static bool SameHash(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}

public static class KioskClaims
{
    // The PC this kiosk request comes from (only set for kiosk requests).
    public static int KioskComputerId(this ClaimsPrincipal user) =>
        int.Parse(user.FindFirstValue(KioskAuthHandler.ComputerClaim)!);
}

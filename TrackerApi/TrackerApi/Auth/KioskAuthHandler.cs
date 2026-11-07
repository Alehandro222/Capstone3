using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace TrackerApi.Auth;

// The "Kiosk" section of appsettings.json.
public class KioskOptions
{
    public const string Section = "Kiosk";

    // The shared secret every kiosk app sends. Empty = kiosk apps cannot connect.
    // Set the same value as "kioskKey" in each PC's kiosk.settings.json.
    public string ApiKey { get; set; } = "";
}

public class KioskAuthOptions : AuthenticationSchemeOptions
{
}

// Recognises the kiosk app by the X-Kiosk-Key header. The app also sends
// X-Kiosk-Computer-Id, which is only used to label entries in the audit log.
public class KioskAuthHandler : AuthenticationHandler<KioskAuthOptions>
{
    public const string SchemeName = "Kiosk";
    public const string KeyHeader = "X-Kiosk-Key";
    public const string ComputerHeader = "X-Kiosk-Computer-Id";
    public const string ComputerClaim = "kiosk_computer_id";

    private readonly KioskOptions _kiosk;

    public KioskAuthHandler(IOptionsMonitor<KioskAuthOptions> options, ILoggerFactory logger, UrlEncoder encoder,
        IOptions<KioskOptions> kiosk) : base(options, logger, encoder)
    {
        _kiosk = kiosk.Value;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(KeyHeader, out var sent) || string.IsNullOrEmpty(sent))
            return Task.FromResult(AuthenticateResult.NoResult()); // not a kiosk; maybe staff

        if (string.IsNullOrEmpty(_kiosk.ApiKey) || !SameSecret(sent.ToString(), _kiosk.ApiKey))
            return Task.FromResult(AuthenticateResult.Fail("Wrong kiosk key."));

        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, Roles.Kiosk),
            new(ClaimTypes.Name, "Kiosk app"),
        };
        if (int.TryParse(Request.Headers[ComputerHeader], out var computerId))
        {
            claims.Add(new Claim(ComputerClaim, computerId.ToString()));
            claims[1] = new Claim(ClaimTypes.Name, $"Kiosk app (computer {computerId})");
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        AuthSetup.WriteError(Response, 401, "Please sign in.");

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        AuthSetup.WriteError(Response, 403, "You do not have permission to do that.");

    // Compares in constant time, so the key cannot be guessed by timing the answers.
    private static bool SameSecret(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}

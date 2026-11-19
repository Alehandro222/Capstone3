using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace TrackerApi.Auth;

public class AutomationAuthOptions : AuthenticationSchemeOptions
{
}

// Lets an automation tool (n8n, Activepieces, a scheduled job) start and read reports without a
// staff login. It sends the header  X-Automation-Token: <Automation:Token>.
//
// The token only opens the routes that name Policies.ReportAutomation or Policies.ReportReader
// (report runs and the exports WITHOUT names); it is never an admin: lists with names, staff,
// settings and everything else still need a staff login. Switched off unless Automation:Token
// is set to at least MinLength characters (set it as the environment variable Automation__Token).
public class AutomationAuthHandler : AuthenticationHandler<AutomationAuthOptions>
{
    public const string SchemeName = "Automation";
    public const string TokenHeader = "X-Automation-Token";
    public const int MinLength = 32;

    private readonly string _token;

    public AutomationAuthHandler(IOptionsMonitor<AutomationAuthOptions> options, ILoggerFactory logger,
        UrlEncoder encoder, IConfiguration config) : base(options, logger, encoder)
    {
        _token = config["Automation:Token"] ?? "";
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(TokenHeader, out var sent) || string.IsNullOrEmpty(sent))
            return Task.FromResult(AuthenticateResult.NoResult()); // not an automation call

        // Constant time, on hashes of equal length, so nothing leaks through timing.
        if (_token.Length < MinLength || !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(sent.ToString())), SHA256.HashData(Encoding.UTF8.GetBytes(_token))))
            return Task.FromResult(AuthenticateResult.Fail("The automation token is wrong or not set up."));

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, Roles.Automation),
            new Claim(ClaimTypes.Name, "Automation"),
        }, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        AuthSetup.WriteError(Response, 401, "Please sign in.");

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        AuthSetup.WriteError(Response, 403, "You do not have permission to do that.");
}

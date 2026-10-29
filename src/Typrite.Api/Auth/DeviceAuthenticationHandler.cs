using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Typrite.Domain.Devices;
using Typrite.Infrastructure.Persistence;

namespace Typrite.Api.Auth;

/// <summary>
/// Device bearer auth. A JWT (three segments) is a short-lived device token issued to the agent; anything else is a
/// development secret looked up by hash. Dev secrets are only honoured for devices enrolled as <see cref="DeviceCredentialKind.Dev"/>.
/// </summary>
public sealed class DeviceAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    TokenIssuer tokens,
    TypriteDbContext db) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Device";

    public static string Hash(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = header["Bearer ".Length..].Trim();
        if (token.Length == 0)
        {
            return AuthenticateResult.NoResult();
        }

        if (token.Count(c => c == '.') == 2)
        {
            var principal = await tokens.ValidateAsync(token, TokenIssuer.DeviceAudience).ConfigureAwait(false);
            return principal is null
                ? AuthenticateResult.Fail("Invalid device token.")
                : AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
        }

        var hash = Hash(token);
        var device = await db.Devices.AsNoTracking()
            .FirstOrDefaultAsync(d => d.CredentialHash == hash && d.CredentialKind == DeviceCredentialKind.Dev && d.Status == DeviceStatus.Active, Context.RequestAborted)
            .ConfigureAwait(false);
        if (device is null)
        {
            return AuthenticateResult.Fail("Unknown device secret.");
        }

        var identity = new ClaimsIdentity(
        [
            new Claim("sub", device.Id.ToString()),
            new Claim(Claims.DeviceId, device.Id.ToString()),
            new Claim(Claims.AccountId, device.AccountId?.ToString() ?? string.Empty),
            new Claim(Claims.CredentialKind, "dev"),
        ], SchemeName, "sub", Claims.Role);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}

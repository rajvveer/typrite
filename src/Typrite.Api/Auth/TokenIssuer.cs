using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Typrite.Api.Auth;

/// <summary>Issues and validates the tokens this API signs itself.</summary>
public sealed class TokenIssuer
{
    public const string DeviceAudience = "typrite-device";
    public const string OperatorAudience = "typrite-operator";

    private readonly AuthOptions _options;
    private readonly JsonWebTokenHandler _handler = new();

    public TokenIssuer(AuthOptions options, TimeProvider time)
    {
        _options = options;
        Time = time;
        var raw = string.IsNullOrWhiteSpace(options.SigningKey)
            ? RandomNumberGenerator.GetBytes(64)
            : Encoding.UTF8.GetBytes(options.SigningKey);
        if (raw.Length < 32)
        {
            throw new InvalidOperationException("Auth:SigningKey must be at least 32 bytes.");
        }

        Key = new SymmetricSecurityKey(raw);
    }

    public SymmetricSecurityKey Key { get; }

    public TimeProvider Time { get; }

    public string IssueDeviceToken(Guid deviceId, Guid? accountId, string credentialKind) =>
        Issue(DeviceAudience, TimeSpan.FromMinutes(_options.DeviceTokenMinutes),
        [
            new Claim("sub", deviceId.ToString()),
            new Claim(Claims.DeviceId, deviceId.ToString()),
            new Claim(Claims.AccountId, accountId?.ToString() ?? string.Empty),
            new Claim(Claims.CredentialKind, credentialKind),
        ]);

    public string IssueOperatorToken(string subject, string role, Guid accountId) =>
        Issue(OperatorAudience, TimeSpan.FromHours(8),
        [
            new Claim("sub", subject),
            new Claim(Claims.Role, role),
            new Claim(Claims.AccountId, accountId.ToString()),
        ]);

    public TokenValidationParameters ValidationFor(string audience) => new()
    {
        ValidIssuer = _options.Issuer,
        ValidAudience = audience,
        IssuerSigningKey = Key,
        ClockSkew = TimeSpan.FromMinutes(1),
        NameClaimType = "sub",
        RoleClaimType = Claims.Role,
    };

    public async Task<ClaimsPrincipal?> ValidateAsync(string token, string audience)
    {
        var result = await _handler.ValidateTokenAsync(token, ValidationFor(audience)).ConfigureAwait(false);
        return result.IsValid ? new ClaimsPrincipal(result.ClaimsIdentity) : null;
    }

    private string Issue(string audience, TimeSpan lifetime, IEnumerable<Claim> claims)
    {
        var now = Time.GetUtcNow().UtcDateTime;
        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now + lifetime,
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.HmacSha256),
        });
    }
}

public static class Claims
{
    public const string DeviceId = "device_id";
    public const string AccountId = "account_id";
    public const string CredentialKind = "credential_kind";
    public const string Role = "role";
}

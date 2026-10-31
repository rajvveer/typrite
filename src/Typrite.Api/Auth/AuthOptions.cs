namespace Typrite.Api.Auth;

public sealed record AuthOptions
{
    /// <summary>Enables dev-register and dev-login. Must be false outside local development and CI.</summary>
    public bool AllowDevAuth { get; init; }

    /// <summary>Symmetric key for tokens the API issues itself (device tokens, dev operator tokens). Generated per run when empty.</summary>
    public string? SigningKey { get; init; }

    /// <summary>OIDC authority for operator tokens in production. When set, dev-login tokens are not accepted for operators.</summary>
    public string? OperatorAuthority { get; init; }

    public string? OperatorAudience { get; init; }

    public string? OperatorClientId { get; init; }

    public string OperatorScopes { get; init; } = "openid profile";

    public string Issuer { get; init; } = "typrite";

    public int DeviceTokenMinutes { get; init; } = 30;

    /// <summary>Account whose operators may manage releases and see partner reconciliation. Defaults to the seeded platform account.</summary>
    public Guid PlatformAccountId { get; init; } = new("00000000-0000-0000-0000-000000000001");
}

namespace Typrite.Domain.Accounts;

/// <summary>The tenant. Every admin query is scoped to one of these.</summary>
public sealed class Account
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public AccountStatus Status { get; set; } = AccountStatus.Active;

    public DateTimeOffset CreatedAtUtc { get; set; }
}

public enum AccountStatus
{
    Active,
    Suspended,
}

public enum OperatorRole
{
    Viewer,
    CampaignOperator,
    DeviceOperator,
    ReleaseManager,
    Administrator,
}

public sealed class OperatorAccount
{
    public Guid Id { get; set; }

    /// <summary>OIDC subject claim.</summary>
    public required string ExternalSubject { get; set; }

    public Guid AccountId { get; set; }

    public OperatorRole Role { get; set; }

    public string? DisplayName { get; set; }
}

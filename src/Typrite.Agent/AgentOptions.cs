namespace Typrite.Agent;

public sealed record AgentOptions
{
    public string ApiBaseUrl { get; init; } = "https://localhost:7210/";

    /// <summary>Pinned publisher public key (SPKI PEM) used to verify release manifests. Required for updates.</summary>
    public string PublisherPublicKeyPem { get; init; } = string.Empty;

    /// <summary>Per-machine data directory. Defaults to ProgramData\Typrite on Windows.</summary>
    public string DataDirectory { get; init; } = string.Empty;

    /// <summary>One-time enrollment claim, usually dropped as <c>claim.token</c> by the installer rather than configured here.</summary>
    public string? ClaimToken { get; init; }

    public int HeartbeatSeconds { get; init; } = 300;

    public int CommandPollSeconds { get; init; } = 60;

    public int ReleaseCheckMinutes { get; init; } = 60;

    public string ResolvedDataDirectory => string.IsNullOrWhiteSpace(DataDirectory)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Typrite")
        : DataDirectory;
}

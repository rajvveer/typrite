namespace Typrite.Contracts.Agent;

/// <summary>
/// Line-delimited JSON over the local named pipe between the desktop bar (client) and the agent (server).
/// One request per line, one response per line. The agent never accepts commands from the desktop; it only answers.
/// </summary>
public static class AgentPipe
{
    public const string Name = "Typrite.Agent";
}

public sealed record PipeRequest(string Op, string? Arg = null);

public sealed record PipeResponse(bool Ok, string? Error = null, string? Token = null, DateTimeOffset? ExpiresAtUtc = null,
    Guid? DeviceId = null, Guid? AccountId = null, string? Command = null);

public static class PipeOps
{
    /// <summary>Short-lived delivery token for the desktop.</summary>
    public const string Token = "token";

    /// <summary>Device identity without a token.</summary>
    public const string Identity = "identity";

    /// <summary>Next pending command addressed to the desktop (enter-safe-mode, leave-safe-mode, restart-bar, refresh-configuration), or none.</summary>
    public const string NextCommand = "next-command";

    public const string Ping = "ping";
}

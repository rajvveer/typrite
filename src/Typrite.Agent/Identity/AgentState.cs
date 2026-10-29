using System.Text.Json;

namespace Typrite.Agent.Identity;

/// <summary>What survives restarts: who this device is. The private key is in <see cref="DeviceKeyStore"/>, never here.</summary>
public sealed record AgentState(Guid? DeviceId, Guid? AccountId, string? LastInstalledVersion, string? LastAttemptedVersion)
{
    public static AgentState Empty { get; } = new(null, null, null, null);

    public bool IsEnrolled => DeviceId is not null;

    public static AgentState Load(string dataDirectory)
    {
        var path = PathFor(dataDirectory);
        return File.Exists(path) ? JsonSerializer.Deserialize<AgentState>(File.ReadAllText(path)) ?? Empty : Empty;
    }

    public void Save(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var path = PathFor(dataDirectory);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this));
        File.Move(path + ".tmp", path, overwrite: true);
    }

    private static string PathFor(string dir) => Path.Combine(dir, "agent-state.json");
}

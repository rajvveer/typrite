using System.Security.Cryptography;
using System.Text.Json;

namespace Typrite.Desktop;

/// <summary>
/// Development-only device identity (guide section 3, phase P3/P4): a random secret protected with DPAPI for the
/// current user. Replaced by agent-brokered short-lived tokens in P6; the server marks these devices as <c>dev</c>.
/// </summary>
public sealed record DevDeviceCredential(Guid DeviceId, string Secret)
{
    private static readonly byte[] Entropy = "Typrite.DevDeviceCredential.v1"u8.ToArray();

    public static DevDeviceCredential LoadOrCreate(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "device.json");
        if (File.Exists(path))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var protectedSecret = Convert.FromBase64String(doc.RootElement.GetProperty("secret").GetString()!);
            var secret = ProtectedData.Unprotect(protectedSecret, Entropy, DataProtectionScope.CurrentUser);
            return new DevDeviceCredential(doc.RootElement.GetProperty("deviceId").GetGuid(), Convert.ToBase64String(secret));
        }

        var fresh = new DevDeviceCredential(Guid.NewGuid(), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        var stored = new
        {
            deviceId = fresh.DeviceId,
            secret = Convert.ToBase64String(ProtectedData.Protect(Convert.FromBase64String(fresh.Secret), Entropy, DataProtectionScope.CurrentUser)),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(stored));
        return fresh;
    }
}

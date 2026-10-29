using System.Security.Cryptography;
using System.Text;

namespace Typrite.Shell.Displays;

/// <summary>EDID fields that survive re-docking and reboots. Serial may be absent on cheap panels.</summary>
public sealed record EdidIdentity(string ManufacturerId, ushort ProductCode, string? SerialNumber);

/// <summary>
/// Stable identity for a display, used to key per-display state and delivery events.
/// Prefer EDID; fall back to the device path with the volatile instance segment removed. Never a monitor handle or \\.\DISPLAYn.
/// </summary>
public sealed record DisplayKey(string Value)
{
    public static DisplayKey From(string devicePath, EdidIdentity? edid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);

        var material = edid is null
            ? "path:" + StripInstance(devicePath)
            : $"edid:{edid.ManufacturerId.ToUpperInvariant()}:{edid.ProductCode:X4}:{edid.SerialNumber ?? string.Empty}";

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return new DisplayKey(Convert.ToHexStringLower(hash.AsSpan(0, 16)));
    }

    /// <summary>Device paths look like \\?\DISPLAY#DELA1C3#5&amp;2b1f0a2&amp;0&amp;UID4357#{guid}. Segment 2 is the bus instance and changes per port.</summary>
    private static string StripInstance(string devicePath)
    {
        var parts = devicePath.Split('#');
        return parts.Length >= 4
            ? string.Join('#', parts[0], parts[1], parts[3]).ToUpperInvariant()
            : devicePath.ToUpperInvariant();
    }
}

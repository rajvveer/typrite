using System.Runtime.InteropServices;
using Typrite.Shell.Displays;
using Typrite.Shell.Geometry;
using static Typrite.Shell.Win32.NativeMethods;

namespace Typrite.Shell.Win32;

/// <summary>
/// Joins GDI monitors (bounds, DPI) with display-config targets (device path, EDID ids) on the GDI device name,
/// producing <see cref="DisplayInfo"/> with keys that survive re-docking.
/// </summary>
public static unsafe class DisplayEnumerator
{
    public static IReadOnlyList<DisplayInfo> Enumerate()
    {
        var targets = TargetsByGdiName();
        var result = new List<DisplayInfo>();

        EnumDisplayMonitors(0, 0, (hMonitor, _, _, _) =>
        {
            var info = new MONITORINFOEXW { cbSize = (uint)Marshal.SizeOf<MONITORINFOEXW>() };
            if (!GetMonitorInfo(hMonitor, ref info))
            {
                return true;
            }

            var gdiName = Fixed(info.szDevice, 32);
            var bounds = new PhysicalRect(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right, info.rcMonitor.Bottom);
            var dpi = GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 ? dpiX : DpiScale.BaseDpi;

            var key = targets.TryGetValue(gdiName, out var t)
                ? DisplayKey.From(t.DevicePath, t.Edid)
                : DisplayKey.From(gdiName, null);

            result.Add(new DisplayInfo(key, bounds, dpi));
            return true;
        }, 0);

        return result;
    }

    private static Dictionary<string, (string DevicePath, EdidIdentity? Edid)> TargetsByGdiName()
    {
        var map = new Dictionary<string, (string, EdidIdentity?)>(StringComparer.OrdinalIgnoreCase);
        if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount) != 0)
        {
            return map;
        }

        var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
        if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, 0) != 0)
        {
            return map;
        }

        for (var i = 0; i < pathCount; i++)
        {
            var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
            {
                header = Header(DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME, Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(), paths[i].sourceInfo.adapterId, paths[i].sourceInfo.id),
            };
            var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME
            {
                header = Header(DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME, Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(), paths[i].targetInfo.adapterId, paths[i].targetInfo.id),
            };
            if (DisplayConfigGetDeviceInfo(ref source) != 0 || DisplayConfigGetDeviceInfo(ref target) != 0)
            {
                continue;
            }

            var gdi = Fixed(source.viewGdiDeviceName, 32);
            var path = Fixed(target.monitorDevicePath, 128);
            var edid = target.edidManufactureId == 0 && target.edidProductCodeId == 0
                ? null
                : new EdidIdentity(DecodeManufacturer(target.edidManufactureId), target.edidProductCodeId, SerialNumber: null);
            map[gdi] = (path, edid);
        }

        return map;
    }

    private static DISPLAYCONFIG_DEVICE_INFO_HEADER Header(uint type, int size, LUID adapter, uint id) =>
        new() { type = type, size = (uint)size, adapterId = adapter, id = id };

    /// <summary>EDID packs three 5-bit letters big-endian; the struct holds the raw bytes little-endian.</summary>
    internal static string DecodeManufacturer(ushort raw)
    {
        var swapped = (ushort)((raw << 8) | (raw >> 8));
        return new string(
        [
            (char)('A' - 1 + ((swapped >> 10) & 0x1F)),
            (char)('A' - 1 + ((swapped >> 5) & 0x1F)),
            (char)('A' - 1 + (swapped & 0x1F)),
        ]);
    }

    private static string Fixed(char* buffer, int capacity)
    {
        var span = new ReadOnlySpan<char>(buffer, capacity);
        var end = span.IndexOf('\0');
        return new string(end < 0 ? span : span[..end]);
    }
}

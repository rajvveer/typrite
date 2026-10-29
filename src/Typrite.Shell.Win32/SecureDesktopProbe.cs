using static Typrite.Shell.Win32.NativeMethods;

namespace Typrite.Shell.Win32;

/// <summary>
/// True when the interactive input desktop is not the user's "Default" desktop (UAC prompt, lock screen, Winlogon).
/// A standard-user process cannot open the secure desktop at all, so failure is treated as secure: fail closed.
/// </summary>
public static class SecureDesktopProbe
{
    public static bool IsSecureDesktopActive()
    {
        var desktop = OpenInputDesktop(0, false, DESKTOP_READOBJECTS);
        if (desktop == 0)
        {
            return true;
        }

        try
        {
            Span<char> name = stackalloc char[64];
            if (!GetUserObjectInformation(desktop, UOI_NAME, name, (uint)(name.Length * sizeof(char)), out var needed))
            {
                return true;
            }

            var len = Math.Max(0, (int)(needed / sizeof(char)) - 1);
            return !name[..len].Equals("Default", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CloseDesktop(desktop);
        }
    }
}

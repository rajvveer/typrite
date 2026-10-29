using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;
using Typrite.Shell.Fullscreen;
using Typrite.Shell.Geometry;
using static Typrite.Shell.Win32.NativeMethods;

namespace Typrite.Shell.Win32;

/// <summary>Snapshots top-level windows in z-order (top first) with the facts the detector needs.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowEnumerator
{
    private static readonly HashSet<string> ShellClasses =
        new(StringComparer.Ordinal) { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

    private readonly uint _ownPid = (uint)Environment.ProcessId;
    private readonly Dictionary<uint, AppIdentity?> _byPid = [];
    private readonly Dictionary<string, string?> _signerByPath = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<WindowObservation> Snapshot()
    {
        var result = new List<WindowObservation>();
        EnumWindows((hwnd, _) =>
        {
            var o = Observe(hwnd);
            if (o is not null)
            {
                result.Add(o);
            }

            return true;
        }, 0);
        return result;
    }

    /// <summary>Forget cached identities for processes that no longer exist. Call occasionally, not per snapshot.</summary>
    public void TrimCaches() => _byPid.Clear();

    private WindowObservation? Observe(nint hwnd)
    {
        if (!IsWindowVisible(hwnd))
        {
            return null;
        }

        var bounds = GetBounds(hwnd);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return null;
        }

        var style = (long)GetWindowLongPtr(hwnd, GWL_STYLE);
        var exStyle = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        GetWindowThreadProcessId(hwnd, out var pid);

        return new WindowObservation(
            Handle: hwnd,
            Bounds: bounds,
            IsVisible: true,
            IsCloaked: IsCloaked(hwnd),
            IsMinimized: IsIconic(hwnd),
            IsToolWindow: (exStyle & WS_EX_TOOLWINDOW) != 0,
            IsOwnWindow: pid == _ownPid,
            IsShellWindow: ShellClasses.Contains(ClassName(hwnd)),
            App: Identity(pid),
            IsBorderless: (style & WS_CAPTION) != WS_CAPTION);
    }

    private static PhysicalRect GetBounds(nint hwnd)
    {
        // GetWindowRect includes the invisible resize border on Windows 10/11; DWM reports what is actually drawn.
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT dwm, (uint)System.Runtime.InteropServices.Marshal.SizeOf<RECT>()) == 0)
        {
            return new PhysicalRect(dwm.Left, dwm.Top, dwm.Right, dwm.Bottom);
        }

        GetWindowRect(hwnd, out var r);
        return new PhysicalRect(r.Left, r.Top, r.Right, r.Bottom);
    }

    private static bool IsCloaked(nint hwnd) =>
        DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    private static string ClassName(nint hwnd)
    {
        Span<char> buffer = stackalloc char[64];
        var len = GetClassName(hwnd, buffer, buffer.Length);
        return len > 0 ? new string(buffer[..len]) : string.Empty;
    }

    private AppIdentity? Identity(uint pid)
    {
        if (_byPid.TryGetValue(pid, out var cached))
        {
            return cached;
        }

        var path = ImagePath(pid);
        var identity = path is null ? null : new AppIdentity(path, Signer(path));
        _byPid[pid] = identity;
        return identity;
    }

    private static string? ImagePath(uint pid)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == 0)
        {
            return null;
        }

        try
        {
            Span<char> buffer = stackalloc char[1024];
            var size = (uint)buffer.Length;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? new string(buffer[..(int)size]) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private string? Signer(string path)
    {
        if (_signerByPath.TryGetValue(path, out var cached))
        {
            return cached;
        }

        string? subject;
        try
        {
            // SYSLIB0057 points at X509CertificateLoader, which has no Authenticode (signed PE) entry point.
#pragma warning disable SYSLIB0057
            using var cert = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            subject = cert.Subject;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            subject = null; // unsigned or unreadable: never allowlisted
        }

        _signerByPath[path] = subject;
        return subject;
    }
}

using Typrite.Shell.Displays;
using Typrite.Shell.Fullscreen;
using Typrite.Shell.Geometry;

namespace Typrite.Shell.Tests;

public class FullscreenObserverTests
{
    private static readonly DisplayInfo Primary = new(new DisplayKey("a".PadLeft(32, 'a')), new PhysicalRect(0, 0, 1920, 1080), 96);
    private static readonly DisplayInfo Secondary = new(new DisplayKey("b".PadLeft(32, 'b')), new PhysicalRect(1920, 0, 3840, 1080), 96);
    private static readonly AppIdentity Chrome = new(@"C:\chrome.exe", "CN=Google LLC");
    private static readonly AppIdentity Vlc = new(@"C:\vlc.exe", "CN=VideoLAN");

    private static WindowObservation Win(PhysicalRect bounds, AppIdentity app, bool borderless = true, nint handle = 1) =>
        new(handle, bounds, IsVisible: true, IsCloaked: false, IsMinimized: false, IsToolWindow: false,
            IsOwnWindow: false, IsShellWindow: false, app, IsBorderless: borderless);

    [Fact]
    public void Topmost_covering_window_on_this_display_is_the_candidate()
    {
        var top = Win(Primary.Bounds, Chrome, handle: 1);
        var behind = Win(Primary.Bounds, Vlc, handle: 2);

        var o = FullscreenObserver.Observe(Primary, [top, behind], secureDesktop: false, FullscreenPolicy.YieldByDefault, _ => false);

        Assert.Same(top, o.FullscreenCandidate);
    }

    [Fact]
    public void Window_covering_another_display_is_ignored()
    {
        var onSecondary = Win(Secondary.Bounds, Chrome);

        var o = FullscreenObserver.Observe(Primary, [onSecondary], false, FullscreenPolicy.YieldByDefault, _ => false);

        Assert.Null(o.FullscreenCandidate);
    }

    [Fact]
    public void Non_covering_windows_above_a_covering_one_do_not_hide_it()
    {
        var small = Win(new PhysicalRect(10, 10, 400, 300), Vlc, handle: 1);
        var full = Win(Primary.Bounds, Chrome, handle: 2);

        var o = FullscreenObserver.Observe(Primary, [small, full], false, FullscreenPolicy.YieldByDefault, _ => false);

        Assert.Same(full, o.FullscreenCandidate);
    }

    [Fact]
    public void Secure_desktop_is_reported_as_protected()
    {
        var o = FullscreenObserver.Observe(Primary, [], secureDesktop: true, FullscreenPolicy.YieldByDefault, _ => false);

        Assert.True(o.IsProtectedOrSecureContext);
    }

    [Fact]
    public void Borderless_flag_comes_from_the_candidate()
    {
        var captioned = Win(Primary.Bounds, Chrome, borderless: false);

        var o = FullscreenObserver.Observe(Primary, [captioned], false, FullscreenPolicy.YieldByDefault, _ => true);

        Assert.False(o.IsBorderlessCompatible);
    }

    [Fact]
    public void Overlay_probe_runs_only_for_allowlisted_apps()
    {
        var probed = new List<nint>();
        bool Probe(WindowObservation w)
        {
            probed.Add(w.Handle);
            return true;
        }

        var policy = FullscreenPolicy.YieldByDefault.Allow(Chrome);
        var unknown = FullscreenObserver.Observe(Primary, [Win(Primary.Bounds, Vlc, handle: 7)], false, policy, Probe);
        var allowed = FullscreenObserver.Observe(Primary, [Win(Primary.Bounds, Chrome, handle: 8)], false, policy, Probe);

        Assert.False(unknown.OverlayValidationPassed);
        Assert.True(allowed.OverlayValidationPassed);
        Assert.Equal([8], probed);
    }
}

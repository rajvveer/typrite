using Typrite.Shell.AppBar;
using Typrite.Shell.Fullscreen;
using Typrite.Shell.Geometry;

namespace Typrite.Shell.Tests;

public class BarCoordinatorObservationTests
{
    private static readonly PhysicalRect Monitor = new(0, 0, 1920, 1080);
    private static readonly AppIdentity Chrome = new(@"C:\chrome.exe", "CN=Google LLC");

    private sealed class Host : IAppBarHost
    {
        public int Registrations { get; private set; }
        public event EventHandler<bool>? FullscreenAppChanged;
        public event EventHandler? PositionChanged;
        public PhysicalRect Register(nint hwnd, PhysicalRect monitor, int heightPx) { Registrations++; return monitor; }
        public PhysicalRect Reposition(PhysicalRect monitor, int heightPx) => monitor;
        public void Unregister() { }
        public void Dispose() { }
        public void Raise(bool started) => FullscreenAppChanged?.Invoke(this, started);
        public void RaisePos() => PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Surface : IBarSurface
    {
        public List<string> Calls { get; } = [];
        public void SendToBottom() => Calls.Add("bottom");
        public void BringToTop() => Calls.Add("top");
        public void EnterOverlay() => Calls.Add("overlay-on");
        public void LeaveOverlay() => Calls.Add("overlay-off");
    }

    private static WindowObservation Candidate() =>
        new(1, Monitor, true, false, false, false, false, false, Chrome, IsBorderless: true);

    private static DisplayObservation Seen(WindowObservation? w, bool secure = false, bool probe = true) =>
        new(secure, w, IsBorderlessCompatible: w?.IsBorderless ?? false, OverlayValidationPassed: probe);

    private static (BarCoordinator Bar, Host Host, Surface Surface) Make(FullscreenPolicy? policy = null)
    {
        var host = new Host();
        var surface = new Surface();
        var bar = new BarCoordinator(host, surface, 210) { Policy = policy ?? FullscreenPolicy.YieldByDefault };
        bar.Attach(1, Monitor, 96);
        return (bar, host, surface);
    }

    [Fact]
    public void Candidate_from_unknown_app_yields()
    {
        var (bar, _, surface) = Make();

        bar.Reevaluate(Seen(Candidate()));

        Assert.Equal(DisplayMode.YieldedFullscreen, bar.Mode);
        Assert.Equal(["bottom"], surface.Calls);
    }

    [Fact]
    public void Allowlisted_validated_candidate_enters_overlay()
    {
        var (bar, _, surface) = Make(FullscreenPolicy.YieldByDefault.Allow(Chrome));

        bar.Reevaluate(Seen(Candidate()));

        Assert.Equal(DisplayMode.CompatibleOverlay, bar.Mode);
        Assert.Equal(["overlay-on"], surface.Calls);
    }

    [Fact]
    public void Overlay_that_fails_probe_falls_back_to_yield()
    {
        var (bar, _, surface) = Make(FullscreenPolicy.YieldByDefault.Allow(Chrome));
        bar.Reevaluate(Seen(Candidate()));

        bar.Reevaluate(Seen(Candidate(), probe: false));

        Assert.Equal(DisplayMode.YieldedFullscreen, bar.Mode);
        Assert.Equal(["overlay-on", "overlay-off", "bottom"], surface.Calls);
    }

    [Fact]
    public void Candidate_disappearing_restores_normal_from_overlay()
    {
        var (bar, _, surface) = Make(FullscreenPolicy.YieldByDefault.Allow(Chrome));
        bar.Reevaluate(Seen(Candidate()));

        bar.Reevaluate(Seen(null));

        Assert.Equal(DisplayMode.NormalReserved, bar.Mode);
        Assert.Equal(["overlay-on", "overlay-off", "top"], surface.Calls);
    }

    [Fact]
    public void Candidate_disappearing_restores_normal_from_yield()
    {
        var (bar, _, surface) = Make();
        bar.Reevaluate(Seen(Candidate()));

        bar.Reevaluate(Seen(null));

        Assert.Equal(DisplayMode.NormalReserved, bar.Mode);
        Assert.Equal(["bottom", "top"], surface.Calls);
    }

    [Fact]
    public void Secure_desktop_from_normal_hides_bar_and_restores_via_reregistration()
    {
        var (bar, host, surface) = Make();

        bar.Reevaluate(Seen(null, secure: true));
        Assert.Equal(DisplayMode.ProtectedSurface, bar.Mode);
        Assert.Equal(["bottom"], surface.Calls);

        bar.Reevaluate(Seen(null));
        Assert.Equal(DisplayMode.NormalReserved, bar.Mode);
        Assert.Equal(2, host.Registrations);
        Assert.Equal(["bottom", "top"], surface.Calls);
    }

    [Fact]
    public void Secure_desktop_while_yielded_moves_to_protected()
    {
        var (bar, _, _) = Make();
        bar.Reevaluate(Seen(Candidate()));

        bar.Reevaluate(Seen(Candidate(), secure: true));

        Assert.Equal(DisplayMode.ProtectedSurface, bar.Mode);
    }

    [Fact]
    public void Same_observation_twice_is_idempotent()
    {
        var (bar, _, surface) = Make();

        bar.Reevaluate(Seen(Candidate()));
        bar.Reevaluate(Seen(Candidate()));

        Assert.Equal(["bottom"], surface.Calls);
    }

    [Fact]
    public void Shell_fullscreen_notification_triggers_observation_when_observer_is_set()
    {
        var host = new Host();
        var surface = new Surface();
        var observed = 0;
        var bar = new BarCoordinator(host, surface, 210)
        {
            Observer = () => { observed++; return Seen(Candidate()); },
        };
        bar.Attach(1, Monitor, 96);

        host.Raise(true);

        Assert.Equal(1, observed);
        Assert.Equal(DisplayMode.YieldedFullscreen, bar.Mode);
    }

    [Fact]
    public void Safe_mode_ignores_observations_until_recovery_is_authorized()
    {
        var (bar, host, surface) = Make();

        bar.EnterSafeMode();
        bar.Reevaluate(Seen(Candidate()));
        Assert.Equal(DisplayMode.SafeMode, bar.Mode);
        Assert.Equal(["bottom"], surface.Calls);

        bar.AuthorizeRecovery();
        Assert.Equal(DisplayMode.NormalReserved, bar.Mode);
        Assert.Equal(2, host.Registrations);
    }
}

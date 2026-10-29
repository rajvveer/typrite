using Typrite.Shell.Fullscreen;
using Typrite.Shell.Geometry;

namespace Typrite.Shell.Tests;

public class FullscreenClassifierTests
{
    private static readonly PhysicalRect Monitor = new(0, 0, 1920, 1080);
    private static readonly AppIdentity Chrome = new(@"C:\Program Files\Google\Chrome\Application\chrome.exe", "CN=Google LLC");

    private static WindowObservation Window(PhysicalRect bounds, AppIdentity? app = null) =>
        new(Handle: 0x1234, Bounds: bounds, IsVisible: true, IsCloaked: false, IsMinimized: false,
            IsToolWindow: false, IsOwnWindow: false, IsShellWindow: false, App: app ?? Chrome);

    [Fact]
    public void Window_covering_monitor_is_candidate()
    {
        Assert.True(FullscreenCandidateDetector.IsCandidate(Window(Monitor), Monitor, tolerancePx: 2));
    }

    [Fact]
    public void Window_within_tolerance_is_candidate()
    {
        var almost = new PhysicalRect(-1, -1, 1921, 1081);

        Assert.True(FullscreenCandidateDetector.IsCandidate(Window(almost), Monitor, tolerancePx: 2));
    }

    [Fact]
    public void Maximized_window_that_respects_work_area_is_not_candidate()
    {
        var maximized = new PhysicalRect(0, 0, 1920, 870);

        Assert.False(FullscreenCandidateDetector.IsCandidate(Window(maximized), Monitor, tolerancePx: 2));
    }

    [Theory]
    [InlineData(nameof(WindowObservation.IsCloaked))]
    [InlineData(nameof(WindowObservation.IsMinimized))]
    [InlineData(nameof(WindowObservation.IsToolWindow))]
    [InlineData(nameof(WindowObservation.IsOwnWindow))]
    [InlineData(nameof(WindowObservation.IsShellWindow))]
    public void Excluded_window_kinds_are_never_candidates(string flag)
    {
        var w = Window(Monitor);
        w = flag switch
        {
            nameof(WindowObservation.IsCloaked) => w with { IsCloaked = true },
            nameof(WindowObservation.IsMinimized) => w with { IsMinimized = true },
            nameof(WindowObservation.IsToolWindow) => w with { IsToolWindow = true },
            nameof(WindowObservation.IsOwnWindow) => w with { IsOwnWindow = true },
            nameof(WindowObservation.IsShellWindow) => w with { IsShellWindow = true },
            _ => throw new ArgumentOutOfRangeException(nameof(flag)),
        };

        Assert.False(FullscreenCandidateDetector.IsCandidate(w, Monitor, tolerancePx: 2));
    }

    [Fact]
    public void Invisible_window_is_not_candidate()
    {
        Assert.False(FullscreenCandidateDetector.IsCandidate(Window(Monitor) with { IsVisible = false }, Monitor, 2));
    }

    [Fact]
    public void Protected_context_wins_over_everything()
    {
        var o = new DisplayObservation(IsProtectedOrSecureContext: true, FullscreenCandidate: Window(Monitor),
            IsBorderlessCompatible: true, OverlayValidationPassed: true);

        Assert.Equal(DisplayMode.ProtectedSurface, FullscreenClassifier.Decide(o, FullscreenPolicy.YieldByDefault));
    }

    [Fact]
    public void No_candidate_means_normal_reserved()
    {
        var o = new DisplayObservation(false, FullscreenCandidate: null, false, false);

        Assert.Equal(DisplayMode.NormalReserved, FullscreenClassifier.Decide(o, FullscreenPolicy.YieldByDefault));
    }

    [Fact]
    public void Unknown_app_yields()
    {
        var o = new DisplayObservation(false, Window(Monitor), IsBorderlessCompatible: true, OverlayValidationPassed: true);

        Assert.Equal(DisplayMode.YieldedFullscreen, FullscreenClassifier.Decide(o, FullscreenPolicy.YieldByDefault));
    }

    [Fact]
    public void Allowlisted_borderless_validated_app_gets_overlay()
    {
        var policy = FullscreenPolicy.YieldByDefault.Allow(Chrome);
        var o = new DisplayObservation(false, Window(Monitor), IsBorderlessCompatible: true, OverlayValidationPassed: true);

        Assert.Equal(DisplayMode.CompatibleOverlay, FullscreenClassifier.Decide(o, policy));
    }

    [Fact]
    public void Allowlisted_app_without_validation_still_yields()
    {
        var policy = FullscreenPolicy.YieldByDefault.Allow(Chrome);
        var o = new DisplayObservation(false, Window(Monitor), IsBorderlessCompatible: true, OverlayValidationPassed: false);

        Assert.Equal(DisplayMode.YieldedFullscreen, FullscreenClassifier.Decide(o, policy));
    }

    [Fact]
    public void Allowlist_match_ignores_path_case_but_requires_same_signer()
    {
        var policy = FullscreenPolicy.YieldByDefault.Allow(Chrome);
        var sameAppDifferentCase = new AppIdentity(@"c:\program files\google\chrome\application\CHROME.EXE", "CN=Google LLC");
        var forged = new AppIdentity(Chrome.ExecutablePath, "CN=Someone Else");

        Assert.True(policy.IsAllowlisted(sameAppDifferentCase));
        Assert.False(policy.IsAllowlisted(forged));
    }

    [Fact]
    public void Unsigned_app_is_never_allowlisted()
    {
        var unsigned = new AppIdentity(@"C:\tools\player.exe", SignerSubject: null);
        var policy = FullscreenPolicy.YieldByDefault.Allow(unsigned);

        Assert.False(policy.IsAllowlisted(unsigned));
    }
}

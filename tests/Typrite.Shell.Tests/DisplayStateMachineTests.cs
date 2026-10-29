using Typrite.Shell.Fullscreen;

namespace Typrite.Shell.Tests;

public class DisplayStateMachineTests
{
    private static DisplayStateMachine Normal()
    {
        var sm = new DisplayStateMachine();
        sm.Apply(DisplayEvent.BarVerified);
        return sm;
    }

    [Fact]
    public void Starts_in_recovering_until_bar_is_verified()
    {
        var sm = new DisplayStateMachine();

        Assert.Equal(DisplayMode.Recovering, sm.Mode);
        Assert.True(sm.Apply(DisplayEvent.BarVerified));
        Assert.Equal(DisplayMode.NormalReserved, sm.Mode);
    }

    [Fact]
    public void Candidate_moves_to_detected_then_classification_decides()
    {
        var sm = Normal();

        Assert.True(sm.Apply(DisplayEvent.FullscreenCandidateAppeared));
        Assert.Equal(DisplayMode.FullscreenDetected, sm.Mode);

        Assert.True(sm.Apply(DisplayEvent.Classified(DisplayMode.YieldedFullscreen)));
        Assert.Equal(DisplayMode.YieldedFullscreen, sm.Mode);
    }

    [Fact]
    public void False_positive_classification_returns_to_normal()
    {
        var sm = Normal();
        sm.Apply(DisplayEvent.FullscreenCandidateAppeared);

        sm.Apply(DisplayEvent.Classified(DisplayMode.NormalReserved));

        Assert.Equal(DisplayMode.NormalReserved, sm.Mode);
    }

    [Theory]
    [InlineData(DisplayMode.Recovering)]
    [InlineData(DisplayMode.SafeMode)]
    [InlineData(DisplayMode.FullscreenDetected)]
    public void Classification_can_only_produce_a_classified_mode(DisplayMode illegal)
    {
        var sm = Normal();
        sm.Apply(DisplayEvent.FullscreenCandidateAppeared);

        Assert.Throws<ArgumentOutOfRangeException>(() => sm.Apply(DisplayEvent.Classified(illegal)));
    }

    [Fact]
    public void Overlay_falls_back_to_yield_when_occluded()
    {
        var sm = Normal();
        sm.Apply(DisplayEvent.FullscreenCandidateAppeared);
        sm.Apply(DisplayEvent.Classified(DisplayMode.CompatibleOverlay));

        Assert.True(sm.Apply(DisplayEvent.OverlayOccluded));
        Assert.Equal(DisplayMode.YieldedFullscreen, sm.Mode);
    }

    [Theory]
    [InlineData(DisplayMode.CompatibleOverlay)]
    [InlineData(DisplayMode.YieldedFullscreen)]
    public void Fullscreen_ending_restores_normal(DisplayMode from)
    {
        var sm = Normal();
        sm.Apply(DisplayEvent.FullscreenCandidateAppeared);
        sm.Apply(DisplayEvent.Classified(from));

        Assert.True(sm.Apply(DisplayEvent.FullscreenEnded));
        Assert.Equal(DisplayMode.NormalReserved, sm.Mode);
    }

    [Theory]
    [InlineData(DisplayMode.CompatibleOverlay)]
    [InlineData(DisplayMode.YieldedFullscreen)]
    public void Secure_desktop_moves_fullscreen_modes_to_protected(DisplayMode from)
    {
        var sm = Normal();
        sm.Apply(DisplayEvent.FullscreenCandidateAppeared);
        sm.Apply(DisplayEvent.Classified(from));

        Assert.True(sm.Apply(DisplayEvent.SecureDesktopAppeared));
        Assert.Equal(DisplayMode.ProtectedSurface, sm.Mode);
    }

    [Fact]
    public void Lock_screen_from_normal_desktop_moves_to_protected()
    {
        var sm = Normal();

        Assert.True(sm.Apply(DisplayEvent.SecureDesktopAppeared));
        Assert.Equal(DisplayMode.ProtectedSurface, sm.Mode);
    }

    [Fact]
    public void Protected_surface_recovers_only_through_recovering()
    {
        var sm = Normal();
        sm.Apply(DisplayEvent.FullscreenCandidateAppeared);
        sm.Apply(DisplayEvent.Classified(DisplayMode.ProtectedSurface));

        Assert.False(sm.Apply(DisplayEvent.FullscreenEnded));
        Assert.Equal(DisplayMode.ProtectedSurface, sm.Mode);

        Assert.True(sm.Apply(DisplayEvent.DesktopRestored));
        Assert.Equal(DisplayMode.Recovering, sm.Mode);
    }

    [Theory]
    [InlineData(DisplayMode.NormalReserved)]
    [InlineData(DisplayMode.FullscreenDetected)]
    [InlineData(DisplayMode.CompatibleOverlay)]
    [InlineData(DisplayMode.YieldedFullscreen)]
    [InlineData(DisplayMode.ProtectedSurface)]
    public void Topology_change_enters_recovering_outside_safe_mode(DisplayMode from)
    {
        var sm = DriveTo(from);

        Assert.True(sm.Apply(DisplayEvent.TopologyChanged));
        Assert.Equal(DisplayMode.Recovering, sm.Mode);
    }

    [Theory]
    [InlineData(DisplayMode.NormalReserved)]
    [InlineData(DisplayMode.YieldedFullscreen)]
    [InlineData(DisplayMode.Recovering)]
    public void Safe_mode_is_reachable_from_any_state_and_needs_authorized_recovery(DisplayMode from)
    {
        var sm = DriveTo(from);

        Assert.True(sm.Apply(DisplayEvent.SafeModeRequested));
        Assert.Equal(DisplayMode.SafeMode, sm.Mode);

        Assert.False(sm.Apply(DisplayEvent.BarVerified));
        Assert.False(sm.Apply(DisplayEvent.FullscreenEnded));
        Assert.False(sm.Apply(DisplayEvent.TopologyChanged));
        Assert.Equal(DisplayMode.SafeMode, sm.Mode);

        Assert.True(sm.Apply(DisplayEvent.RecoveryAuthorized));
        Assert.Equal(DisplayMode.Recovering, sm.Mode);
    }

    [Fact]
    public void Ignored_events_do_not_change_mode_and_report_false()
    {
        var sm = Normal();

        Assert.False(sm.Apply(DisplayEvent.FullscreenEnded));
        Assert.False(sm.Apply(DisplayEvent.OverlayOccluded));
        Assert.Equal(DisplayMode.NormalReserved, sm.Mode);
    }

    [Fact]
    public void Transitions_are_observable()
    {
        var sm = new DisplayStateMachine();
        var seen = new List<(DisplayMode From, DisplayMode To)>();
        sm.Transitioned += (_, e) => seen.Add((e.From, e.To));

        sm.Apply(DisplayEvent.BarVerified);
        sm.Apply(DisplayEvent.FullscreenEnded); // ignored

        Assert.Equal([(DisplayMode.Recovering, DisplayMode.NormalReserved)], seen);
    }

    private static DisplayStateMachine DriveTo(DisplayMode target)
    {
        var sm = new DisplayStateMachine();
        switch (target)
        {
            case DisplayMode.Recovering:
                break;
            case DisplayMode.NormalReserved:
                sm.Apply(DisplayEvent.BarVerified);
                break;
            case DisplayMode.FullscreenDetected:
                sm.Apply(DisplayEvent.BarVerified);
                sm.Apply(DisplayEvent.FullscreenCandidateAppeared);
                break;
            case DisplayMode.SafeMode:
                sm.Apply(DisplayEvent.SafeModeRequested);
                break;
            default:
                sm.Apply(DisplayEvent.BarVerified);
                sm.Apply(DisplayEvent.FullscreenCandidateAppeared);
                sm.Apply(DisplayEvent.Classified(target));
                break;
        }

        Assert.Equal(target, sm.Mode);
        return sm;
    }
}

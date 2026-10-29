using Microsoft.Extensions.Time.Testing;
using Typrite.Contracts.Delivery;
using Typrite.Player.Playback;
using Typrite.Shell.Fullscreen;

namespace Typrite.Player.Tests;

public class AdVisibilityTrackerTests
{
    private static readonly Guid Decision = Guid.NewGuid();
    private static readonly Guid Creative = Guid.NewGuid();

    private static (AdVisibilityTracker Tracker, FakeTimeProvider Time, List<DeliveryEvent> Events) Make(int? durationMs = 10_000)
    {
        var time = new FakeTimeProvider();
        var events = new List<DeliveryEvent>();
        var ctx = new PlaybackContext(Guid.NewGuid(), "session-1", "display-a", Decision, Creative, ConfigurationVersion: 7, DurationMs: durationMs);
        var tracker = new AdVisibilityTracker(ctx, time, events.Add);
        return (tracker, time, events);
    }

    private static DeliveryEventType[] Types(List<DeliveryEvent> e) => [.. e.Select(x => x.Type)];

    [Fact]
    public void Render_start_alone_does_not_claim_visibility()
    {
        var (t, _, events) = Make();

        t.RenderStarted();

        Assert.Equal([DeliveryEventType.RenderStarted], Types(events));
    }

    [Fact]
    public void Visibility_is_claimed_only_when_rendering_in_a_visible_mode()
    {
        var (t, _, events) = Make();

        t.DisplayModeChanged(DisplayMode.NormalReserved);
        Assert.Equal([], Types(events));

        t.RenderStarted();
        Assert.Equal([DeliveryEventType.RenderStarted, DeliveryEventType.VisibilityObserved], Types(events));
    }

    [Fact]
    public void Yielding_ends_visibility_with_measured_visible_time()
    {
        var (t, time, events) = Make();
        t.DisplayModeChanged(DisplayMode.NormalReserved);
        t.RenderStarted();
        time.Advance(TimeSpan.FromSeconds(3));

        t.DisplayModeChanged(DisplayMode.YieldedFullscreen);

        var lost = Assert.Single(events, e => e.Type == DeliveryEventType.VisibilityLost);
        Assert.Equal(3000, lost.ReportedDurationMs);
    }

    [Fact]
    public void Visible_time_accumulates_only_while_visible()
    {
        var (t, time, events) = Make();
        t.DisplayModeChanged(DisplayMode.NormalReserved);
        t.RenderStarted();
        time.Advance(TimeSpan.FromSeconds(2));
        t.DisplayModeChanged(DisplayMode.YieldedFullscreen);
        time.Advance(TimeSpan.FromSeconds(5));
        t.DisplayModeChanged(DisplayMode.NormalReserved);
        time.Advance(TimeSpan.FromSeconds(1));

        t.PlaybackCompleted();

        var completed = Assert.Single(events, e => e.Type == DeliveryEventType.PlaybackCompleted);
        Assert.Equal(3000, completed.ReportedDurationMs);
        Assert.Equal(3000, t.VisibleMilliseconds);
    }

    [Fact]
    public void Quartiles_fire_once_each_in_order()
    {
        var (t, _, events) = Make(durationMs: 10_000);
        t.DisplayModeChanged(DisplayMode.NormalReserved);
        t.RenderStarted();

        t.PlaybackProgress(2_600);
        t.PlaybackProgress(2_700);
        t.PlaybackProgress(7_600);
        t.PlaybackProgress(9_999);

        Assert.Equal(
            [DeliveryEventType.RenderStarted, DeliveryEventType.VisibilityObserved,
             DeliveryEventType.PlaybackFirstQuartile, DeliveryEventType.PlaybackMidpoint, DeliveryEventType.PlaybackThirdQuartile],
            Types(events));
    }

    [Fact]
    public void Static_image_has_no_quartiles()
    {
        var (t, _, events) = Make(durationMs: null);
        t.DisplayModeChanged(DisplayMode.NormalReserved);
        t.RenderStarted();

        t.PlaybackProgress(5_000);

        Assert.DoesNotContain(events, e => e.Type == DeliveryEventType.PlaybackMidpoint);
    }

    [Theory]
    [InlineData(DisplayMode.ProtectedSurface)]
    [InlineData(DisplayMode.SafeMode)]
    [InlineData(DisplayMode.Recovering)]
    [InlineData(DisplayMode.FullscreenDetected)]
    public void Non_visible_modes_never_claim_visibility(DisplayMode mode)
    {
        var (t, _, events) = Make();
        t.DisplayModeChanged(mode);

        t.RenderStarted();

        Assert.DoesNotContain(events, e => e.Type == DeliveryEventType.VisibilityObserved);
    }

    [Fact]
    public void Compatible_overlay_counts_as_visible()
    {
        var (t, _, events) = Make();
        t.DisplayModeChanged(DisplayMode.CompatibleOverlay);

        t.RenderStarted();

        Assert.Contains(events, e => e.Type == DeliveryEventType.VisibilityObserved);
    }

    [Fact]
    public void Events_carry_context_and_unique_ids()
    {
        var (t, _, events) = Make();
        t.DisplayModeChanged(DisplayMode.NormalReserved);
        t.RenderStarted();

        Assert.All(events, e =>
        {
            Assert.Equal(Decision, e.DecisionId);
            Assert.Equal(Creative, e.CreativeId);
            Assert.Equal("display-a", e.DisplayKey);
            Assert.Equal(7, e.ConfigurationVersion);
        });
        Assert.Equal(events.Count, events.Select(e => e.ClientEventId).Distinct().Count());
    }

    [Fact]
    public void Failure_emits_render_failed_and_stops_tracking()
    {
        var (t, time, events) = Make();
        t.DisplayModeChanged(DisplayMode.NormalReserved);
        t.RenderStarted();

        t.RenderFailed("decoder");
        time.Advance(TimeSpan.FromSeconds(5));
        t.PlaybackCompleted();

        Assert.Equal([DeliveryEventType.RenderStarted, DeliveryEventType.VisibilityObserved, DeliveryEventType.RenderFailed], Types(events));
    }
}

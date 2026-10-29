using Typrite.Contracts.Delivery;
using Typrite.Shell.Fullscreen;

namespace Typrite.Player.Playback;

/// <summary>
/// Turns player state and display mode into technical delivery events. Visibility is claimed only while the
/// creative is rendering and the display is in a mode where the bar is actually on screen; the clock stops
/// the moment that is no longer true. Nothing here is billable.
/// </summary>
public sealed class AdVisibilityTracker
{
    private readonly PlaybackContext _ctx;
    private readonly TimeProvider _time;
    private readonly Action<DeliveryEvent> _emit;
    private bool _rendering;
    private bool _finished;
    private bool _modeVisible;
    private DateTimeOffset? _visibleSince;
    private long _visibleMs;
    private int _quartilesFired;

    public AdVisibilityTracker(PlaybackContext context, TimeProvider time, Action<DeliveryEvent> emit)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(emit);
        _ctx = context;
        _time = time;
        _emit = emit;
    }

    public long VisibleMilliseconds =>
        _visibleMs + (_visibleSince is { } since ? (long)(_time.GetUtcNow() - since).TotalMilliseconds : 0);

    public void DisplayModeChanged(DisplayMode mode)
    {
        var visible = mode is DisplayMode.NormalReserved or DisplayMode.CompatibleOverlay;
        if (visible == _modeVisible)
        {
            return;
        }

        _modeVisible = visible;
        if (!_rendering || _finished)
        {
            return;
        }

        if (visible)
        {
            StartVisible();
        }
        else
        {
            StopVisible();
            Emit(DeliveryEventType.VisibilityLost, _visibleMs);
        }
    }

    public void RenderStarted()
    {
        if (_rendering || _finished)
        {
            return;
        }

        _rendering = true;
        Emit(DeliveryEventType.RenderStarted);
        if (_modeVisible)
        {
            StartVisible();
        }
    }

    public void PlaybackProgress(int positionMs)
    {
        if (!_rendering || _finished || _ctx.DurationMs is not > 0)
        {
            return;
        }

        var fraction = (double)positionMs / _ctx.DurationMs.Value;
        ReadOnlySpan<(double At, DeliveryEventType Type)> quartiles =
        [
            (0.25, DeliveryEventType.PlaybackFirstQuartile),
            (0.50, DeliveryEventType.PlaybackMidpoint),
            (0.75, DeliveryEventType.PlaybackThirdQuartile),
        ];

        while (_quartilesFired < quartiles.Length && fraction >= quartiles[_quartilesFired].At)
        {
            Emit(quartiles[_quartilesFired].Type);
            _quartilesFired++;
        }
    }

    public void PlaybackCompleted()
    {
        if (!_rendering || _finished)
        {
            return;
        }

        StopVisible();
        _finished = true;
        Emit(DeliveryEventType.PlaybackCompleted, _visibleMs);
    }

    public void RenderFailed(string reason)
    {
        if (_finished)
        {
            return;
        }

        StopVisible();
        _finished = true;
        Emit(DeliveryEventType.RenderFailed);
    }

    private void StartVisible()
    {
        if (_visibleSince is not null)
        {
            return;
        }

        _visibleSince = _time.GetUtcNow();
        Emit(DeliveryEventType.VisibilityObserved);
    }

    private void StopVisible()
    {
        if (_visibleSince is { } since)
        {
            _visibleMs += (long)(_time.GetUtcNow() - since).TotalMilliseconds;
            _visibleSince = null;
        }
    }

    private void Emit(DeliveryEventType type, long? durationMs = null) =>
        _emit(new DeliveryEvent(
            ClientEventId: Guid.NewGuid(),
            DeviceId: _ctx.DeviceId,
            SessionId: _ctx.SessionId,
            DisplayKey: _ctx.DisplayKey,
            DecisionId: _ctx.DecisionId,
            CreativeId: _ctx.CreativeId,
            Type: type,
            DeviceTimeUtc: _time.GetUtcNow(),
            ReportedDurationMs: durationMs,
            ConfigurationVersion: _ctx.ConfigurationVersion));
}

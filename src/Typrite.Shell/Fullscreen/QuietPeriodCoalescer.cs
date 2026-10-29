namespace Typrite.Shell.Fullscreen;

/// <summary>
/// Collapses bursts of shell events into one callback: fires after <c>quiet</c> with no new signals,
/// or at <c>maxDelay</c> after the first signal of a burst if the burst never goes quiet (window drags).
/// Callback runs on the timer thread; callers marshal to the UI thread themselves.
/// </summary>
public sealed class QuietPeriodCoalescer : IDisposable
{
    private readonly TimeProvider _time;
    private readonly TimeSpan _quiet;
    private readonly TimeSpan _maxDelay;
    private readonly Action _fire;
    private readonly ITimer _timer;
    private readonly Lock _gate = new();
    private DateTimeOffset? _burstStart;
    private bool _disposed;

    public QuietPeriodCoalescer(TimeProvider time, TimeSpan quiet, TimeSpan maxDelay, Action fire)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(fire);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(quiet, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDelay, quiet);

        _time = time;
        _quiet = quiet;
        _maxDelay = maxDelay;
        _fire = fire;
        _timer = time.CreateTimer(_ => OnTimer(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Signal()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var now = _time.GetUtcNow();
            _burstStart ??= now;
            var untilMax = _burstStart.Value + _maxDelay - now;
            var delay = untilMax < _quiet ? untilMax : _quiet;
            _timer.Change(delay < TimeSpan.Zero ? TimeSpan.Zero : delay, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _timer.Dispose();
        }
    }

    private void OnTimer()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _burstStart = null;
        }

        _fire();
    }
}

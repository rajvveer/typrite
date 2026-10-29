using Microsoft.Extensions.Time.Testing;
using Typrite.Shell.Fullscreen;

namespace Typrite.Shell.Tests;

public class QuietPeriodCoalescerTests
{
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan Max = TimeSpan.FromSeconds(1);

    [Fact]
    public void Fires_once_after_quiet_period()
    {
        var time = new FakeTimeProvider();
        var fired = 0;
        using var c = new QuietPeriodCoalescer(time, Quiet, Max, () => fired++);

        c.Signal();
        time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(0, fired);

        time.Advance(TimeSpan.FromMilliseconds(60));
        Assert.Equal(1, fired);

        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Repeated_signals_inside_quiet_period_restart_it()
    {
        var time = new FakeTimeProvider();
        var fired = 0;
        using var c = new QuietPeriodCoalescer(time, Quiet, Max, () => fired++);

        c.Signal();
        time.Advance(TimeSpan.FromMilliseconds(100));
        c.Signal();
        time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(0, fired);

        time.Advance(TimeSpan.FromMilliseconds(60));
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Continuous_signals_still_fire_at_max_delay()
    {
        var time = new FakeTimeProvider();
        var fired = 0;
        using var c = new QuietPeriodCoalescer(time, Quiet, Max, () => fired++);

        for (var elapsed = 0; elapsed < 1200; elapsed += 50)
        {
            c.Signal();
            time.Advance(TimeSpan.FromMilliseconds(50));
        }

        Assert.Equal(1, fired);
    }

    [Fact]
    public void Disposed_coalescer_never_fires()
    {
        var time = new FakeTimeProvider();
        var fired = 0;
        var c = new QuietPeriodCoalescer(time, Quiet, Max, () => fired++);

        c.Signal();
        c.Dispose();
        time.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(0, fired);
    }
}

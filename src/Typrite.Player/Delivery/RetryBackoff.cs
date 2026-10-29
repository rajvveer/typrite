namespace Typrite.Player.Delivery;

public static class RetryBackoff
{
    /// <summary>Exponential backoff with symmetric jitter: base·2^attempt, capped, then ±jitter fraction.</summary>
    public static TimeSpan Delay(int attempt, TimeSpan initial, TimeSpan max, double jitter)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(attempt);
        ArgumentOutOfRangeException.ThrowIfNegative(jitter);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(jitter, 1.0);

        var ms = Math.Min(initial.TotalMilliseconds * Math.Pow(2, Math.Min(attempt, 30)), max.TotalMilliseconds);
        var factor = 1 + ((Random.Shared.NextDouble() * 2) - 1) * jitter;
        return TimeSpan.FromMilliseconds(ms * factor);
    }
}

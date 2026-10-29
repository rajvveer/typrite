namespace Typrite.Agent.Identity;

public sealed record IssuedToken(string Token, DateTimeOffset ExpiresAtUtc);

/// <summary>Caches the short-lived device token and refreshes it inside the margin; concurrent callers share one fetch.</summary>
public sealed class TokenCache(TimeProvider time, TimeSpan refreshMargin, Func<CancellationToken, Task<IssuedToken>> fetch) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IssuedToken? _current;

    public void Dispose() => _gate.Dispose();

    public async Task<string> GetAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        if (_current is { } fresh && fresh.ExpiresAtUtc - now > refreshMargin)
        {
            return fresh.Token;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            now = time.GetUtcNow();
            if (_current is { } raced && raced.ExpiresAtUtc - now > refreshMargin)
            {
                return raced.Token;
            }

            try
            {
                _current = await fetch(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                if (_current is { } stale && stale.ExpiresAtUtc > now)
                {
                    return stale.Token; // still valid; try again on the next call
                }

                throw;
            }

            return _current.Token;
        }
        finally
        {
            _gate.Release();
        }
    }
}

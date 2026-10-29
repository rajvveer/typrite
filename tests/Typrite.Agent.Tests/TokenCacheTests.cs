using Microsoft.Extensions.Time.Testing;
using Typrite.Agent.Identity;

namespace Typrite.Agent.Tests;

public class TokenCacheTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Fetches_once_and_reuses_until_near_expiry()
    {
        var time = new FakeTimeProvider(Now);
        var fetches = 0;
        var cache = new TokenCache(time, refreshMargin: TimeSpan.FromMinutes(5), async ct =>
        {
            fetches++;
            await Task.Yield();
            return new IssuedToken($"t{fetches}", time.GetUtcNow().AddMinutes(30));
        });

        var a = await cache.GetAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(20));
        var b = await cache.GetAsync(CancellationToken.None);

        Assert.Equal("t1", a);
        Assert.Equal("t1", b);
        Assert.Equal(1, fetches);
    }

    [Fact]
    public async Task Refreshes_inside_the_margin_before_expiry()
    {
        var time = new FakeTimeProvider(Now);
        var fetches = 0;
        var cache = new TokenCache(time, TimeSpan.FromMinutes(5), ct =>
        {
            fetches++;
            return Task.FromResult(new IssuedToken($"t{fetches}", time.GetUtcNow().AddMinutes(30)));
        });

        await cache.GetAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(26));
        var refreshed = await cache.GetAsync(CancellationToken.None);

        Assert.Equal("t2", refreshed);
    }

    [Fact]
    public async Task Fetch_failure_keeps_a_still_valid_token()
    {
        var time = new FakeTimeProvider(Now);
        var fail = false;
        var cache = new TokenCache(time, TimeSpan.FromMinutes(5), ct =>
            fail ? throw new HttpRequestException("offline") : Task.FromResult(new IssuedToken("t1", time.GetUtcNow().AddMinutes(30))));

        await cache.GetAsync(CancellationToken.None);
        fail = true;
        time.Advance(TimeSpan.FromMinutes(27)); // inside margin, still valid

        Assert.Equal("t1", await cache.GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Fetch_failure_with_an_expired_token_throws()
    {
        var time = new FakeTimeProvider(Now);
        var fail = false;
        var cache = new TokenCache(time, TimeSpan.FromMinutes(5), ct =>
            fail ? throw new HttpRequestException("offline") : Task.FromResult(new IssuedToken("t1", time.GetUtcNow().AddMinutes(30))));

        await cache.GetAsync(CancellationToken.None);
        fail = true;
        time.Advance(TimeSpan.FromMinutes(31));

        await Assert.ThrowsAsync<HttpRequestException>(() => cache.GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Concurrent_callers_share_one_fetch()
    {
        var time = new FakeTimeProvider(Now);
        var fetches = 0;
        var gate = new TaskCompletionSource();
        var cache = new TokenCache(time, TimeSpan.FromMinutes(5), async ct =>
        {
            Interlocked.Increment(ref fetches);
            await gate.Task;
            return new IssuedToken("t", time.GetUtcNow().AddMinutes(30));
        });

        var first = cache.GetAsync(CancellationToken.None);
        var second = cache.GetAsync(CancellationToken.None);
        gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, fetches);
    }
}

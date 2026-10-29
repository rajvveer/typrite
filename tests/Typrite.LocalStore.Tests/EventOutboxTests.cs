using Microsoft.Extensions.Time.Testing;
using Typrite.Contracts.Delivery;
using Typrite.LocalStore;

namespace Typrite.LocalStore.Tests;

public sealed class EventOutboxTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"typrite-q-{Guid.NewGuid():N}.db");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero));

    private EventOutbox Open(int maxCount = 1000) => new(_path, _time, maxCount, TimeSpan.FromDays(7));

    private DeliveryEvent Event(DeliveryEventType type = DeliveryEventType.RenderStarted) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "s", "d", null, null, type, _time.GetUtcNow(), null, 1);

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [Fact]
    public void Enqueued_events_survive_reopen()
    {
        var e = Event();
        using (var q = Open())
        {
            q.Enqueue(e);
        }

        using var reopened = Open();
        var batch = reopened.PeekBatch(10);

        Assert.Equal(e.ClientEventId, Assert.Single(batch).ClientEventId);
    }

    [Fact]
    public void Acknowledged_events_are_removed_and_others_stay()
    {
        using var q = Open();
        var a = Event();
        var b = Event();
        q.Enqueue(a);
        q.Enqueue(b);

        q.Acknowledge([a.ClientEventId]);

        Assert.Equal(b.ClientEventId, Assert.Single(q.PeekBatch(10)).ClientEventId);
        Assert.Equal(1, q.Count);
    }

    [Fact]
    public void Duplicate_client_event_id_is_ignored()
    {
        using var q = Open();
        var e = Event();

        q.Enqueue(e);
        q.Enqueue(e);

        Assert.Equal(1, q.Count);
    }

    [Fact]
    public void PeekBatch_returns_oldest_first_and_respects_size()
    {
        using var q = Open();
        var first = Event();
        q.Enqueue(first);
        _time.Advance(TimeSpan.FromSeconds(1));
        q.Enqueue(Event());
        _time.Advance(TimeSpan.FromSeconds(1));
        q.Enqueue(Event());

        var batch = q.PeekBatch(2);

        Assert.Equal(2, batch.Count);
        Assert.Equal(first.ClientEventId, batch[0].ClientEventId);
    }

    [Fact]
    public void Queue_is_capped_and_drops_oldest_with_a_counter()
    {
        using var q = Open(maxCount: 2);
        var oldest = Event();
        q.Enqueue(oldest);
        _time.Advance(TimeSpan.FromSeconds(1));
        q.Enqueue(Event());
        _time.Advance(TimeSpan.FromSeconds(1));
        q.Enqueue(Event());

        Assert.Equal(2, q.Count);
        Assert.Equal(1, q.DroppedCount);
        Assert.DoesNotContain(q.PeekBatch(10), e => e.ClientEventId == oldest.ClientEventId);
    }

    [Fact]
    public void Expired_events_are_pruned()
    {
        using var q = Open();
        q.Enqueue(Event());
        _time.Advance(TimeSpan.FromDays(8));
        q.Enqueue(Event());

        var pruned = q.PruneExpired();

        Assert.Equal(1, pruned);
        Assert.Equal(1, q.Count);
        Assert.Equal(1, q.DroppedCount);
    }

    [Fact]
    public void Round_trips_every_field()
    {
        using var q = Open();
        var e = new DeliveryEvent(Guid.NewGuid(), Guid.NewGuid(), "sess", "disp", Guid.NewGuid(), Guid.NewGuid(),
            DeliveryEventType.VisibilityLost, _time.GetUtcNow(), 1234, 42);
        q.Enqueue(e);

        Assert.Equal(e, Assert.Single(q.PeekBatch(1)));
    }
}

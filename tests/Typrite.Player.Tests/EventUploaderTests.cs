using Typrite.Contracts.Delivery;
using Typrite.Player.Delivery;

namespace Typrite.Player.Tests;

public class EventUploaderTests
{
    private sealed class Outbox : IEventOutbox
    {
        public List<DeliveryEvent> Events { get; } = [];
        public List<Guid> Acked { get; } = [];
        public int Count => Events.Count;
        public IReadOnlyList<DeliveryEvent> PeekBatch(int max) => Events.Take(max).ToList();
        public void Acknowledge(IEnumerable<Guid> ids)
        {
            var set = ids.ToHashSet();
            Acked.AddRange(set);
            Events.RemoveAll(e => set.Contains(e.ClientEventId));
        }
    }

    private sealed class Api : IDeliveryApi
    {
        public List<EventBatchRequest> Posted { get; } = [];
        public Func<EventBatchRequest, EventBatchResponse> Respond { get; set; } = r => new([.. r.Events.Select(e => e.ClientEventId)], []);
        public bool Fail { get; set; }

        public Task<EventBatchResponse> PostEventsAsync(EventBatchRequest batch, CancellationToken ct)
        {
            if (Fail)
            {
                throw new HttpRequestException("offline");
            }

            Posted.Add(batch);
            return Task.FromResult(Respond(batch));
        }

        public Task<AdDecisionResponse?> RequestDecisionAsync(string displayKey, AdDecisionRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<byte[]> FetchCreativeAsync(Uri contentUri, long maxBytes, CancellationToken ct) => throw new NotSupportedException();
    }

    private static DeliveryEvent Event() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "s", "d", null, null, DeliveryEventType.RenderStarted, DateTimeOffset.UtcNow, null, 1);

    [Fact]
    public async Task Empty_outbox_makes_no_request()
    {
        var api = new Api();
        var uploader = new EventUploader(new Outbox(), api, batchSize: 10);

        var result = await uploader.DrainOnceAsync(CancellationToken.None);

        Assert.Equal(UploadOutcome.Nothing, result.Outcome);
        Assert.Empty(api.Posted);
    }

    [Fact]
    public async Task Accepted_events_are_acknowledged()
    {
        var outbox = new Outbox();
        outbox.Events.AddRange([Event(), Event()]);
        var uploader = new EventUploader(outbox, new Api(), batchSize: 10);

        var result = await uploader.DrainOnceAsync(CancellationToken.None);

        Assert.Equal(UploadOutcome.Sent, result.Outcome);
        Assert.Equal(2, result.Accepted);
        Assert.Empty(outbox.Events);
    }

    [Fact]
    public async Task Rejected_events_are_dropped_not_retried_and_counted()
    {
        var outbox = new Outbox();
        var bad = Event();
        outbox.Events.AddRange([Event(), bad]);
        var api = new Api { Respond = r => new([.. r.Events.Where(e => e != bad).Select(e => e.ClientEventId)], [bad.ClientEventId]) };
        var uploader = new EventUploader(outbox, api, batchSize: 10);

        var result = await uploader.DrainOnceAsync(CancellationToken.None);

        Assert.Equal(1, result.Accepted);
        Assert.Equal(1, result.Rejected);
        Assert.Empty(outbox.Events);
    }

    [Fact]
    public async Task Transport_failure_leaves_outbox_intact()
    {
        var outbox = new Outbox();
        outbox.Events.Add(Event());
        var uploader = new EventUploader(outbox, new Api { Fail = true }, batchSize: 10);

        var result = await uploader.DrainOnceAsync(CancellationToken.None);

        Assert.Equal(UploadOutcome.Failed, result.Outcome);
        Assert.Single(outbox.Events);
        Assert.Empty(outbox.Acked);
    }

    [Fact]
    public async Task Unacknowledged_events_are_kept_for_retry()
    {
        var outbox = new Outbox();
        var silent = Event();
        outbox.Events.AddRange([Event(), silent]);
        var api = new Api { Respond = r => new([.. r.Events.Where(e => e != silent).Select(e => e.ClientEventId)], []) };
        var uploader = new EventUploader(outbox, api, batchSize: 10);

        await uploader.DrainOnceAsync(CancellationToken.None);

        Assert.Equal(silent, Assert.Single(outbox.Events));
    }

    [Fact]
    public async Task DrainAll_sends_batches_until_empty()
    {
        var outbox = new Outbox();
        outbox.Events.AddRange(Enumerable.Range(0, 25).Select(_ => Event()));
        var api = new Api();
        var uploader = new EventUploader(outbox, api, batchSize: 10);

        var sent = await uploader.DrainAllAsync(CancellationToken.None);

        Assert.Equal(25, sent);
        Assert.Equal(3, api.Posted.Count);
        Assert.Empty(outbox.Events);
    }

    [Fact]
    public async Task DrainAll_stops_at_first_failure()
    {
        var outbox = new Outbox();
        outbox.Events.AddRange(Enumerable.Range(0, 25).Select(_ => Event()));
        var api = new Api();
        var uploader = new EventUploader(outbox, api, batchSize: 10);
        api.Respond = r => { api.Fail = true; return new([.. r.Events.Select(e => e.ClientEventId)], []); };

        var sent = await uploader.DrainAllAsync(CancellationToken.None);

        Assert.Equal(10, sent);
        Assert.Equal(15, outbox.Count);
    }

    [Theory]
    [InlineData(0, 2_000)]
    [InlineData(1, 4_000)]
    [InlineData(3, 16_000)]
    [InlineData(10, 300_000)]
    public void Backoff_doubles_and_caps(int attempt, int expectedBaseMs)
    {
        var delay = RetryBackoff.Delay(attempt, TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(5), jitter: 0.0);

        Assert.Equal(expectedBaseMs, delay.TotalMilliseconds);
    }

    [Fact]
    public void Backoff_jitter_stays_within_bounds()
    {
        for (var i = 0; i < 200; i++)
        {
            var delay = RetryBackoff.Delay(2, TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(5), jitter: 0.2);
            Assert.InRange(delay.TotalMilliseconds, 6_400, 9_600);
        }
    }
}

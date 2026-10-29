using System.Security.Cryptography;
using Typrite.Contracts.Delivery;
using Typrite.Player.Creatives;
using Typrite.Player.Delivery;

namespace Typrite.Player.Tests;

public class AdDeliveryPipelineTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9, 9, 9];
    private static readonly byte[] House = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1];

    private sealed class Api : IDeliveryApi
    {
        public AdDecisionResponse? Decision { get; set; }
        public byte[] Bytes { get; set; } = Png;
        public int Fetches { get; private set; }
        public bool FailDecision { get; set; }

        public Task<AdDecisionResponse?> RequestDecisionAsync(string displayKey, AdDecisionRequest request, CancellationToken ct) =>
            FailDecision ? throw new HttpRequestException("offline") : Task.FromResult(Decision);

        public Task<byte[]> FetchCreativeAsync(Uri contentUri, long maxBytes, CancellationToken ct)
        {
            Fetches++;
            return Task.FromResult(Bytes);
        }

        public Task<EventBatchResponse> PostEventsAsync(EventBatchRequest batch, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class MemoryStore : ICreativeStore
    {
        public Dictionary<Guid, byte[]> Files { get; } = [];
        public IReadOnlyList<StoredCreative> List() => [.. Files.Select(f => new StoredCreative(f.Key, f.Value.Length))];
        public Task WriteAsync(Guid id, ReadOnlyMemory<byte> bytes, CancellationToken ct) { Files[id] = bytes.ToArray(); return Task.CompletedTask; }
        public Task<byte[]?> ReadAsync(Guid id, CancellationToken ct) => Task.FromResult(Files.GetValueOrDefault(id));
        public void Delete(Guid id) => Files.Remove(id);
    }

    private static CreativeManifest Manifest(byte[] bytes, string mediaType = "image/png") =>
        new(Guid.NewGuid(), "400x225", mediaType, bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)), null,
            new Uri("https://cdn.example.com/c.png"), null);

    private static AdDecisionResponse Decision(CreativeManifest m) =>
        new(Guid.NewGuid(), Guid.NewGuid(), m, DateTimeOffset.UtcNow.AddMinutes(5));

    private static (AdDeliveryPipeline Pipeline, Api Api, MemoryStore Store, List<DeliveryEvent> Events) Make()
    {
        var api = new Api();
        var store = new MemoryStore();
        var events = new List<DeliveryEvent>();
        var house = new HouseCreative(Manifest(House) with { ContentUri = new Uri("https://local/house") }, House);
        var ctx = new DeviceContext(Guid.NewGuid(), "session", ConfigurationVersion: 3);
        var pipeline = new AdDeliveryPipeline(api, new CreativeCache(store, 1_000_000), CreativePolicy.Default, house, ctx, TimeProvider.System, events.Add);
        return (pipeline, api, store, events);
    }

    private static DeliveryEventType[] Types(List<DeliveryEvent> e) => [.. e.Select(x => x.Type)];

    [Fact]
    public async Task No_fill_falls_back_to_house_creative()
    {
        var (p, _, _, events) = Make();

        var r = await p.NextAsync("display-a", CancellationToken.None);

        Assert.Equal(CreativeSource.House, r.Source);
        Assert.Equal(House, r.Bytes);
        Assert.Equal([DeliveryEventType.AdRequested, DeliveryEventType.NoFill], Types(events));
    }

    [Fact]
    public async Task Decision_failure_falls_back_to_house_without_throwing()
    {
        var (p, api, _, events) = Make();
        api.FailDecision = true;

        var r = await p.NextAsync("display-a", CancellationToken.None);

        Assert.Equal(CreativeSource.House, r.Source);
        Assert.Equal([DeliveryEventType.AdRequested, DeliveryEventType.NoFill], Types(events));
    }

    [Fact]
    public async Task Valid_decision_downloads_verifies_and_caches()
    {
        var (p, api, store, events) = Make();
        var m = Manifest(Png);
        api.Decision = Decision(m);

        var r = await p.NextAsync("display-a", CancellationToken.None);

        Assert.Equal(CreativeSource.Downloaded, r.Source);
        Assert.Equal(Png, r.Bytes);
        Assert.Equal(m.CreativeId, r.Manifest.CreativeId);
        Assert.True(store.Files.ContainsKey(m.CreativeId));
        Assert.Equal([DeliveryEventType.AdRequested, DeliveryEventType.AdReturned, DeliveryEventType.CreativeDownloaded], Types(events));
    }

    [Fact]
    public async Task Cached_creative_is_not_fetched_again()
    {
        var (p, api, _, _) = Make();
        api.Decision = Decision(Manifest(Png));
        await p.NextAsync("display-a", CancellationToken.None);

        var r = await p.NextAsync("display-a", CancellationToken.None);

        Assert.Equal(CreativeSource.Cache, r.Source);
        Assert.Equal(1, api.Fetches);
    }

    [Fact]
    public async Task Invalid_manifest_is_rejected_before_any_fetch()
    {
        var (p, api, _, events) = Make();
        api.Decision = Decision(Manifest(Png, mediaType: "text/html"));

        var r = await p.NextAsync("display-a", CancellationToken.None);

        Assert.Equal(CreativeSource.House, r.Source);
        Assert.Equal(0, api.Fetches);
        Assert.Contains(DeliveryEventType.CreativeRejected, Types(events));
    }

    [Fact]
    public async Task Tampered_bytes_are_rejected_and_not_cached()
    {
        var (p, api, store, events) = Make();
        var m = Manifest(Png);
        api.Decision = Decision(m);
        api.Bytes = [.. Png, 0xFF];

        var r = await p.NextAsync("display-a", CancellationToken.None);

        Assert.Equal(CreativeSource.House, r.Source);
        Assert.False(store.Files.ContainsKey(m.CreativeId));
        Assert.Contains(DeliveryEventType.CreativeRejected, Types(events));
    }

    [Fact]
    public async Task Events_carry_decision_and_creative_ids_when_known()
    {
        var (p, api, _, events) = Make();
        var m = Manifest(Png);
        var d = Decision(m);
        api.Decision = d;

        await p.NextAsync("display-a", CancellationToken.None);

        Assert.Null(events[0].DecisionId);
        Assert.All(events.Skip(1), e =>
        {
            Assert.Equal(d.DecisionId, e.DecisionId);
            Assert.Equal(m.CreativeId, e.CreativeId);
            Assert.Equal("display-a", e.DisplayKey);
            Assert.Equal(3, e.ConfigurationVersion);
        });
    }
}

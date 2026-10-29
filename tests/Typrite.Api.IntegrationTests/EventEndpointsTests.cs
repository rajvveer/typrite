using System.Net.Http.Json;
using Typrite.Contracts;
using Typrite.Contracts.Delivery;

namespace Typrite.Api.IntegrationTests;

public sealed class EventEndpointsTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private static DeliveryEvent Event(Guid device, DeliveryEventType type = DeliveryEventType.RenderStarted) =>
        new(Guid.NewGuid(), device, "sess", "disp", null, null, type, DateTimeOffset.UtcNow, null, 1);

    [Fact]
    public async Task Valid_events_are_accepted_and_mismatched_device_rejected()
    {
        var (deviceId, client) = await api.RegisterDeviceAsync();
        var good = Event(deviceId);
        var forged = Event(Guid.NewGuid());

        var res = await client.PostAsJsonAsync("api/v1/events/batches", new EventBatchRequest([good, forged]), ContractsJson.Options);
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<EventBatchResponse>(ContractsJson.Options);

        Assert.Equal([good.ClientEventId], body!.Accepted);
        Assert.Equal([forged.ClientEventId], body.Rejected);
    }

    [Fact]
    public async Task Resending_a_batch_is_acknowledged_without_duplicating_storage()
    {
        var (deviceId, client) = await api.RegisterDeviceAsync();
        var e = Event(deviceId, DeliveryEventType.VisibilityObserved);
        var batch = new EventBatchRequest([e]);

        var first = await (await client.PostAsJsonAsync("api/v1/events/batches", batch, ContractsJson.Options)).Content.ReadFromJsonAsync<EventBatchResponse>(ContractsJson.Options);
        var second = await (await client.PostAsJsonAsync("api/v1/events/batches", batch, ContractsJson.Options)).Content.ReadFromJsonAsync<EventBatchResponse>(ContractsJson.Options);

        Assert.Equal([e.ClientEventId], first!.Accepted);
        Assert.Equal([e.ClientEventId], second!.Accepted);

        var admin = await api.OperatorAsync("Viewer");
        var count = await admin.GetFromJsonAsync<CountResponse>($"api/v1/admin/events/count?deviceId={deviceId}", ContractsJson.Options);
        Assert.Equal(1, count!.Count);
    }

    [Fact]
    public async Task Oversized_batch_is_rejected()
    {
        var (deviceId, client) = await api.RegisterDeviceAsync();
        var events = Enumerable.Range(0, 1001).Select(_ => Event(deviceId)).ToList();

        var res = await client.PostAsJsonAsync("api/v1/events/batches", new EventBatchRequest(events), ContractsJson.Options);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, res.StatusCode);
    }

    public sealed record CountResponse(long Count);
}

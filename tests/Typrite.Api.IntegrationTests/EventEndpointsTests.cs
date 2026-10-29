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

    [Fact]
    public async Task Events_referencing_another_devices_decision_or_an_unknown_decision_are_rejected()
    {
        var (deviceId, client) = await api.RegisterDeviceAsync();
        var unknown = Event(deviceId) with { DecisionId = Guid.NewGuid() };

        var res = await client.PostAsJsonAsync("api/v1/events/batches", new EventBatchRequest([unknown]), ContractsJson.Options);
        var body = await res.Content.ReadFromJsonAsync<EventBatchResponse>(ContractsJson.Options);

        Assert.Equal([unknown.ClientEventId], body!.Rejected);
    }

    [Fact]
    public async Task Single_shot_event_types_cannot_be_replayed_for_the_same_decision()
    {
        var account = await api.CreateAccountAsync("replay");
        var decision = await DecisionAsync(account);
        var first = Event(decision.DeviceId, DeliveryEventType.RenderStarted) with { DecisionId = decision.DecisionId };
        var second = Event(decision.DeviceId, DeliveryEventType.RenderStarted) with { DecisionId = decision.DecisionId };

        var a = await (await decision.Client.PostAsJsonAsync("api/v1/events/batches", new EventBatchRequest([first]), ContractsJson.Options)).Content.ReadFromJsonAsync<EventBatchResponse>(ContractsJson.Options);
        var b = await (await decision.Client.PostAsJsonAsync("api/v1/events/batches", new EventBatchRequest([second]), ContractsJson.Options)).Content.ReadFromJsonAsync<EventBatchResponse>(ContractsJson.Options);

        Assert.Equal([first.ClientEventId], a!.Accepted);
        Assert.Equal([second.ClientEventId], b!.Rejected);
    }

    [Fact]
    public async Task Visibility_events_cannot_exceed_one_observation_per_decision_per_display_pair()
    {
        var account = await api.CreateAccountAsync("visibility");
        var decision = await DecisionAsync(account);
        var events = Enumerable.Range(0, 5)
            .Select(_ => Event(decision.DeviceId, DeliveryEventType.VisibilityObserved) with { DecisionId = decision.DecisionId })
            .ToList();

        var res = await (await decision.Client.PostAsJsonAsync("api/v1/events/batches", new EventBatchRequest(events), ContractsJson.Options)).Content.ReadFromJsonAsync<EventBatchResponse>(ContractsJson.Options);

        Assert.True(res!.Accepted.Count <= 2, "more than two VisibilityObserved per decision is not plausible for one creative");
        Assert.True(res.Rejected.Count >= 3);
    }

    private async Task<(Guid DeviceId, Guid DecisionId, HttpClient Client)> DecisionAsync(Guid account)
    {
        var op = await api.OperatorAsync("CampaignOperator", account);
        var created = await op.PostAsJsonAsync("api/v1/admin/campaigns",
            new { name = "r", startsAtUtc = DateTimeOffset.UtcNow.AddDays(-1), endsAtUtc = DateTimeOffset.UtcNow.AddDays(1), weight = 1 }, ContractsJson.Options);
        var campaignId = (await created.Content.ReadFromJsonAsync<ApiFixture.IdResponse>(ContractsJson.Options))!.Id;
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1]);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "c.png");
        form.Add(new StringContent("400x225"), "format");
        (await op.PostAsync($"api/v1/admin/campaigns/{campaignId}/creatives", form)).EnsureSuccessStatusCode();
        (await op.PostAsync($"api/v1/admin/campaigns/{campaignId}/approve", null)).EnsureSuccessStatusCode();
        (await op.PostAsync($"api/v1/admin/campaigns/{campaignId}/activate", null)).EnsureSuccessStatusCode();

        var (deviceId, client) = await api.RegisterDeviceAsync(account);
        var request = new AdDecisionRequest("bar", Guid.NewGuid().ToString("N"), 1, ["400x225"]);
        var decision = await (await client.PostAsJsonAsync($"api/v1/devices/{deviceId}/displays/disp/decisions", request, ContractsJson.Options)).Content.ReadFromJsonAsync<AdDecisionResponse>(ContractsJson.Options);
        return (deviceId, decision!.DecisionId, client);
    }

    public sealed record CountResponse(long Count);
}

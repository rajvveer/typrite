using Typrite.Contracts.Delivery;

namespace Typrite.Player.Delivery;

/// <summary>Device-facing delivery endpoints. Implemented over HTTP with a short-lived device token; faked in tests.</summary>
public interface IDeliveryApi
{
    /// <returns>Null on no-fill.</returns>
    Task<AdDecisionResponse?> RequestDecisionAsync(string displayKey, AdDecisionRequest request, CancellationToken ct);

    Task<byte[]> FetchCreativeAsync(Uri contentUri, long maxBytes, CancellationToken ct);

    Task<EventBatchResponse> PostEventsAsync(EventBatchRequest batch, CancellationToken ct);
}

/// <summary>What the uploader needs from the local outbox.</summary>
public interface IEventOutbox
{
    int Count { get; }

    IReadOnlyList<DeliveryEvent> PeekBatch(int max);

    void Acknowledge(IEnumerable<Guid> clientEventIds);
}

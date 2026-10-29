using Typrite.Contracts.Delivery;

namespace Typrite.Player.Delivery;

public enum UploadOutcome
{
    Nothing,
    Sent,
    Failed,
}

public readonly record struct UploadResult(UploadOutcome Outcome, int Accepted, int Rejected);

/// <summary>
/// Drains the outbox in batches. Accepted and rejected ids are both acknowledged: a server-side rejection is
/// permanent (schema, replay, unknown device) and must not poison the queue. Transport failures acknowledge nothing.
/// </summary>
public sealed class EventUploader
{
    private readonly IEventOutbox _outbox;
    private readonly IDeliveryApi _api;
    private readonly int _batchSize;

    public EventUploader(IEventOutbox outbox, IDeliveryApi api, int batchSize)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(api);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(batchSize, 0);
        _outbox = outbox;
        _api = api;
        _batchSize = batchSize;
    }

    public async Task<UploadResult> DrainOnceAsync(CancellationToken ct)
    {
        var batch = _outbox.PeekBatch(_batchSize);
        if (batch.Count == 0)
        {
            return new UploadResult(UploadOutcome.Nothing, 0, 0);
        }

        EventBatchResponse response;
        try
        {
            response = await _api.PostEventsAsync(new EventBatchRequest(batch), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return new UploadResult(UploadOutcome.Failed, 0, 0);
        }

        _outbox.Acknowledge(response.Accepted.Concat(response.Rejected));
        return new UploadResult(UploadOutcome.Sent, response.Accepted.Count, response.Rejected.Count);
    }

    /// <returns>Total accepted events before the outbox emptied or a failure stopped the drain.</returns>
    public async Task<int> DrainAllAsync(CancellationToken ct)
    {
        var total = 0;
        while (true)
        {
            var r = await DrainOnceAsync(ct).ConfigureAwait(false);
            if (r.Outcome != UploadOutcome.Sent)
            {
                return total;
            }

            total += r.Accepted;
        }
    }
}

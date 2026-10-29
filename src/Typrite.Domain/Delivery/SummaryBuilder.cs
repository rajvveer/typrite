using Typrite.Contracts.Delivery;

namespace Typrite.Domain.Delivery;

/// <summary>One raw event joined to its decision's campaign. The worker projects these from the database.</summary>
public readonly record struct RawDeliveryFact(Guid CampaignId, Guid CreativeId, Guid DeviceId, DeliveryEventType Type, long? ReportedDurationMs);

public static class SummaryBuilder
{
    /// <summary>Groups one day's facts into <see cref="DeliverySummary"/> rows. Pure and idempotent: re-running on the same input gives the same rows.</summary>
    public static IReadOnlyList<DeliverySummary> Build(DateOnly day, IEnumerable<RawDeliveryFact> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var groups = new Dictionary<(Guid, Guid, DeliveryEventType), (long Count, HashSet<Guid> Devices, long VisibleMs)>();

        foreach (var f in facts)
        {
            var key = (f.CampaignId, f.CreativeId, f.Type);
            groups.TryGetValue(key, out var g);
            g.Devices ??= [];
            g.Count++;
            g.Devices.Add(f.DeviceId);
            if (f.Type is DeliveryEventType.VisibilityLost or DeliveryEventType.PlaybackCompleted)
            {
                g.VisibleMs += f.ReportedDurationMs ?? 0;
            }

            groups[key] = g;
        }

        return [.. groups.Select(kv => new DeliverySummary
        {
            Day = day,
            CampaignId = kv.Key.Item1,
            CreativeId = kv.Key.Item2,
            Type = kv.Key.Item3,
            Count = kv.Value.Count,
            DistinctDevices = kv.Value.Devices.Count,
            VisibleMilliseconds = kv.Value.VisibleMs,
        })];
    }
}

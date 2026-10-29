using Typrite.Contracts.Delivery;

namespace Typrite.Domain.Delivery;

public static class EventIngestRules
{
    public const int SupportedSchemaVersion = 1;
    public static readonly TimeSpan MaxFutureSkew = TimeSpan.FromHours(1);
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    /// <returns>A reason to reject, or null when the event is acceptable for the authenticated device.</returns>
    public static string? Reject(DeliveryEvent e, Guid authenticatedDeviceId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (e.ClientEventId == Guid.Empty)
        {
            return "client event id missing";
        }

        if (e.DeviceId != authenticatedDeviceId)
        {
            return "device id does not match the authenticated device";
        }

        if (e.SchemaVersion != SupportedSchemaVersion)
        {
            return $"schema version {e.SchemaVersion} not supported";
        }

        if (string.IsNullOrWhiteSpace(e.DisplayKey) || string.IsNullOrWhiteSpace(e.SessionId))
        {
            return "display key and session id are required";
        }

        if (e.DeviceTimeUtc > now + MaxFutureSkew)
        {
            return "device time is in the future";
        }

        if (e.DeviceTimeUtc < now - MaxAge)
        {
            return "device time is older than the retention window";
        }

        return null;
    }
}

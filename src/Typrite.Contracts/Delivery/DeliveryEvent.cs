namespace Typrite.Contracts.Delivery;

/// <summary>Technical observations only. None of these is a billing assertion; the partner contract decides that.</summary>
public enum DeliveryEventType
{
    AdRequested,
    AdReturned,
    NoFill,
    CreativeDownloaded,
    CreativeRejected,
    RenderStarted,
    RenderFailed,
    VisibilityObserved,
    VisibilityLost,
    PlaybackFirstQuartile,
    PlaybackMidpoint,
    PlaybackThirdQuartile,
    PlaybackCompleted,
    ClickThrough,
}

public sealed record DeliveryEvent(
    Guid ClientEventId,
    Guid DeviceId,
    string SessionId,
    string DisplayKey,
    Guid? DecisionId,
    Guid? CreativeId,
    DeliveryEventType Type,
    DateTimeOffset DeviceTimeUtc,
    long? ReportedDurationMs,
    int ConfigurationVersion,
    int SchemaVersion = 1);

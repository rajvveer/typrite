namespace Typrite.Player.Playback;

/// <summary>Identifiers stamped onto every event of one creative playback. <c>DurationMs</c> is null for stills; <c>DecisionId</c> is null for the house creative.</summary>
public sealed record PlaybackContext(
    Guid DeviceId,
    string SessionId,
    string DisplayKey,
    Guid? DecisionId,
    Guid CreativeId,
    int ConfigurationVersion,
    int? DurationMs);

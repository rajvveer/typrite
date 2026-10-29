namespace Typrite.Contracts.Delivery;

/// <summary>Server-normalised description of one approved creative. Never advertiser-supplied markup.</summary>
public sealed record CreativeManifest(
    Guid CreativeId,
    string Format,
    string MediaType,
    long SizeBytes,
    string Sha256,
    int? DurationMs,
    Uri ContentUri,
    string? ClickThroughUrl);

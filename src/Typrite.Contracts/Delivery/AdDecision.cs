namespace Typrite.Contracts.Delivery;

public sealed record AdDecisionRequest(string PlacementId, string IdempotencyKey, int ConfigurationVersion, IReadOnlyList<string> SupportedFormats);

public sealed record AdDecisionResponse(Guid DecisionId, Guid CampaignId, CreativeManifest Creative, DateTimeOffset ExpiresAtUtc);

public sealed record EventBatchRequest(IReadOnlyList<DeliveryEvent> Events);

public sealed record EventBatchResponse(IReadOnlyList<Guid> Accepted, IReadOnlyList<Guid> Rejected);

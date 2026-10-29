using Typrite.Contracts.Delivery;
using Typrite.Player.Creatives;

namespace Typrite.Player.Delivery;

public enum CreativeSource
{
    House,
    Cache,
    Downloaded,
}

public sealed record DeviceContext(Guid DeviceId, string SessionId, int ConfigurationVersion);

/// <summary>Local fallback shown whenever a real decision is unavailable. Bytes are embedded with the app.</summary>
public sealed record HouseCreative(CreativeManifest Manifest, byte[] Bytes);

public sealed record ResolvedCreative(CreativeSource Source, CreativeManifest Manifest, byte[] Bytes, AdDecisionResponse? Decision);

/// <summary>
/// Request → validate → cache or fetch → verify → creative, or the house creative when anything fails.
/// Every step emits a technical event; nothing here renders or claims visibility.
/// </summary>
public sealed class AdDeliveryPipeline
{
    private readonly IDeliveryApi _api;
    private readonly CreativeCache _cache;
    private readonly CreativePolicy _policy;
    private readonly HouseCreative _house;
    private readonly DeviceContext _ctx;
    private readonly TimeProvider _time;
    private readonly Action<DeliveryEvent> _emit;

    public AdDeliveryPipeline(
        IDeliveryApi api,
        CreativeCache cache,
        CreativePolicy policy,
        HouseCreative house,
        DeviceContext context,
        TimeProvider time,
        Action<DeliveryEvent> emit)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(house);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(emit);
        _api = api;
        _cache = cache;
        _policy = policy;
        _house = house;
        _ctx = context;
        _time = time;
        _emit = emit;
    }

    public async Task<ResolvedCreative> NextAsync(string displayKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayKey);
        Emit(displayKey, DeliveryEventType.AdRequested, null, null);

        AdDecisionResponse? decision;
        try
        {
            var request = new AdDecisionRequest("bar", Guid.NewGuid().ToString("N"), _ctx.ConfigurationVersion, [.. _policy.AllowedFormats]);
            decision = await _api.RequestDecisionAsync(displayKey, request, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            decision = null;
        }

        if (decision is null)
        {
            Emit(displayKey, DeliveryEventType.NoFill, null, null);
            return House(null);
        }

        var m = decision.Creative;
        Emit(displayKey, DeliveryEventType.AdReturned, decision.DecisionId, m.CreativeId);

        if (CreativeValidator.Validate(m, _policy).Count > 0)
        {
            Emit(displayKey, DeliveryEventType.CreativeRejected, decision.DecisionId, m.CreativeId);
            return House(decision);
        }

        var cached = await _cache.GetAsync(m.CreativeId, ct).ConfigureAwait(false);
        if (cached is not null && CreativeValidator.VerifyBytes(m, cached).Count == 0)
        {
            return new ResolvedCreative(CreativeSource.Cache, m, cached, decision);
        }

        byte[] bytes;
        try
        {
            bytes = await _api.FetchCreativeAsync(m.ContentUri, m.SizeBytes, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            Emit(displayKey, DeliveryEventType.CreativeRejected, decision.DecisionId, m.CreativeId);
            return House(decision);
        }

        if (CreativeValidator.VerifyBytes(m, bytes).Count > 0)
        {
            Emit(displayKey, DeliveryEventType.CreativeRejected, decision.DecisionId, m.CreativeId);
            return House(decision);
        }

        await _cache.PutAsync(m.CreativeId, bytes, ct).ConfigureAwait(false);
        Emit(displayKey, DeliveryEventType.CreativeDownloaded, decision.DecisionId, m.CreativeId);
        return new ResolvedCreative(CreativeSource.Downloaded, m, bytes, decision);
    }

    private ResolvedCreative House(AdDecisionResponse? decision) =>
        new(CreativeSource.House, _house.Manifest, _house.Bytes, decision);

    private void Emit(string displayKey, DeliveryEventType type, Guid? decisionId, Guid? creativeId) =>
        _emit(new DeliveryEvent(Guid.NewGuid(), _ctx.DeviceId, _ctx.SessionId, displayKey, decisionId, creativeId, type,
            _time.GetUtcNow(), null, _ctx.ConfigurationVersion));
}

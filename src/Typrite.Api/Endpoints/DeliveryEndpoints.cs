using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using Typrite.Api.Auth;
using Typrite.Contracts.Delivery;
using Typrite.Domain.Campaigns;
using Typrite.Domain.Delivery;
using Typrite.Infrastructure.Partners;
using Typrite.Infrastructure.Persistence;
using Typrite.Infrastructure.Storage;

namespace Typrite.Api.Endpoints;

public sealed record ApiOptions
{
    /// <summary>Public https origin devices use to fetch creative bytes.</summary>
    public string PublicBaseUrl { get; init; } = "https://localhost:7210";

    public int DecisionTtlMinutes { get; init; } = 10;
}

public static class DeliveryEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/devices/{deviceId:guid}/displays/{displayKey}/decisions", DecideAsync)
            .RequireAuthorization(Policies.Device)
            .RequireRateLimiting("device")
            .WithTags("Delivery");

        app.MapGet("/api/v1/creatives/{creativeId:guid}/content", ContentAsync)
            .RequireAuthorization(Policies.Device)
            .RequireRateLimiting("device")
            .WithTags("Delivery");
    }

    private static async Task<IResult> DecideAsync(
        Guid deviceId, string displayKey, AdDecisionRequest req, HttpContext http, TypriteDbContext db,
        ApiOptions api, IAdPartner partner, TimeProvider time, CancellationToken ct)
    {
        var me = DevicePrincipal.From(http.User);
        if (me.DeviceId != deviceId)
        {
            return Results.Forbid();
        }

        if (string.IsNullOrWhiteSpace(req.IdempotencyKey) || req.IdempotencyKey.Length > 100 || displayKey.Length > 64)
        {
            return Results.BadRequest(new { error = "idempotency key and display key are required and bounded" });
        }

        var existing = await db.AdDecisions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.DeviceId == deviceId && d.IdempotencyKey == req.IdempotencyKey, ct);
        if (existing is not null)
        {
            var creative = await db.Creatives.AsNoTracking().FirstAsync(c => c.Id == existing.CreativeId, ct);
            return Results.Ok(ToResponse(existing, creative, api));
        }

        var now = time.GetUtcNow();
        var campaigns = await db.Campaigns.AsNoTracking()
            .Where(c => c.AccountId == me.AccountId && c.Status == CampaignStatus.Active && c.StartsAtUtc <= now && c.EndsAtUtc > now)
            .ToListAsync(ct);
        var ids = campaigns.Select(c => c.Id).ToList();
        var creatives = await db.Creatives.AsNoTracking().Where(c => ids.Contains(c.CampaignId)).ToListAsync(ct);
        var inventory = campaigns
            .Select(c => (Campaign: c, Creatives: (IReadOnlyList<Creative>)creatives.Where(x => x.CampaignId == c.Id).ToList()))
            .ToList();

        Guid? partnerId = null;
        string? partnerRef = null;
        (Campaign Campaign, Creative Creative)? pick = null;

        var enabledPartner = await db.Partners.AsNoTracking().FirstOrDefaultAsync(p => p.Enabled && p.AdapterType == partner.AdapterType, ct);
        if (enabledPartner is not null)
        {
            var pd = await partner.DecideAsync(deviceId, displayKey, inventory, ct);
            if (pd is not null)
            {
                var campaign = campaigns.FirstOrDefault(c => c.Id == pd.CampaignId);
                var creative = creatives.FirstOrDefault(c => c.Id == pd.CreativeId && req.SupportedFormats.Contains(c.Format));
                if (campaign is not null && creative is not null)
                {
                    pick = (campaign, creative);
                    partnerId = enabledPartner.Id;
                    partnerRef = pd.PartnerReference;
                }
            }
        }

        pick ??= AdSelector.Choose(inventory, req.SupportedFormats, now, Random.Shared);
        if (pick is null)
        {
            return Results.NoContent();
        }

        var decision = new AdDecision
        {
            Id = Guid.NewGuid(),
            DeviceId = deviceId,
            DisplayKey = displayKey,
            PlacementId = req.PlacementId,
            CampaignId = pick.Value.Campaign.Id,
            CreativeId = pick.Value.Creative.Id,
            IdempotencyKey = req.IdempotencyKey,
            PartnerId = partnerId,
            PartnerReference = partnerRef,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(api.DecisionTtlMinutes),
        };
        db.AdDecisions.Add(decision);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(decision, pick.Value.Creative, api));
    }

    private static async Task<IResult> ContentAsync(Guid creativeId, HttpContext http, TypriteDbContext db, IBlobStorage blobs, CancellationToken ct)
    {
        var me = DevicePrincipal.From(http.User);
        var creative = await db.Creatives.AsNoTracking()
            .Join(db.Campaigns.AsNoTracking(), c => c.CampaignId, k => k.Id, (c, k) => new { Creative = c, k.AccountId })
            .FirstOrDefaultAsync(x => x.Creative.Id == creativeId && x.AccountId == me.AccountId, ct);
        if (creative is null)
        {
            return Results.NotFound();
        }

        var blob = await blobs.GetAsync(creative.Creative.StorageKey, ct);
        if (blob is null)
        {
            return Results.NotFound();
        }

        http.Response.Headers[HeaderNames.ETag] = $"\"{creative.Creative.Sha256}\"";
        http.Response.Headers[HeaderNames.CacheControl] = "private, max-age=86400, immutable";
        return Results.File(blob.Value.Bytes, creative.Creative.MediaType);
    }

    private static AdDecisionResponse ToResponse(AdDecision d, Creative c, ApiOptions api) =>
        new(d.Id, d.CampaignId,
            new CreativeManifest(c.Id, c.Format, c.MediaType, c.SizeBytes, c.Sha256, c.DurationMs,
                new Uri($"{api.PublicBaseUrl.TrimEnd('/')}/api/v1/creatives/{c.Id}/content"), c.ClickThroughUrl),
            d.ExpiresAtUtc);
}

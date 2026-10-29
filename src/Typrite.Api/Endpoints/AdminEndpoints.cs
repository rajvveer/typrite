using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Typrite.Api.Auth;
using Typrite.Contracts.Delivery;
using Typrite.Domain.Accounts;
using Typrite.Domain.Campaigns;
using Typrite.Domain.Devices;
using Typrite.Domain.Releases;
using Typrite.Infrastructure.Persistence;
using Typrite.Infrastructure.Storage;
using Typrite.Player.Creatives;

namespace Typrite.Api.Endpoints;

/// <summary>Operator console API. Every query is scoped to the operator's account; roles gate writes.</summary>
public static class AdminEndpoints
{
    public sealed record CreateAccountRequest(string Name);

    public sealed record CreateCampaignRequest(string Name, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc, int Weight);

    public sealed record IssueCommandRequest(string Type, string? Payload, int? TtlMinutes);

    public sealed record CreateReleaseRequest(string Version, string CpuArchitecture, string PackageSha256, string PackageUrl, string ManifestSignature);

    public sealed record RolloutRequest(int Percent);

    public static void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/admin").WithTags("Admin");

        g.MapPost("/accounts", async (CreateAccountRequest req, HttpContext http, TypriteDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
            {
                return Results.BadRequest(new { error = "name required" });
            }

            var me = OperatorPrincipal.From(http.User);
            var account = new Account { Id = Guid.NewGuid(), Name = req.Name.Trim(), CreatedAtUtc = time.GetUtcNow() };
            db.Accounts.Add(account);
            AuditWriter.Record(db, time, me.Subject, account.Id, "account.create", $"account:{account.Id}");
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = account.Id });
        }).RequireAuthorization(Policies.Administrator);

        g.MapGet("/devices", async (int? pageSize, Guid? cursor, HttpContext http, TypriteDbContext db, CancellationToken ct) =>
        {
            var me = OperatorPrincipal.From(http.User);
            var size = Math.Clamp(pageSize ?? 50, 1, 200);
            var query = db.Devices.AsNoTracking().Where(d => d.AccountId == me.AccountId).OrderBy(d => d.Id);
            if (cursor is { } c)
            {
                query = (IOrderedQueryable<Device>)query.Where(d => d.Id.CompareTo(c) > 0);
            }

            var page = await query.Take(size + 1).ToListAsync(ct);
            var items = page.Take(size).Select(d => new
            {
                d.Id, Status = d.Status.ToString(), d.AppVersion, d.CpuArchitecture, d.LastSeenUtc, d.EnrolledAtUtc,
                CredentialKind = d.CredentialKind.ToString(), d.ConfigurationVersion,
            });
            return Results.Ok(new { items, nextCursor = page.Count > size ? page[size - 1].Id.ToString() : null });
        }).RequireAuthorization(Policies.Viewer);

        g.MapPost("/devices/{deviceId:guid}/commands", async (Guid deviceId, IssueCommandRequest req, HttpContext http, TypriteDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            if (!RemoteCommandTypes.All.Contains(req.Type))
            {
                return Results.BadRequest(new { error = "command type not allowlisted" });
            }

            var me = OperatorPrincipal.From(http.User);
            if (!await db.Devices.AnyAsync(d => d.Id == deviceId && d.AccountId == me.AccountId, ct))
            {
                return Results.NotFound();
            }

            var now = time.GetUtcNow();
            var command = new RemoteCommand
            {
                Id = Guid.NewGuid(),
                DeviceId = deviceId,
                Type = req.Type,
                PayloadJson = req.Payload,
                RequestedBy = me.Subject,
                RequestedAtUtc = now,
                ExpiresAtUtc = now.AddMinutes(Math.Clamp(req.TtlMinutes ?? 60, 1, 24 * 60)),
            };
            db.RemoteCommands.Add(command);
            AuditWriter.Record(db, time, me.Subject, me.AccountId, "device.command", $"device:{deviceId}", new { command.Type, command.Id });
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = command.Id });
        }).RequireAuthorization(Policies.DeviceOperator);

        g.MapGet("/campaigns", async (HttpContext http, TypriteDbContext db, CancellationToken ct) =>
        {
            var me = OperatorPrincipal.From(http.User);
            var items = await db.Campaigns.AsNoTracking().Where(c => c.AccountId == me.AccountId).OrderByDescending(c => c.CreatedAtUtc)
                .Select(c => new { c.Id, c.Name, Status = c.Status.ToString(), c.StartsAtUtc, c.EndsAtUtc, c.Weight })
                .ToListAsync(ct);
            return Results.Ok(new { items });
        }).RequireAuthorization(Policies.Viewer);

        g.MapPost("/campaigns", async (CreateCampaignRequest req, HttpContext http, TypriteDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name) || req.EndsAtUtc <= req.StartsAtUtc || req.Weight <= 0)
            {
                return Results.BadRequest(new { error = "name, a positive weight and a non-empty window are required" });
            }

            var me = OperatorPrincipal.From(http.User);
            var campaign = new Campaign
            {
                Id = Guid.NewGuid(), AccountId = me.AccountId, Name = req.Name.Trim(),
                StartsAtUtc = req.StartsAtUtc, EndsAtUtc = req.EndsAtUtc, Weight = req.Weight, CreatedAtUtc = time.GetUtcNow(),
            };
            db.Campaigns.Add(campaign);
            AuditWriter.Record(db, time, me.Subject, me.AccountId, "campaign.create", $"campaign:{campaign.Id}");
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = campaign.Id });
        }).RequireAuthorization(Policies.CampaignOperator);

        g.MapPost("/campaigns/{campaignId:guid}/creatives", async (Guid campaignId, IFormFile file, [FromForm] string format, [FromForm] int? durationMs, [FromForm] string? clickThroughUrl,
            HttpContext http, TypriteDbContext db, IBlobStorage blobs, TimeProvider time, CancellationToken ct) =>
        {
            var me = OperatorPrincipal.From(http.User);
            var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == campaignId && c.AccountId == me.AccountId, ct);
            if (campaign is null)
            {
                return Results.NotFound();
            }

            if (file.Length is <= 0 or > 25 * 1024 * 1024)
            {
                return Results.BadRequest(new { error = "file is empty or above 25 MB" });
            }

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();
            var id = Guid.NewGuid();
            var manifest = new CreativeManifest(id, format, file.ContentType, bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)),
                durationMs, new Uri("https://placeholder.invalid/"), clickThroughUrl);
            var problems = CreativeValidator.Validate(manifest, CreativePolicy.Default).Concat(CreativeValidator.VerifyBytes(manifest, bytes)).ToList();
            if (problems.Count > 0)
            {
                return Results.BadRequest(new { error = "creative rejected", problems });
            }

            var key = $"creatives/{id:N}";
            await blobs.PutAsync(key, bytes, file.ContentType, ct);
            db.Creatives.Add(new Creative
            {
                Id = id, CampaignId = campaignId, Format = format, MediaType = file.ContentType, SizeBytes = bytes.Length,
                Sha256 = manifest.Sha256, StorageKey = key, DurationMs = durationMs, ClickThroughUrl = clickThroughUrl, CreatedAtUtc = time.GetUtcNow(),
            });
            AuditWriter.Record(db, time, me.Subject, me.AccountId, "creative.upload", $"campaign:{campaignId}", new { creativeId = id, format, file.ContentType });
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id });
        }).RequireAuthorization(Policies.CampaignOperator).DisableAntiforgery();

        foreach (var (action, target) in new[] { ("approve", CampaignStatus.Approved), ("activate", CampaignStatus.Active), ("pause", CampaignStatus.Paused), ("end", CampaignStatus.Ended) })
        {
            g.MapPost($"/campaigns/{{campaignId:guid}}/{action}", async (Guid campaignId, HttpContext http, TypriteDbContext db, TimeProvider time, CancellationToken ct) =>
            {
                var me = OperatorPrincipal.From(http.User);
                var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == campaignId && c.AccountId == me.AccountId, ct);
                if (campaign is null)
                {
                    return Results.NotFound();
                }

                if (!CampaignLifecycle.CanTransition(campaign.Status, target))
                {
                    return Results.Conflict(new { error = $"cannot move from {campaign.Status} to {target}" });
                }

                if (target == CampaignStatus.Approved)
                {
                    var creatives = await db.Creatives.CountAsync(c => c.CampaignId == campaignId, ct);
                    if (!CampaignLifecycle.CanApprove(creatives))
                    {
                        return Results.Conflict(new { error = "campaign has no creatives" });
                    }

                    campaign.ApprovedBy = me.Subject;
                    campaign.ApprovedAtUtc = time.GetUtcNow();
                }

                var from = campaign.Status;
                campaign.Status = target;
                AuditWriter.Record(db, time, me.Subject, me.AccountId, $"campaign.{action}", $"campaign:{campaignId}", new { from = from.ToString(), to = target.ToString() });
                await db.SaveChangesAsync(ct);
                return Results.Ok(new { campaign.Id, status = campaign.Status.ToString() });
            }).RequireAuthorization(Policies.CampaignOperator);
        }

        g.MapPost("/releases", async (CreateReleaseRequest req, HttpContext http, TypriteDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Version) || req.PackageSha256.Length != 64 || !Uri.TryCreate(req.PackageUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
            {
                return Results.BadRequest(new { error = "version, 64-hex sha256 and an https package url are required" });
            }

            var me = OperatorPrincipal.From(http.User);
            if (await db.Releases.AnyAsync(r => r.Version == req.Version && r.CpuArchitecture == req.CpuArchitecture, ct))
            {
                return Results.Conflict(new { error = "release version already exists for this architecture" });
            }

            var release = new Release
            {
                Id = Guid.NewGuid(), Version = req.Version, CpuArchitecture = req.CpuArchitecture, PackageSha256 = req.PackageSha256.ToLowerInvariant(),
                PackageUrl = req.PackageUrl, ManifestSignature = req.ManifestSignature, CreatedBy = me.Subject, CreatedAtUtc = time.GetUtcNow(),
            };
            db.Releases.Add(release);
            AuditWriter.Record(db, time, me.Subject, me.AccountId, "release.create", $"release:{release.Id}", new { req.Version, req.CpuArchitecture });
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = release.Id });
        }).RequireAuthorization(Policies.PlatformReleaseManager);

        g.MapPost("/releases/{releaseId:guid}/rollout", async (Guid releaseId, RolloutRequest req, HttpContext http, TypriteDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            if (req.Percent is < 0 or > 100)
            {
                return Results.BadRequest(new { error = "percent must be 0..100" });
            }

            var me = OperatorPrincipal.From(http.User);
            var release = await db.Releases.FirstOrDefaultAsync(r => r.Id == releaseId, ct);
            if (release is null)
            {
                return Results.NotFound();
            }

            release.RolloutPercent = req.Percent;
            release.Status = req.Percent == 0 ? ReleaseStatus.Paused : req.Percent == 100 ? ReleaseStatus.Complete : ReleaseStatus.RollingOut;
            AuditWriter.Record(db, time, me.Subject, me.AccountId, "release.rollout", $"release:{releaseId}", new { req.Percent });
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { release.Id, status = release.Status.ToString(), release.RolloutPercent });
        }).RequireAuthorization(Policies.PlatformReleaseManager);

        g.MapPost("/releases/{releaseId:guid}/rollback", async (Guid releaseId, HttpContext http, TypriteDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var me = OperatorPrincipal.From(http.User);
            var release = await db.Releases.FirstOrDefaultAsync(r => r.Id == releaseId, ct);
            if (release is null)
            {
                return Results.NotFound();
            }

            release.RolloutPercent = 0;
            release.Status = ReleaseStatus.RolledBack;
            AuditWriter.Record(db, time, me.Subject, me.AccountId, "release.rollback", $"release:{releaseId}");
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { release.Id, status = release.Status.ToString() });
        }).RequireAuthorization(Policies.PlatformReleaseManager);

        g.MapGet("/releases", async (HttpContext http, TypriteDbContext db, CancellationToken ct) =>
        {
            var items = await db.Releases.AsNoTracking().OrderByDescending(r => r.CreatedAtUtc)
                .Select(r => new { r.Id, r.Version, r.CpuArchitecture, Status = r.Status.ToString(), r.RolloutPercent, r.CreatedAtUtc })
                .ToListAsync(ct);
            return Results.Ok(new { items });
        }).RequireAuthorization(Policies.PlatformViewer);

        g.MapGet("/audit", async (string? resource, int? take, HttpContext http, TypriteDbContext db, CancellationToken ct) =>
        {
            var me = OperatorPrincipal.From(http.User);
            var query = db.AuditEntries.AsNoTracking().Where(a => a.AccountId == me.AccountId);
            if (!string.IsNullOrEmpty(resource))
            {
                query = query.Where(a => a.Resource == resource);
            }

            var items = await query.OrderByDescending(a => a.TimeUtc).Take(Math.Clamp(take ?? 100, 1, 500))
                .Select(a => new { a.Actor, a.Action, a.Resource, a.TimeUtc, a.MetadataJson }).ToListAsync(ct);
            return Results.Ok(new { items });
        }).RequireAuthorization(Policies.Viewer);

        g.MapGet("/events/count", async (Guid deviceId, HttpContext http, TypriteDbContext db, CancellationToken ct) =>
        {
            var me = OperatorPrincipal.From(http.User);
            if (!await db.Devices.AnyAsync(d => d.Id == deviceId && d.AccountId == me.AccountId, ct))
            {
                return Results.NotFound();
            }

            return Results.Ok(new { count = await db.DeliveryEvents.LongCountAsync(e => e.DeviceId == deviceId, ct) });
        }).RequireAuthorization(Policies.Viewer);

        g.MapGet("/reports/delivery", async (DateOnly? from, DateOnly? to, HttpContext http, TypriteDbContext db, CancellationToken ct) =>
        {
            var me = OperatorPrincipal.From(http.User);
            var start = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7));
            var end = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var campaignIds = await db.Campaigns.AsNoTracking().Where(c => c.AccountId == me.AccountId).Select(c => c.Id).ToListAsync(ct);
            var items = await db.DeliverySummaries.AsNoTracking()
                .Where(s => campaignIds.Contains(s.CampaignId) && s.Day >= start && s.Day <= end)
                .OrderBy(s => s.Day).ThenBy(s => s.CampaignId)
                .Select(s => new { s.Day, s.CampaignId, s.CreativeId, Type = s.Type.ToString(), s.Count, s.DistinctDevices, s.VisibleMilliseconds })
                .ToListAsync(ct);
            return Results.Ok(new { from = start, to = end, items });
        }).RequireAuthorization(Policies.Viewer);

        g.MapGet("/reports/reconciliation", async (TypriteDbContext db, CancellationToken ct) =>
        {
            var items = await db.ReconciliationRuns.AsNoTracking().OrderByDescending(r => r.StartedAtUtc).Take(50)
                .Select(r => new { r.Id, r.PartnerId, r.PeriodStart, r.PeriodEnd, Status = r.Status.ToString(), r.OurCount, r.PartnerCount, r.MismatchSummaryJson, r.StartedAtUtc, r.FinishedAtUtc })
                .ToListAsync(ct);
            return Results.Ok(new { items });
        }).RequireAuthorization(Policies.PlatformViewer);
    }
}

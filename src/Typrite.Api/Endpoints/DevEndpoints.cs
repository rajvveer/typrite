using Microsoft.EntityFrameworkCore;
using Typrite.Api.Auth;
using Typrite.Domain.Accounts;
using Typrite.Domain.Devices;
using Typrite.Infrastructure.Persistence;

namespace Typrite.Api.Endpoints;

/// <summary>Development-only identity shortcuts. Mapped only when <c>Auth:AllowDevAuth</c> is true.</summary>
public static class DevEndpoints
{
    public sealed record DevRegisterRequest(Guid DeviceId, string Secret, string? AppVersion, Guid? AccountId);

    public sealed record DevLoginRequest(string Subject, string Role, Guid? AccountId);

    public static void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1").AllowAnonymous().RequireRateLimiting("anonymous").WithTags("Development");

        g.MapPost("/devices/dev-register", async (DevRegisterRequest req, TypriteDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Secret) || req.DeviceId == Guid.Empty)
            {
                return Results.BadRequest(new { error = "deviceId and secret are required" });
            }

            var accountId = req.AccountId ?? Seed.DefaultAccountId;
            if (!await db.Accounts.AnyAsync(a => a.Id == accountId, ct))
            {
                return Results.BadRequest(new { error = "unknown account" });
            }

            var hash = DeviceAuthenticationHandler.Hash(req.Secret);
            var existing = await db.Devices.FirstOrDefaultAsync(d => d.Id == req.DeviceId, ct);
            if (existing is not null)
            {
                if (existing.CredentialKind != DeviceCredentialKind.Dev || existing.CredentialHash != hash)
                {
                    return Results.Conflict(new { error = "device already registered with a different credential" });
                }

                existing.AppVersion = req.AppVersion ?? existing.AppVersion;
                existing.LastSeenUtc = time.GetUtcNow();
                await db.SaveChangesAsync(ct);
                return Results.Ok(new { deviceId = existing.Id, accountId = existing.AccountId });
            }

            db.Devices.Add(new Device
            {
                Id = req.DeviceId,
                AccountId = accountId,
                CredentialKind = DeviceCredentialKind.Dev,
                CredentialHash = hash,
                AppVersion = req.AppVersion,
                EnrolledAtUtc = time.GetUtcNow(),
                LastSeenUtc = time.GetUtcNow(),
            });
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { deviceId = req.DeviceId, accountId });
        });

        g.MapPost("/admin/dev-login", async (DevLoginRequest req, TypriteDbContext db, TokenIssuer tokens, CancellationToken ct) =>
        {
            if (!Enum.TryParse<OperatorRole>(req.Role, ignoreCase: false, out var role))
            {
                return Results.BadRequest(new { error = "unknown role" });
            }

            var accountId = req.AccountId ?? Seed.DefaultAccountId;
            if (!await db.Accounts.AnyAsync(a => a.Id == accountId, ct))
            {
                return Results.BadRequest(new { error = "unknown account" });
            }

            var op = await db.Operators.FirstOrDefaultAsync(o => o.ExternalSubject == req.Subject && o.AccountId == accountId, ct);
            if (op is null)
            {
                db.Operators.Add(new OperatorAccount { Id = Guid.NewGuid(), ExternalSubject = req.Subject, AccountId = accountId, Role = role });
            }
            else
            {
                op.Role = role;
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { token = tokens.IssueOperatorToken(req.Subject, role.ToString(), accountId), accountId });
        });
    }
}

using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Typrite.Api.Auth;
using Typrite.Contracts.Releases;
using Typrite.Domain.Devices;
using Typrite.Domain.Releases;
using Typrite.Infrastructure.Persistence;

namespace Typrite.Api.Endpoints;

/// <summary>Key-based device identity: one-time claims, proof-of-possession tokens, and signed release manifests.</summary>
public static class EnrollmentEndpoints
{
    public sealed record EnrollRequest(string ClaimToken, string PublicKey, string? AppVersion, string? CpuArchitecture);

    public sealed record CreateClaimRequest(int? TtlMinutes);

    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/devices/enroll", EnrollAsync).AllowAnonymous().RequireRateLimiting("anonymous").WithTags("Enrollment");
        app.MapPost("/api/v1/devices/{deviceId:guid}/tokens", TokenAsync).AllowAnonymous().RequireRateLimiting("anonymous").WithTags("Enrollment");
        app.MapGet("/api/v1/releases/publisher-key", (ReleaseSigner signer) => Results.Text(signer.PublicKeyPem, "application/x-pem-file"))
            .AllowAnonymous().WithTags("Releases");

        app.MapGet("/api/v1/devices/{deviceId:guid}/releases/current", CurrentReleaseAsync)
            .RequireAuthorization(Policies.Device).RequireRateLimiting("device").WithTags("Releases");

        app.MapPost("/api/v1/admin/enrollment-claims", CreateClaimAsync)
            .RequireAuthorization(Policies.DeviceOperator).WithTags("Admin");
    }

    private static async Task<IResult> CreateClaimAsync(CreateClaimRequest req, HttpContext http, TypriteDbContext db, TimeProvider time, CancellationToken ct)
    {
        var me = OperatorPrincipal.From(http.User);
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var claim = new EnrollmentClaim
        {
            Id = Guid.NewGuid(),
            AccountId = me.AccountId,
            TokenHash = Hash(token),
            ExpiresAtUtc = time.GetUtcNow().AddMinutes(Math.Clamp(req.TtlMinutes ?? 60, 5, 7 * 24 * 60)),
            IssuedBy = me.Subject,
        };
        db.EnrollmentClaims.Add(claim);
        AuditWriter.Record(db, time, me.Subject, me.AccountId, "enrollment.claim", $"claim:{claim.Id}", new { claim.ExpiresAtUtc });
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { claim.Id, token, claim.ExpiresAtUtc });
    }

    private static async Task<IResult> EnrollAsync(EnrollRequest req, TypriteDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ClaimToken) || string.IsNullOrWhiteSpace(req.PublicKey))
        {
            return Results.BadRequest(new { error = "claimToken and publicKey are required" });
        }

        byte[] spki;
        try
        {
            spki = Convert.FromBase64String(req.PublicKey);
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(spki, out _);
            if (rsa.KeySize < 2048)
            {
                return Results.BadRequest(new { error = "key must be at least 2048 bits" });
            }
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return Results.BadRequest(new { error = "publicKey must be a base64 SubjectPublicKeyInfo" });
        }

        var now = time.GetUtcNow();
        var claim = await db.EnrollmentClaims.FirstOrDefaultAsync(c => c.TokenHash == Hash(req.ClaimToken), ct);
        if (claim is null || claim.UsedByDeviceId is not null || claim.ExpiresAtUtc <= now)
        {
            return Results.Unauthorized();
        }

        var device = new Device
        {
            Id = Guid.NewGuid(),
            AccountId = claim.AccountId,
            CredentialKind = DeviceCredentialKind.Key,
            CredentialHash = Convert.ToHexStringLower(SHA256.HashData(spki)),
            PublicKey = req.PublicKey,
            AppVersion = req.AppVersion,
            CpuArchitecture = req.CpuArchitecture,
            EnrolledAtUtc = now,
            LastSeenUtc = now,
        };
        claim.UsedByDeviceId = device.Id;
        claim.UsedAtUtc = now;
        db.Devices.Add(device);
        AuditWriter.Record(db, time, "device-enrollment", claim.AccountId, "device.enroll", $"device:{device.Id}", new { claimId = claim.Id });
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { deviceId = device.Id, accountId = device.AccountId });
    }

    private static async Task<IResult> TokenAsync(Guid deviceId, DeviceProof proof, TypriteDbContext db, TokenIssuer tokens, TimeProvider time, CancellationToken ct)
    {
        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId && d.Status == DeviceStatus.Active && d.CredentialKind == DeviceCredentialKind.Key, ct);
        if (device?.PublicKey is null || !ProofOfPossession.Verify(deviceId, proof, device.PublicKey, time.GetUtcNow()))
        {
            return Results.Unauthorized();
        }

        device.LastSeenUtc = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        var token = tokens.IssueDeviceToken(device.Id, device.AccountId, "key");
        return Results.Ok(new { token, expiresAtUtc = time.GetUtcNow().AddMinutes(30) });
    }

    private static async Task<IResult> CurrentReleaseAsync(Guid deviceId, HttpContext http, TypriteDbContext db, ReleaseSigner signer, CancellationToken ct)
    {
        var me = DevicePrincipal.From(http.User);
        if (me.DeviceId != deviceId)
        {
            return Results.Forbid();
        }

        var arch = await db.Devices.AsNoTracking().Where(d => d.Id == deviceId).Select(d => d.CpuArchitecture).FirstOrDefaultAsync(ct) ?? "x64";
        var candidates = await db.Releases.AsNoTracking()
            .Where(r => r.CpuArchitecture == arch && (r.Status == ReleaseStatus.RollingOut || r.Status == ReleaseStatus.Complete))
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(ct);

        var release = candidates.FirstOrDefault(r => RolloutCohort.IsIncluded(deviceId, r.RolloutPercent));
        if (release is null)
        {
            return Results.NoContent();
        }

        var manifest = new ReleaseManifest(release.Version, release.CpuArchitecture, release.PackageSha256, new Uri(release.PackageUrl), release.RolloutPercent);
        return Results.Ok(new CurrentReleaseResponse(manifest, signer.Sign(manifest)));
    }
}

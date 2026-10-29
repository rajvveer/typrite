using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Typrite.Contracts;
using Typrite.Contracts.Configuration;
using Typrite.Domain.Accounts;
using Typrite.Domain.Configuration;
using Typrite.Domain.Partners;
using Typrite.Infrastructure.Persistence;

namespace Typrite.Api;

/// <summary>Baseline rows every environment needs: a default account, configuration v1 and the mock partner.</summary>
public static class Seed
{
    public static readonly Guid DefaultAccountId = new("00000000-0000-0000-0000-000000000001");

    public static async Task ApplyAsync(TypriteDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (!await db.Accounts.AnyAsync(a => a.Id == DefaultAccountId, ct))
        {
            db.Accounts.Add(new Account { Id = DefaultAccountId, Name = "Default", CreatedAtUtc = time.GetUtcNow() });
        }

        if (!await db.Configurations.AnyAsync(c => c.AccountId == null, ct))
        {
            var json = JsonSerializer.Serialize(DeviceConfiguration.Default with { ConfigurationVersion = 1 }, ContractsJson.Options);
            db.Configurations.Add(new ConfigurationVersion
            {
                Id = Guid.NewGuid(),
                Version = 1,
                AccountId = null,
                Json = json,
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json))),
                PublishedBy = "seed",
                PublishedAtUtc = time.GetUtcNow(),
            });
        }

        if (!await db.Partners.AnyAsync(p => p.AdapterType == "mock", ct))
        {
            db.Partners.Add(new Partner { Id = Guid.NewGuid(), Name = "Mock partner", AdapterType = "mock", Enabled = false });
        }

        await db.SaveChangesAsync(ct);
    }
}

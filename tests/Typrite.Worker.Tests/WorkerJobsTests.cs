using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Typrite.Contracts.Delivery;
using Typrite.Domain.Audit;
using Typrite.Domain.Campaigns;
using Typrite.Domain.Delivery;
using Typrite.Domain.Partners;
using Typrite.Infrastructure.Partners;
using Typrite.Infrastructure.Persistence;
using Typrite.Worker.Jobs;

namespace Typrite.Worker.Tests;

/// <summary>Runs the jobs once against the local PostgreSQL test database; the API tests have already migrated it.</summary>
public sealed class WorkerJobsTests : IAsyncLifetime
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("TYPRITE_TEST_PG") ?? "Host=localhost;Database=typrite_test;Username=thenoah";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 30, 15, 0, 0, TimeSpan.Zero));
    private readonly Guid _campaign = Guid.NewGuid();
    private readonly Guid _creative = Guid.NewGuid();
    private readonly Guid _account = Guid.NewGuid();

    private static TypriteDbContext Db() => new(new DbContextOptionsBuilder<TypriteDbContext>().UseNpgsql(ConnectionString).Options);

    public async Task InitializeAsync()
    {
        await using var db = Db();
        await db.Database.MigrateAsync();
        db.Accounts.Add(new Domain.Accounts.Account { Id = _account, Name = "worker-test", CreatedAtUtc = _time.GetUtcNow() });
        db.Campaigns.Add(new Campaign { Id = _campaign, AccountId = _account, Name = "w", Status = CampaignStatus.Active, StartsAtUtc = _time.GetUtcNow().AddDays(-1), EndsAtUtc = _time.GetUtcNow().AddDays(1), CreatedAtUtc = _time.GetUtcNow() });
        db.Creatives.Add(new Creative { Id = _creative, CampaignId = _campaign, Format = "400x225", MediaType = "image/png", SizeBytes = 1, Sha256 = new string('a', 64), StorageKey = "k", CreatedAtUtc = _time.GetUtcNow() });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SeedEventsAsync(int devices, int visibleEach, long visibleMs)
    {
        await using var db = Db();
        for (var d = 0; d < devices; d++)
        {
            var deviceId = Guid.NewGuid();
            db.Devices.Add(new Domain.Devices.Device { Id = deviceId, AccountId = _account, CredentialKind = Domain.Devices.DeviceCredentialKind.Dev, CredentialHash = Guid.NewGuid().ToString("N"), EnrolledAtUtc = _time.GetUtcNow() });
            var decision = new AdDecision { Id = Guid.NewGuid(), DeviceId = deviceId, DisplayKey = "disp", PlacementId = "bar", CampaignId = _campaign, CreativeId = _creative, IdempotencyKey = Guid.NewGuid().ToString("N"), CreatedAtUtc = _time.GetUtcNow(), ExpiresAtUtc = _time.GetUtcNow().AddMinutes(10) };
            db.AdDecisions.Add(decision);
            for (var i = 0; i < visibleEach; i++)
            {
                db.DeliveryEvents.Add(Event(deviceId, decision.Id, DeliveryEventType.VisibilityObserved, null));
                db.DeliveryEvents.Add(Event(deviceId, decision.Id, DeliveryEventType.VisibilityLost, visibleMs));
            }
        }

        db.Outbox.Add(new OutboxMessage { Type = "events-ingested", PayloadJson = $$"""{"receivedAtUtc":"{{_time.GetUtcNow():O}}"}""", CreatedAtUtc = _time.GetUtcNow() });
        await db.SaveChangesAsync();
    }

    private DeliveryEventRecord Event(Guid device, Guid decision, DeliveryEventType type, long? duration) => new()
    {
        ClientEventId = Guid.NewGuid(), DeviceId = device, SessionId = "s", DisplayKey = "disp", DecisionId = decision, CreativeId = _creative,
        Type = type, DeviceTimeUtc = _time.GetUtcNow(), ReceivedAtUtc = _time.GetUtcNow(), ReportedDurationMs = duration, ConfigurationVersion = 1, SchemaVersion = 1,
    };

    [Fact]
    public async Task Aggregator_builds_daily_summaries_from_outbox_days_and_marks_messages_published()
    {
        await SeedEventsAsync(devices: 3, visibleEach: 2, visibleMs: 1500);
        var day = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);

        await using (var db = Db())
        {
            var processed = await new DeliveryAggregator(db, _time, NullLogger<DeliveryAggregator>.Instance).RunOnceAsync(CancellationToken.None);
            Assert.True(processed >= 1);
        }

        await using (var db = Db())
        {
            var observed = await db.DeliverySummaries.SingleAsync(s => s.Day == day && s.CreativeId == _creative && s.Type == DeliveryEventType.VisibilityObserved);
            var lost = await db.DeliverySummaries.SingleAsync(s => s.Day == day && s.CreativeId == _creative && s.Type == DeliveryEventType.VisibilityLost);
            Assert.Equal(6, observed.Count);
            Assert.Equal(3, observed.DistinctDevices);
            Assert.Equal(6 * 1500, lost.VisibleMilliseconds);
            Assert.Equal(_campaign, observed.CampaignId);
            Assert.False(await db.Outbox.AnyAsync(o => o.PublishedAtUtc == null && o.Type == "events-ingested" && o.CreatedAtUtc == _time.GetUtcNow()));
        }
    }

    [Fact]
    public async Task Aggregator_is_idempotent()
    {
        await SeedEventsAsync(devices: 1, visibleEach: 1, visibleMs: 100);
        var day = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);

        await using (var db = Db())
        {
            await new DeliveryAggregator(db, _time, NullLogger<DeliveryAggregator>.Instance).RunOnceAsync(CancellationToken.None);
        }

        await using (var db = Db())
        {
            db.Outbox.Add(new OutboxMessage { Type = "events-ingested", PayloadJson = $$"""{"receivedAtUtc":"{{_time.GetUtcNow():O}}"}""", CreatedAtUtc = _time.GetUtcNow() });
            await db.SaveChangesAsync();
            await new DeliveryAggregator(db, _time, NullLogger<DeliveryAggregator>.Instance).RunOnceAsync(CancellationToken.None);
        }

        await using (var check = Db())
        {
            Assert.Equal(1, await check.DeliverySummaries.CountAsync(s => s.Day == day && s.CreativeId == _creative && s.Type == DeliveryEventType.VisibilityObserved));
        }
    }

    [Fact]
    public async Task Reconciliation_compares_our_visibility_with_the_partner_report()
    {
        await SeedEventsAsync(devices: 2, visibleEach: 5, visibleMs: 100); // 10 VisibilityObserved
        var day = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);
        var partner = new MockAdPartner();
        partner.SeedReport([new PartnerDailyCount(day, _creative, 7)]);
        Guid partnerId;

        await using (var db = Db())
        {
            var row = new Partner { Id = Guid.NewGuid(), Name = "recon", AdapterType = "mock", Enabled = true };
            db.Partners.Add(row);
            await db.SaveChangesAsync();
            partnerId = row.Id;
            await new DeliveryAggregator(db, _time, NullLogger<DeliveryAggregator>.Instance).RunOnceAsync(CancellationToken.None);
            await new ReconciliationJob(db, partner, _time, NullLogger<ReconciliationJob>.Instance).RunAsync(day, day, tolerance: 0.02, CancellationToken.None);
        }

        await using (var check = Db())
        {
            var run = await check.ReconciliationRuns.Where(r => r.PartnerId == partnerId).OrderByDescending(r => r.StartedAtUtc).FirstAsync();
            Assert.Equal(ReconciliationStatus.Mismatched, run.Status);
            Assert.Equal(7, run.PartnerCount);
            Assert.True(run.OurCount >= 10);
            Assert.Contains(_creative.ToString(), run.MismatchSummaryJson);
        }
    }
}

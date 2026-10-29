using Microsoft.EntityFrameworkCore;
using Typrite.Domain.Accounts;
using Typrite.Domain.Audit;
using Typrite.Domain.Campaigns;
using Typrite.Domain.Configuration;
using Typrite.Domain.Delivery;
using Typrite.Domain.Devices;
using Typrite.Domain.Partners;
using Typrite.Domain.Releases;

namespace Typrite.Infrastructure.Persistence;

public sealed class TypriteDbContext(DbContextOptions<TypriteDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<OperatorAccount> Operators => Set<OperatorAccount>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<RemoteCommand> RemoteCommands => Set<RemoteCommand>();
    public DbSet<EnrollmentClaim> EnrollmentClaims => Set<EnrollmentClaim>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<Creative> Creatives => Set<Creative>();
    public DbSet<AdDecision> AdDecisions => Set<AdDecision>();
    public DbSet<DeliveryEventRecord> DeliveryEvents => Set<DeliveryEventRecord>();
    public DbSet<DeliverySummary> DeliverySummaries => Set<DeliverySummary>();
    public DbSet<Release> Releases => Set<Release>();
    public DbSet<Partner> Partners => Set<Partner>();
    public DbSet<ReconciliationRun> ReconciliationRuns => Set<ReconciliationRun>();
    public DbSet<ConfigurationVersion> Configurations => Set<ConfigurationVersion>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var b = modelBuilder;
        b.Entity<Account>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        });

        b.Entity<OperatorAccount>(e =>
        {
            e.Property(x => x.ExternalSubject).HasMaxLength(300);
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(x => new { x.ExternalSubject, x.AccountId }).IsUnique();
        });

        b.Entity<Device>(e =>
        {
            e.Property(x => x.CredentialKind).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.CredentialHash).HasMaxLength(64);
            e.Property(x => x.AppVersion).HasMaxLength(50);
            e.Property(x => x.CpuArchitecture).HasMaxLength(10);
            e.Property(x => x.LastHealthJson).HasColumnType("jsonb");
            e.HasIndex(x => x.CredentialHash);
            e.HasIndex(x => x.AccountId);
        });

        b.Entity<RemoteCommand>(e =>
        {
            e.Property(x => x.Type).HasMaxLength(50);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.PayloadJson).HasColumnType("jsonb");
            e.Property(x => x.ResultJson).HasColumnType("jsonb");
            e.Property(x => x.RequestedBy).HasMaxLength(300);
            e.HasIndex(x => new { x.DeviceId, x.Status });
        });

        b.Entity<EnrollmentClaim>(e =>
        {
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.Property(x => x.IssuedBy).HasMaxLength(300);
            e.HasIndex(x => x.TokenHash).IsUnique();
        });

        b.Entity<Campaign>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ApprovedBy).HasMaxLength(300);
            e.HasIndex(x => new { x.AccountId, x.Status });
        });

        b.Entity<Creative>(e =>
        {
            e.Property(x => x.Format).HasMaxLength(20);
            e.Property(x => x.MediaType).HasMaxLength(50);
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.Property(x => x.StorageKey).HasMaxLength(300);
            e.Property(x => x.ClickThroughUrl).HasMaxLength(2000);
            e.HasIndex(x => x.CampaignId);
        });

        b.Entity<AdDecision>(e =>
        {
            e.Property(x => x.DisplayKey).HasMaxLength(64);
            e.Property(x => x.PlacementId).HasMaxLength(50);
            e.Property(x => x.IdempotencyKey).HasMaxLength(100);
            e.Property(x => x.PartnerReference).HasMaxLength(300);
            e.HasIndex(x => new { x.DeviceId, x.IdempotencyKey }).IsUnique();
            e.HasIndex(x => x.CreatedAtUtc);
        });

        b.Entity<DeliveryEventRecord>(e =>
        {
            e.Property(x => x.SessionId).HasMaxLength(64);
            e.Property(x => x.DisplayKey).HasMaxLength(64);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(x => new { x.DeviceId, x.ClientEventId }).IsUnique();
            e.HasIndex(x => x.ReceivedAtUtc);
            e.HasIndex(x => new { x.CreativeId, x.DeviceTimeUtc });
        });

        b.Entity<DeliverySummary>(e =>
        {
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(x => new { x.Day, x.CampaignId, x.CreativeId, x.Type }).IsUnique();
        });

        b.Entity<Release>(e =>
        {
            e.Property(x => x.Version).HasMaxLength(50);
            e.Property(x => x.CpuArchitecture).HasMaxLength(10);
            e.Property(x => x.PackageSha256).HasMaxLength(64);
            e.Property(x => x.PackageUrl).HasMaxLength(2000);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.CreatedBy).HasMaxLength(300);
            e.HasIndex(x => new { x.Version, x.CpuArchitecture }).IsUnique();
        });

        b.Entity<Partner>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.AdapterType).HasMaxLength(50);
        });

        b.Entity<ReconciliationRun>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.MismatchSummaryJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.PartnerId, x.PeriodStart });
        });

        b.Entity<ConfigurationVersion>(e =>
        {
            e.Property(x => x.Json).HasColumnType("jsonb");
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.Property(x => x.PublishedBy).HasMaxLength(300);
            e.HasIndex(x => new { x.AccountId, x.Version }).IsUnique();
        });

        b.Entity<AuditEntry>(e =>
        {
            e.Property(x => x.Actor).HasMaxLength(300);
            e.Property(x => x.Action).HasMaxLength(100);
            e.Property(x => x.Resource).HasMaxLength(300);
            e.Property(x => x.MetadataJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.AccountId, x.Resource });
            e.HasIndex(x => x.TimeUtc);
        });

        b.Entity<OutboxMessage>(e =>
        {
            e.Property(x => x.Type).HasMaxLength(100);
            e.Property(x => x.PayloadJson).HasColumnType("jsonb");
            e.HasIndex(x => x.PublishedAtUtc);
        });
    }
}

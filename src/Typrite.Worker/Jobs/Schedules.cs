using Microsoft.EntityFrameworkCore;
using Typrite.Infrastructure.Persistence;

namespace Typrite.Worker.Jobs;

public sealed record WorkerOptions
{
    public int AggregationIntervalSeconds { get; init; } = 30;

    /// <summary>UTC hour at which yesterday is reconciled.</summary>
    public int ReconciliationHourUtc { get; init; } = 3;

    public double ReconciliationTolerance { get; init; } = 0.02;

    public int OutboxRetentionDays { get; init; } = 7;
}

/// <summary>Runs the aggregator on an interval and prunes published outbox rows. Errors are logged and the loop continues.</summary>
public sealed partial class AggregationHostedService(IServiceScopeFactory scopes, WorkerOptions options, TimeProvider time, ILogger<AggregationHostedService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TypriteDbContext>();
                var aggregator = new DeliveryAggregator(db, time, scope.ServiceProvider.GetRequiredService<ILogger<DeliveryAggregator>>());
                while (await aggregator.RunOnceAsync(stoppingToken) > 0)
                {
                }

                var cutoff = time.GetUtcNow().AddDays(-options.OutboxRetentionDays);
                await db.Outbox.Where(o => o.PublishedAtUtc != null && o.PublishedAtUtc < cutoff).ExecuteDeleteAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogCycleFailed(log, ex);
            }

            await Task.Delay(TimeSpan.FromSeconds(options.AggregationIntervalSeconds), time, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Aggregation cycle failed")]
    private static partial void LogCycleFailed(ILogger logger, Exception ex);
}

/// <summary>Once per day, after <see cref="WorkerOptions.ReconciliationHourUtc"/>, reconciles yesterday with every enabled partner.</summary>
public sealed partial class ReconciliationHostedService(IServiceScopeFactory scopes, WorkerOptions options, TimeProvider time, ILogger<ReconciliationHostedService> log) : BackgroundService
{
    private DateOnly? _lastRunFor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = time.GetUtcNow();
            var yesterday = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-1);
            if (now.Hour >= options.ReconciliationHourUtc && _lastRunFor != yesterday)
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var job = new ReconciliationJob(
                        scope.ServiceProvider.GetRequiredService<TypriteDbContext>(),
                        scope.ServiceProvider.GetRequiredService<Typrite.Infrastructure.Partners.IAdPartner>(),
                        time,
                        scope.ServiceProvider.GetRequiredService<ILogger<ReconciliationJob>>());
                    await job.RunAsync(yesterday, yesterday, options.ReconciliationTolerance, stoppingToken);
                    _lastRunFor = yesterday;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogReconciliationFailed(log, ex, yesterday);
                }
            }

            await Task.Delay(TimeSpan.FromMinutes(10), time, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Reconciliation failed for {Day}")]
    private static partial void LogReconciliationFailed(ILogger logger, Exception ex, DateOnly day);
}

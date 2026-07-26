using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NotificationService.Data;
using NotificationService.Domain;

namespace NotificationService.Services;

/// <summary>
/// Deletes notifications that have been delivered and are older than the retention window.
/// </summary>
/// <remarks>
/// Rows whose realtime event has not been published yet are never touched: this table doubles
/// as the delivery outbox, and a null RealtimePublishedAt marks an item still owed to a user.
/// Removing those would silently drop an undelivered notification, so they are counted and
/// reported instead.
/// </remarks>
public sealed class NotificationRetentionService(
    IServiceScopeFactory scopeFactory,
    IOptions<NotificationRetentionOptions> options,
    TimeProvider timeProvider,
    ILogger<NotificationRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Notification retention is disabled; the table will grow without bound.");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Clamp(settings.SweepIntervalMinutes, 1, 24 * 60));
        using var timer = new PeriodicTimer(interval);

        // Sweep once at startup so a long-running deployment does not wait a full interval.
        do
        {
            try
            {
                await SweepAsync(settings, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Notification retention sweep failed; it will run again next interval.");
            }
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    private async Task SweepAsync(NotificationRetentionOptions settings, CancellationToken cancellationToken)
    {
        var retentionDays = Math.Clamp(settings.RetentionDays, 1, 3_650);
        var batchSize = Math.Clamp(settings.BatchSize, 1, 10_000);
        var maxBatches = Math.Clamp(settings.MaxBatchesPerSweep, 1, 1_000);
        var cutoff = timeProvider.GetUtcNow().AddDays(-retentionDays);

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();

        var deleted = 0;
        for (var batch = 0; batch < maxBatches; batch++)
        {
            var ids = await dbContext.Notifications
                .Where(item => item.CreatedAt < cutoff && item.RealtimePublishedAt != null)
                .OrderBy(item => item.Id)
                .Take(batchSize)
                .Select(item => item.Id)
                .ToListAsync(cancellationToken);
            if (ids.Count == 0)
            {
                break;
            }

            // Only the keys were read, and deletion goes through stub entities so this stays
            // one DELETE per batch without materialising rows — and without depending on
            // ExecuteDelete, which the providers used in tests cannot translate for this model.
            dbContext.Notifications.RemoveRange(ids.Select(id => new Notification { Id = id }));
            try
            {
                deleted += await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // A row went away between the read and the delete; nothing to reconcile.
                dbContext.ChangeTracker.Clear();
            }
        }

        if (deleted > 0)
        {
            logger.LogInformation(
                "Notification retention removed {Deleted} delivered notifications older than {RetentionDays} days.",
                deleted,
                retentionDays);
        }

        var undelivered = await dbContext.Notifications
            .CountAsync(item => item.CreatedAt < cutoff && item.RealtimePublishedAt == null, cancellationToken);
        if (undelivered > 0)
        {
            logger.LogWarning(
                "{Undelivered} notifications older than {RetentionDays} days have never been delivered and were kept.",
                undelivered,
                retentionDays);
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}

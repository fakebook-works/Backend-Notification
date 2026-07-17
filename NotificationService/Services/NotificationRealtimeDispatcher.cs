using HotChocolate.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NotificationService.Data;

namespace NotificationService.Services;

public sealed class NotificationRealtimeDispatcher(
    IServiceScopeFactory scopeFactory,
    ITopicEventSender eventSender,
    IOptions<NotificationDeliveryOptions> options,
    TimeProvider timeProvider,
    ILogger<NotificationRealtimeDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollMilliseconds = options.Value.PollMilliseconds;
        var maxIdlePollMilliseconds = options.Value.MaxIdlePollMilliseconds;
        var idlePollMilliseconds = pollMilliseconds;

        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;
            try
            {
                processed = await DispatchBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Notification realtime dispatcher failed to process a batch.");
            }

            if (processed == 0)
            {
                await Task.Delay(idlePollMilliseconds, stoppingToken);
                idlePollMilliseconds = Math.Min(maxIdlePollMilliseconds, idlePollMilliseconds * 2);
            }
            else
            {
                idlePollMilliseconds = pollMilliseconds;
            }
        }
    }

    internal async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var now = timeProvider.GetUtcNow();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var notifications = await dbContext.Notifications
            .FromSqlInterpolated(
                $"""
                SELECT *
                FROM notification
                WHERE realtime_published_at IS NULL
                  AND (next_publish_attempt_at IS NULL OR next_publish_attempt_at <= {now})
                ORDER BY created_at, id
                FOR UPDATE SKIP LOCKED
                LIMIT {options.Value.BatchSize}
                """)
            .ToListAsync(cancellationToken);

        foreach (var notification in notifications)
        {
            try
            {
                await eventSender.SendAsync(
                    NotificationTopics.ForReceiver(notification.ReceiverId),
                    notification,
                    cancellationToken);
                notification.RealtimePublishedAt = timeProvider.GetUtcNow();
                notification.LastPublishError = null;
                notification.NextPublishAttemptAt = null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                notification.PublishAttemptCount++;
                notification.LastPublishError = Truncate(exception.Message, 2_000);
                notification.NextPublishAttemptAt = timeProvider.GetUtcNow().AddSeconds(
                    Math.Min(60, Math.Pow(2, Math.Min(notification.PublishAttemptCount, 6))));
                logger.LogWarning(
                    exception,
                    "Could not publish notification {NotificationId}; retry {AttemptCount} scheduled.",
                    notification.Id,
                    notification.PublishAttemptCount);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return notifications.Count;
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}

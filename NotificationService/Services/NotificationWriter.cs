using HotChocolate.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NotificationService.Data;
using NotificationService.Domain;

namespace NotificationService.Services;

public interface INotificationWriter
{
    Task<NotificationCreateResult> CreateAsync(
        CreateNotificationCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken);
}

public sealed class NotificationWriter(
    NotificationDbContext dbContext,
    ISnowflakeIdGenerator idGenerator,
    ITopicEventSender eventSender,
    TimeProvider timeProvider) : INotificationWriter
{
    public async Task<NotificationCreateResult> CreateAsync(
        CreateNotificationCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.Notifications
            .AsNoTracking()
            .SingleOrDefaultAsync(notification => notification.IdempotencyKey == idempotencyKey, cancellationToken);

        if (existing is not null)
        {
            EnsureSameCommand(existing, command);
            return new NotificationCreateResult(existing, WasCreated: false);
        }

        var notification = new Notification
        {
            Id = idGenerator.NextId(),
            CreatorId = command.CreatorId,
            ReceiverId = command.ReceiverId,
            ActionType = command.ActionType,
            ObjectId = command.ObjectId,
            CreatedAt = timeProvider.GetUtcNow(),
            IsRead = false,
            IdempotencyKey = idempotencyKey
        };

        dbContext.Notifications.Add(notification);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsIdempotencyConflict(exception))
        {
            dbContext.ChangeTracker.Clear();
            existing = await dbContext.Notifications
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.IdempotencyKey == idempotencyKey, cancellationToken);

            if (existing is null)
            {
                throw;
            }

            EnsureSameCommand(existing, command);
            return new NotificationCreateResult(existing, WasCreated: false);
        }

        await eventSender.SendAsync(NotificationTopics.ForReceiver(notification.ReceiverId), notification, cancellationToken);

        return new NotificationCreateResult(notification, WasCreated: true);
    }

    private static bool IsIdempotencyConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static void EnsureSameCommand(Notification existing, CreateNotificationCommand command)
    {
        if (existing.CreatorId != command.CreatorId ||
            existing.ReceiverId != command.ReceiverId ||
            existing.ActionType != command.ActionType ||
            existing.ObjectId != command.ObjectId)
        {
            throw new IdempotencyConflictException();
        }
    }
}

public sealed record CreateNotificationCommand(
    long CreatorId,
    long ReceiverId,
    NotificationActionType ActionType,
    long ObjectId);

public sealed record NotificationCreateResult(Notification Notification, bool WasCreated);

public sealed class IdempotencyConflictException : Exception
{
    public IdempotencyConflictException()
        : base("The Idempotency-Key was already used with a different notification payload.")
    {
    }
}

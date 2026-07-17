using Microsoft.EntityFrameworkCore;
using NotificationService.Data;
using DomainNotification = NotificationService.Domain.Notification;

namespace NotificationService.GraphQL;

/// <summary>
/// EF Core implementation of the GraphQL application boundary. Every lookup
/// includes receiver ownership in SQL so a notification ID alone is never
/// sufficient to read or mutate another user's notification.
/// </summary>
public sealed class EfNotificationGraphqlService(NotificationDbContext dbContext)
    : INotificationGraphqlService
{
    public async Task<NotificationPage> GetNotificationsAsync(
        long receiverId,
        int first,
        NotificationCursor? after,
        bool unreadOnly,
        CancellationToken cancellationToken)
    {
        EnsurePositive(receiverId, nameof(receiverId));
        EnsurePositive(first, nameof(first));

        var unreadCount = await dbContext.Notifications
            .AsNoTracking()
            .CountAsync(notification =>
                notification.ReceiverId == receiverId && !notification.IsRead,
                cancellationToken);

        IQueryable<DomainNotification> query = dbContext.Notifications
            .AsNoTracking()
            .Where(notification => notification.ReceiverId == receiverId);

        if (unreadOnly)
        {
            query = query.Where(notification => !notification.IsRead);
        }

        if (after is { } cursor)
        {
            query = query.Where(notification =>
                notification.CreatedAt < cursor.CreatedAt ||
                (notification.CreatedAt == cursor.CreatedAt && notification.Id < cursor.Id));
        }

        var records = await query
            .OrderByDescending(notification => notification.CreatedAt)
            .ThenByDescending(notification => notification.Id)
            .Take(checked(first + 1))
            .ToListAsync(cancellationToken);

        var hasNextPage = records.Count > first;

        if (hasNextPage)
        {
            records.RemoveAt(first);
        }

        var notifications = records
            .Select(NotificationGraphqlMapper.ToGraphQl)
            .ToArray();

        return new NotificationPage(notifications, hasNextPage, unreadCount);
    }

    public async Task<Notification?> GetByIdForReceiverAsync(
        long notificationId,
        long receiverId,
        CancellationToken cancellationToken)
    {
        EnsurePositive(notificationId, nameof(notificationId));
        EnsurePositive(receiverId, nameof(receiverId));

        var notification = await dbContext.Notifications
            .AsNoTracking()
            .SingleOrDefaultAsync(item =>
                item.Id == notificationId && item.ReceiverId == receiverId,
                cancellationToken);

        return notification is null ? null : NotificationGraphqlMapper.ToGraphQl(notification);
    }

    public async Task<Notification?> MarkNotificationReadAsync(
        long notificationId,
        long receiverId,
        CancellationToken cancellationToken)
    {
        EnsurePositive(notificationId, nameof(notificationId));
        EnsurePositive(receiverId, nameof(receiverId));

        var notification = await dbContext.Notifications.SingleOrDefaultAsync(item =>
            item.Id == notificationId && item.ReceiverId == receiverId,
            cancellationToken);

        if (notification is null)
        {
            // Do not distinguish a foreign notification from a missing one.
            return null;
        }

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return NotificationGraphqlMapper.ToGraphQl(notification);
    }

    public Task<int> MarkAllNotificationsReadAsync(
        long receiverId,
        CancellationToken cancellationToken)
    {
        EnsurePositive(receiverId, nameof(receiverId));

        return dbContext.Notifications
            .Where(notification => notification.ReceiverId == receiverId && !notification.IsRead)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(notification => notification.IsRead, true),
                cancellationToken);
    }

    private static void EnsurePositive(long value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "The identifier must be positive.");
        }
    }
}

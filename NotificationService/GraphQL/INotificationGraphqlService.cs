namespace NotificationService.GraphQL;

/// <summary>
/// Application boundary used by the GraphQL schema. Its implementation owns
/// Entity Framework queries, receiver ownership checks, and state changes.
/// </summary>
public interface INotificationGraphqlService
{
    /// <summary>
    /// Returns a feed ordered by createdAt descending, then id descending. When
    /// <paramref name="after"/> is supplied, it is exclusive.
    /// </summary>
    Task<NotificationPage> GetNotificationsAsync(
        long receiverId,
        int first,
        NotificationCursor? after,
        bool unreadOnly,
        CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves an entity only if it belongs to <paramref name="receiverId"/>.
    /// </summary>
    Task<Notification?> GetByIdForReceiverAsync(
        long notificationId,
        long receiverId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks a notification read only if it belongs to the receiver. Return
    /// null for both an absent and an unauthorized notification.
    /// </summary>
    Task<Notification?> MarkNotificationReadAsync(
        long notificationId,
        long receiverId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks every unread notification for the receiver as read and returns
    /// the number whose state changed.
    /// </summary>
    Task<int> MarkAllNotificationsReadAsync(
        long receiverId,
        CancellationToken cancellationToken);
}

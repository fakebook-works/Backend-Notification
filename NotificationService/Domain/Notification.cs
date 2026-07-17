namespace NotificationService.Domain;

/// <summary>
/// A notification addressed to one user as the result of a social action.
/// </summary>
public sealed class Notification
{
    /// <summary>
    /// Application-generated Snowflake identifier.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// The user who performed the social action.
    /// </summary>
    public long CreatorId { get; set; }

    /// <summary>
    /// The user who receives this notification.
    /// </summary>
    public long ReceiverId { get; set; }

    public NotificationActionType ActionType { get; set; }

    /// <summary>
    /// Identifier of the target social object (post, comment, and so on).
    /// </summary>
    public long ObjectId { get; set; }

    /// <summary>
    /// UTC timestamp at which the notification was committed.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    public bool IsRead { get; set; }

    /// <summary>
    /// Caller-supplied key used to make internal create requests idempotent.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>
    /// Set only after the realtime event has been accepted by the subscription provider.
    /// A null value makes this row a durable transactional outbox item.
    /// </summary>
    public DateTimeOffset? RealtimePublishedAt { get; set; }

    public int PublishAttemptCount { get; set; }

    public DateTimeOffset? NextPublishAttemptAt { get; set; }

    public string? LastPublishError { get; set; }
}

using DomainNotification = NotificationService.Domain.Notification;
using DomainNotificationActionType = NotificationService.Domain.NotificationActionType;

namespace NotificationService.GraphQL;

/// <summary>
/// Maps the persistence entity used by the REST writer and subscription event
/// bus to the public GraphQL entity without exposing idempotency metadata.
/// </summary>
public static class NotificationGraphqlMapper
{
    public static Notification ToGraphQl(DomainNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        return new Notification(
            notification.Id,
            notification.CreatorId,
            notification.ReceiverId,
            MapAction(notification.ActionType),
            notification.ObjectId,
            notification.CreatedAt,
            notification.IsRead);
    }

    private static NotificationAction MapAction(DomainNotificationActionType actionType)
        => actionType switch
        {
            DomainNotificationActionType.Like => NotificationAction.Like,
            DomainNotificationActionType.Comment => NotificationAction.Comment,
            DomainNotificationActionType.Tag => NotificationAction.Tag,
            DomainNotificationActionType.Mention => NotificationAction.Mention,
            DomainNotificationActionType.FriendRequest => NotificationAction.FriendRequest,
            DomainNotificationActionType.FriendAccept => NotificationAction.FriendAccept,
            DomainNotificationActionType.GroupInvite => NotificationAction.GroupInvite,
            DomainNotificationActionType.GroupJoinRequest => NotificationAction.GroupJoinRequest,
            DomainNotificationActionType.GroupJoinAccepted => NotificationAction.GroupJoinAccepted,
            DomainNotificationActionType.Share => NotificationAction.Share,
            _ => throw new ArgumentOutOfRangeException(
                nameof(actionType),
                actionType,
                "Unknown notification action type.")
        };
}

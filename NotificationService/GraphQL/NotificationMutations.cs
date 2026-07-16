using HotChocolate;
using HotChocolate.Types;
using NotificationService.Security;

namespace NotificationService.GraphQL;

[GraphQLName("Mutation")]
public sealed class NotificationMutations
{
    /// <summary>
    /// Marks one of the authenticated Gateway user's notifications as read.
    /// A missing or foreign notification returns null so its existence is not
    /// disclosed to another user.
    /// </summary>
    [GraphQLName("markNotificationRead")]
    public Task<Notification?> MarkNotificationReadAsync(
        [ID] long id,
        [Service] INotificationGraphqlService notifications,
        [Service] ICurrentGatewayUser currentGatewayUser,
        CancellationToken cancellationToken)
        => notifications.MarkNotificationReadAsync(
            id,
            currentGatewayUser.GetRequiredUserId(),
            cancellationToken);

    /// <summary>
    /// Marks all unread notifications belonging to the authenticated Gateway
    /// user as read and returns the count updated.
    /// </summary>
    [GraphQLName("markAllNotificationsRead")]
    public Task<int> MarkAllNotificationsReadAsync(
        [Service] INotificationGraphqlService notifications,
        [Service] ICurrentGatewayUser currentGatewayUser,
        CancellationToken cancellationToken)
        => notifications.MarkAllNotificationsReadAsync(
            currentGatewayUser.GetRequiredUserId(),
            cancellationToken);
}

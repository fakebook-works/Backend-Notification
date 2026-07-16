using HotChocolate;
using NotificationService.Security;

namespace NotificationService.GraphQL;

[GraphQLName("Query")]
public sealed class NotificationQueries
{
    /// <summary>
    /// Gets the authenticated Gateway user's notifications, newest first.
    /// receiverId is intentionally not a GraphQL argument.
    /// </summary>
    [GraphQLName("notifications")]
    public async Task<NotificationConnection> GetNotificationsAsync(
        int? first,
        string? after,
        [Service] INotificationGraphqlService notifications,
        [Service] ICurrentGatewayUser currentGatewayUser,
        CancellationToken cancellationToken)
    {
        var page = await notifications.GetNotificationsAsync(
            currentGatewayUser.GetRequiredUserId(),
            NotificationPaging.GetPageSize(first),
            NotificationPaging.GetAfterCursor(after),
            cancellationToken);

        return page.ToConnection();
    }
}

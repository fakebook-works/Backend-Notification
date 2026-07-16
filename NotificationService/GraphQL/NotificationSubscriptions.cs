using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Subscriptions;
using DomainNotification = NotificationService.Domain.Notification;
using NotificationService.Security;
using NotificationService.Services;

namespace NotificationService.GraphQL;

[GraphQLName("Subscription")]
public sealed class NotificationSubscriptions
{
    /// <summary>
    /// Subscribes only to the authenticated Gateway user's per-recipient
    /// topic. The client cannot choose a receiver ID.
    /// </summary>
    public ValueTask<ISourceStream<DomainNotification>> SubscribeToNotificationCreatedAsync(
        [Service] ITopicEventReceiver receiver,
        [Service] ICurrentGatewayUser currentGatewayUser,
        CancellationToken cancellationToken)
        => receiver.SubscribeAsync<DomainNotification>(
            NotificationTopics.ForReceiver(currentGatewayUser.GetRequiredUserId()),
            cancellationToken);

    [Subscribe(With = nameof(SubscribeToNotificationCreatedAsync))]
    [GraphQLName("notificationCreated")]
    public Notification OnNotificationCreated([EventMessage] DomainNotification notification)
        => NotificationGraphqlMapper.ToGraphQl(notification);
}

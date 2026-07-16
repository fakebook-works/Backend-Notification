using HotChocolate;
using HotChocolate.ApolloFederation;
using HotChocolate.ApolloFederation.Resolvers;
using HotChocolate.ApolloFederation.Types;
using HotChocolate.Types;
using NotificationService.Security;

namespace NotificationService.GraphQL;

/// <summary>
/// GraphQL representation of the persisted notification action enum. The
/// application-layer adapter maps the Domain enum to this output enum.
/// </summary>
[GraphQLName("NotificationActionType")]
public enum NotificationAction
{
    [GraphQLName("LIKE")]
    Like = 0,

    [GraphQLName("COMMENT")]
    Comment = 1,

    [GraphQLName("TAG")]
    Tag = 2,

    [GraphQLName("MENTION")]
    Mention = 3,

    [GraphQLName("FRIEND_REQUEST")]
    FriendRequest = 4,

    [GraphQLName("FRIEND_ACCEPT")]
    FriendAccept = 5,

    [GraphQLName("GROUP_INVITE")]
    GroupInvite = 6,

    [GraphQLName("GROUP_JOIN_REQUEST")]
    GroupJoinRequest = 7,

    [GraphQLName("GROUP_JOIN_ACCEPTED")]
    GroupJoinAccepted = 8,

    [GraphQLName("SHARE")]
    Share = 9
}

/// <summary>
/// The Notification entity owned by this Apollo Federation subgraph.
/// </summary>
[GraphQLName("Notification")]
public sealed class Notification
{
    public Notification(
        long id,
        long creatorId,
        long receiverId,
        NotificationAction actionType,
        long objectId,
        DateTimeOffset createdAt,
        bool isRead)
    {
        Id = id;
        CreatorId = creatorId;
        ReceiverId = receiverId;
        ActionType = actionType;
        ObjectId = objectId;
        CreatedAt = createdAt;
        IsRead = isRead;
    }

    [ID]
    [Key]
    public long Id { get; }

    [ID]
    public long CreatorId { get; }

    [ID]
    public long ReceiverId { get; }

    public NotificationAction ActionType { get; }

    [ID]
    public long ObjectId { get; }

    public DateTimeOffset CreatedAt { get; }

    public bool IsRead { get; }

    /// <summary>
    /// Resolves Federation _entities references without allowing one Gateway
    /// user to resolve another user's notification.
    /// </summary>
    [ReferenceResolver]
    public static Task<Notification?> ResolveReferenceAsync(
        long id,
        [Service] INotificationGraphqlService notifications,
        [Service] ICurrentGatewayUser currentGatewayUser,
        CancellationToken cancellationToken)
        => notifications.GetByIdForReceiverAsync(
            id,
            currentGatewayUser.GetRequiredUserId(),
            cancellationToken);
}

[GraphQLName("NotificationEdge")]
public sealed class NotificationEdge(string cursor, Notification node)
{
    public string Cursor { get; } = cursor;

    public Notification Node { get; } = node;
}

[GraphQLName("NotificationPageInfo")]
public sealed class NotificationPageInfo(bool hasNextPage, string? endCursor)
{
    public bool HasNextPage { get; } = hasNextPage;

    public string? EndCursor { get; } = endCursor;
}

/// <summary>
/// Cursor-based notification feed. unreadCount always reflects the current
/// user, regardless of the selected page.
/// </summary>
[GraphQLName("NotificationConnection")]
public sealed class NotificationConnection
{
    public NotificationConnection(
        IReadOnlyList<NotificationEdge> edges,
        NotificationPageInfo pageInfo,
        int unreadCount)
    {
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(pageInfo);

        if (unreadCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unreadCount));
        }

        Edges = edges;
        Nodes = edges.Select(edge => edge.Node).ToArray();
        PageInfo = pageInfo;
        UnreadCount = unreadCount;
    }

    public IReadOnlyList<NotificationEdge> Edges { get; }

    public IReadOnlyList<Notification> Nodes { get; }

    public NotificationPageInfo PageInfo { get; }

    public int UnreadCount { get; }
}

/// <summary>
/// Read-model page returned by the application adapter and converted to the
/// GraphQL connection by the query resolver.
/// </summary>
public sealed class NotificationPage
{
    public NotificationPage(
        IReadOnlyList<Notification> items,
        bool hasNextPage,
        int unreadCount)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (unreadCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unreadCount));
        }

        Items = items;
        HasNextPage = hasNextPage;
        UnreadCount = unreadCount;
    }

    public IReadOnlyList<Notification> Items { get; }

    public bool HasNextPage { get; }

    public int UnreadCount { get; }

    public NotificationConnection ToConnection()
    {
        var edges = Items
            .Select(notification => new NotificationEdge(
                NotificationCursor.Encode(notification.CreatedAt, notification.Id),
                notification))
            .ToArray();

        var endCursor = edges.Length == 0 ? null : edges[^1].Cursor;

        return new NotificationConnection(
            edges,
            new NotificationPageInfo(HasNextPage, endCursor),
            UnreadCount);
    }
}

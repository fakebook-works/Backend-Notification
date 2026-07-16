namespace NotificationService.Domain;

/// <summary>
/// The social action that caused a notification to be created.
/// The numeric values are persisted and are part of the internal API contract.
/// </summary>
public enum NotificationActionType : short
{
    Like = 0,
    Comment = 1,
    Tag = 2,
    Mention = 3,
    FriendRequest = 4,
    FriendAccept = 5,
    GroupInvite = 6,
    GroupJoinRequest = 7,
    GroupJoinAccepted = 8,
    Share = 9
}

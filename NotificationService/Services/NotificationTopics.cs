namespace NotificationService.Services;

public static class NotificationTopics
{
    public static string ForReceiver(long receiverId) => $"notification-created:{receiverId}";
}

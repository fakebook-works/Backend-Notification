namespace NotificationService.Services;

public sealed class NotificationDeliveryOptions
{
    public const string SectionName = "NotificationDelivery";

    public int PollMilliseconds { get; set; } = 250;

    public int MaxIdlePollMilliseconds { get; set; } = 2_000;

    public int BatchSize { get; set; } = 50;
}

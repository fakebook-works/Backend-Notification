namespace NotificationService.Tests.TestSupport;

internal sealed class MutableTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public MutableTimeProvider(DateTimeOffset initialUtcNow)
    {
        _utcNow = initialUtcNow.ToUniversalTime();
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void SetUtcNow(DateTimeOffset utcNow)
    {
        _utcNow = utcNow.ToUniversalTime();
    }
}

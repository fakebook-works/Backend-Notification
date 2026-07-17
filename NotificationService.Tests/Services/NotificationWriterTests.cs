using Microsoft.EntityFrameworkCore;
using NotificationService.Data;
using NotificationService.Domain;
using NotificationService.Services;
using NotificationService.Tests.TestSupport;

namespace NotificationService.Tests.Services;

public sealed class NotificationWriterTests
{
    [Fact]
    public async Task Create_persists_notification_as_pending_realtime_outbox_and_replays_idempotently()
    {
        await using var dbContext = new NotificationDbContext(
            new DbContextOptionsBuilder<NotificationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options);
        var now = DateTimeOffset.Parse("2026-07-16T01:00:00Z");
        var writer = new NotificationWriter(
            dbContext,
            new FixedSnowflakeIdGenerator(7001),
            new MutableTimeProvider(now));
        var command = new CreateNotificationCommand(10, 20, NotificationActionType.Comment, 30);

        var created = await writer.CreateAsync(command, "event-1", CancellationToken.None);
        var replay = await writer.CreateAsync(command, "event-1", CancellationToken.None);

        Assert.True(created.WasCreated);
        Assert.False(replay.WasCreated);
        Assert.Equal(created.Notification.Id, replay.Notification.Id);
        var persisted = await dbContext.Notifications.SingleAsync();
        Assert.Null(persisted.RealtimePublishedAt);
        Assert.Null(persisted.NextPublishAttemptAt);
        Assert.Null(persisted.LastPublishError);
        Assert.Equal(0, persisted.PublishAttemptCount);
    }

    [Fact]
    public void Realtime_outbox_migration_is_discoverable()
    {
        using var dbContext = new NotificationDbContext(
            new DbContextOptionsBuilder<NotificationDbContext>()
                .UseNpgsql("Host=localhost;Database=fakebook;Username=postgres;Password=unused")
                .Options);

        Assert.Contains("20260716003000_AddNotificationRealtimeOutbox", dbContext.Database.GetMigrations());
    }

    private sealed class FixedSnowflakeIdGenerator(long id) : ISnowflakeIdGenerator
    {
        public long NextId() => id;
    }
}

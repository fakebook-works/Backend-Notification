using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NotificationService.Data;
using NotificationService.Domain;
using NotificationService.Services;
using NotificationService.Tests.TestSupport;

namespace NotificationService.Tests.Services;

/// <summary>
/// The notification table gains a row for every like, comment, follow and mention in the
/// whole system, and nothing ever deleted one, so it and its four indexes grew without
/// limit on a database shared by every service.
/// </summary>
public sealed class NotificationRetentionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly string _database = Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Deletes_delivered_notifications_past_the_retention_window()
    {
        await SeedAsync(
            Notification(1, Now.AddDays(-200), delivered: true),
            Notification(2, Now.AddDays(-91), delivered: true),
            Notification(3, Now.AddDays(-89), delivered: true),
            Notification(4, Now, delivered: true));

        await RunSweepAsync(expectedRemaining: 2);

        Assert.Equal([3L, 4L], await RemainingIdsAsync());
    }

    [Fact]
    public async Task Keeps_notifications_that_were_never_delivered()
    {
        // A null RealtimePublishedAt marks an item still owed to a user: this table doubles
        // as the delivery outbox, so deleting one would silently drop a notification.
        await SeedAsync(
            Notification(1, Now.AddDays(-200), delivered: false),
            Notification(2, Now.AddDays(-200), delivered: true));

        await RunSweepAsync(expectedRemaining: 1);

        Assert.Equal([1L], await RemainingIdsAsync());
    }

    [Fact]
    public async Task Removes_everything_eligible_across_several_batches()
    {
        await SeedAsync(StaleNotifications(25));

        await RunSweepAsync(expectedRemaining: 0, batchSize: 4);

        Assert.Empty(await RemainingIdsAsync());
    }

    [Fact]
    public async Task Stops_after_the_batch_ceiling_rather_than_running_unbounded()
    {
        await SeedAsync(StaleNotifications(25));

        // Two batches of four; the rest waits for the next sweep so one pass stays bounded.
        await RunSweepAsync(expectedRemaining: 17, batchSize: 4, maxBatches: 2);

        Assert.Equal(17, (await RemainingIdsAsync()).Count);
    }

    [Fact]
    public async Task Does_nothing_when_disabled()
    {
        await SeedAsync(Notification(1, Now.AddDays(-200), delivered: true));

        await RunSweepAsync(expectedRemaining: 1, enabled: false);

        Assert.Equal([1L], await RemainingIdsAsync());
    }

    private async Task RunSweepAsync(
        int expectedRemaining,
        bool enabled = true,
        int batchSize = 500,
        int maxBatches = 40)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => CreateContext());
        await using var provider = services.BuildServiceProvider();

        var service = new NotificationRetentionService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new NotificationRetentionOptions
            {
                Enabled = enabled,
                RetentionDays = 90,
                BatchSize = batchSize,
                MaxBatchesPerSweep = maxBatches,
                SweepIntervalMinutes = 1_440
            }),
            new MutableTimeProvider(Now),
            NullLogger<NotificationRetentionService>.Instance);

        using var cancellation = new CancellationTokenSource();
        await service.StartAsync(cancellation.Token);

        // One sweep runs immediately, then the timer waits out the interval. Poll for the
        // outcome rather than guessing how long that first pass takes, and keep waiting a
        // moment after it settles so an over-deleting sweep would still be caught.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && (await RemainingIdsAsync()).Count != expectedRemaining)
        {
            await Task.Delay(25, CancellationToken.None);
        }
        await Task.Delay(100, CancellationToken.None);

        await cancellation.CancelAsync();
        await service.StopAsync(CancellationToken.None);
    }

    private async Task SeedAsync(params Notification[] notifications)
    {
        await using var context = CreateContext();
        context.Notifications.AddRange(notifications);
        await context.SaveChangesAsync();
    }

    private async Task<IReadOnlyList<long>> RemainingIdsAsync()
    {
        await using var context = CreateContext();
        return await context.Notifications.OrderBy(item => item.Id).Select(item => item.Id).ToListAsync();
    }

    private NotificationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<NotificationDbContext>()
            .UseInMemoryDatabase(_database)
            .Options);

    private static Notification[] StaleNotifications(int count) => Enumerable
        .Range(1, count)
        .Select(index => Notification(index, Now.AddDays(-120), delivered: true))
        .ToArray();

    private static Notification Notification(long id, DateTimeOffset createdAt, bool delivered) => new()
    {
        Id = id,
        CreatorId = 1,
        ReceiverId = 2,
        ActionType = NotificationActionType.Like,
        ObjectId = 3,
        CreatedAt = createdAt,
        IsRead = false,
        IdempotencyKey = $"key-{id}",
        RealtimePublishedAt = delivered ? createdAt : null
    };
}

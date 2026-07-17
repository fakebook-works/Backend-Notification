using Microsoft.EntityFrameworkCore;
using NotificationService.Data;
using NotificationService.Domain;
using NotificationService.GraphQL;
using DomainNotification = NotificationService.Domain.Notification;

namespace NotificationService.Tests.GraphQL;

public sealed class EfNotificationGraphqlServiceTests
{
    [Fact]
    public async Task Feed_is_receiver_scoped_cursor_paginated_and_reports_total_unread_count()
    {
        await using var dbContext = CreateDbContext();
        var baseTime = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
        dbContext.Notifications.AddRange(
            CreateNotification(100, receiverId: 10, baseTime.AddMinutes(-1), isRead: false),
            CreateNotification(101, receiverId: 10, baseTime, isRead: false),
            CreateNotification(102, receiverId: 10, baseTime.AddMinutes(-2), isRead: true),
            CreateNotification(200, receiverId: 20, baseTime.AddMinutes(1), isRead: false));
        await dbContext.SaveChangesAsync();

        var service = new EfNotificationGraphqlService(dbContext);

        var firstPage = await service.GetNotificationsAsync(10, first: 1, after: null, unreadOnly: false, CancellationToken.None);

        Assert.Equal(2, firstPage.UnreadCount);
        Assert.True(firstPage.HasNextPage);
        Assert.Single(firstPage.Items);
        Assert.Equal(101, firstPage.Items[0].Id);

        var secondPage = await service.GetNotificationsAsync(
            10,
            first: 2,
            new NotificationCursor(firstPage.Items[0].CreatedAt, firstPage.Items[0].Id),
            unreadOnly: false,
            CancellationToken.None);

        Assert.False(secondPage.HasNextPage);
        Assert.Equal([100L, 102L], secondPage.Items.Select(notification => notification.Id));
    }

    [Fact]
    public async Task Feed_can_filter_unread_rows_before_cursor_pagination()
    {
        await using var dbContext = CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        dbContext.Notifications.AddRange(
            CreateNotification(100, receiverId: 10, now, isRead: true),
            CreateNotification(101, receiverId: 10, now.AddMinutes(-1), isRead: false),
            CreateNotification(102, receiverId: 10, now.AddMinutes(-2), isRead: false));
        await dbContext.SaveChangesAsync();

        var page = await new EfNotificationGraphqlService(dbContext)
            .GetNotificationsAsync(10, first: 10, after: null, unreadOnly: true, CancellationToken.None);

        Assert.Equal(2, page.UnreadCount);
        Assert.Equal([101L, 102L], page.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task Read_mutation_cannot_access_another_receivers_notification()
    {
        await using var dbContext = CreateDbContext();
        var notification = CreateNotification(100, receiverId: 10, DateTimeOffset.UtcNow, isRead: false);
        dbContext.Notifications.Add(notification);
        await dbContext.SaveChangesAsync();
        var service = new EfNotificationGraphqlService(dbContext);

        var foreignRead = await service.MarkNotificationReadAsync(100, receiverId: 20, CancellationToken.None);
        var ownerRead = await service.MarkNotificationReadAsync(100, receiverId: 10, CancellationToken.None);

        Assert.Null(foreignRead);
        Assert.NotNull(ownerRead);
        Assert.True(ownerRead.IsRead);
        Assert.True((await dbContext.Notifications.SingleAsync(item => item.Id == 100)).IsRead);
    }

    private static NotificationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new NotificationDbContext(options);
    }

    private static DomainNotification CreateNotification(
        long id,
        long receiverId,
        DateTimeOffset createdAt,
        bool isRead) => new()
    {
        Id = id,
        CreatorId = 1,
        ReceiverId = receiverId,
        ActionType = NotificationActionType.Like,
        ObjectId = id * 10,
        CreatedAt = createdAt,
        IsRead = isRead,
        IdempotencyKey = $"event-{id}"
    };
}

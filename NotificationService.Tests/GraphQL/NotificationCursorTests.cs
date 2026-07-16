using NotificationService.GraphQL;

namespace NotificationService.Tests.GraphQL;

public sealed class NotificationCursorTests
{
    [Fact]
    public void Cursor_round_trips_a_utc_timestamp_and_a_long_id()
    {
        var createdAt = new DateTimeOffset(2026, 7, 14, 15, 30, 0, TimeSpan.Zero);
        var encoded = NotificationCursor.Encode(createdAt, 9_223_372_036_854L);

        var parsed = NotificationCursor.TryDecode(encoded, out var cursor);

        Assert.True(parsed);
        Assert.Equal(createdAt, cursor.CreatedAt);
        Assert.Equal(9_223_372_036_854L, cursor.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-cursor")]
    [InlineData("dGVzdA")]
    public void Cursor_rejects_malformed_input(string input)
    {
        Assert.False(NotificationCursor.TryDecode(input, out _));
    }
}

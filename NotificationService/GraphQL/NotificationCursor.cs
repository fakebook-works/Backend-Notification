using System.Globalization;
using System.Text;

namespace NotificationService.GraphQL;

/// <summary>
/// Stable, opaque cursor for the feed's descending (createdAt, id) order.
/// The persistence adapter must apply both fields as its seek boundary.
/// </summary>
public readonly record struct NotificationCursor(DateTimeOffset CreatedAt, long Id)
{
    public static string Encode(DateTimeOffset createdAt, long id)
    {
        if (id <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "Notification IDs must be positive.");
        }

        var payload = string.Create(
            CultureInfo.InvariantCulture,
            $"{createdAt.UtcDateTime.Ticks}:{id}");
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));

        return base64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool TryDecode(string? encoded, out NotificationCursor cursor)
    {
        cursor = default;

        if (string.IsNullOrWhiteSpace(encoded))
        {
            return false;
        }

        try
        {
            var base64 = encoded.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');

            var payload = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            var separatorIndex = payload.IndexOf(':');

            if (separatorIndex <= 0 || separatorIndex != payload.LastIndexOf(':'))
            {
                return false;
            }

            var ticksText = payload[..separatorIndex];
            var idText = payload[(separatorIndex + 1)..];

            if (!long.TryParse(ticksText, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ||
                !long.TryParse(idText, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ||
                id <= 0)
            {
                return false;
            }

            var createdAt = new DateTimeOffset(new DateTime(ticks, DateTimeKind.Utc));
            cursor = new NotificationCursor(createdAt, id);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

internal static class NotificationPaging
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    public static int GetPageSize(int? first)
    {
        var pageSize = first ?? DefaultPageSize;

        if (pageSize is < 1 or > MaxPageSize)
        {
            throw GraphQlErrors.BadUserInput($"first must be between 1 and {MaxPageSize}.");
        }

        return pageSize;
    }

    public static NotificationCursor? GetAfterCursor(string? after)
    {
        if (after is null)
        {
            return null;
        }

        if (!NotificationCursor.TryDecode(after, out var cursor))
        {
            throw GraphQlErrors.BadUserInput("after must be a valid notification cursor.");
        }

        return cursor;
    }
}

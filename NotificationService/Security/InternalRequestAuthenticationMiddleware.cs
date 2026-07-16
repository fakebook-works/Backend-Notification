using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using Microsoft.Extensions.Options;

namespace NotificationService.Security;

public sealed class InternalRequestAuthenticationMiddleware(RequestDelegate next)
{
    public const string GatewayUserIdItemKey = "TrustedGatewayUserId";

    public async Task InvokeAsync(HttpContext context, IOptions<InternalAuthenticationOptions> options)
    {
        if (context.Request.Path.StartsWithSegments("/graphql", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryAuthenticateGateway(context, options.Value, out var userId))
            {
                await WriteUnauthorizedAsync(context, "A valid Gateway secret and trusted user id are required.");
                return;
            }

            context.Items[GatewayUserIdItemKey] = userId;
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                authenticationType: "GatewayHeaders"));
        }
        else if (context.Request.Path.StartsWithSegments("/internal", StringComparison.OrdinalIgnoreCase))
        {
            if (!HasMatchingSecret(context.Request.Headers["X-Internal-NotificationService-Secret"],
                    options.Value.NotificationServiceSecret))
            {
                await WriteUnauthorizedAsync(context, "A valid internal NotificationService secret is required.");
                return;
            }
        }

        await next(context);
    }

    private static bool TryAuthenticateGateway(
        HttpContext context,
        InternalAuthenticationOptions options,
        out long userId)
    {
        userId = 0;

        var userIdHeader = context.Request.Headers["X-User-Id"];

        return HasMatchingSecret(context.Request.Headers["X-Gateway-Secret"], options.GatewaySecret) &&
               userIdHeader.Count == 1 &&
               long.TryParse(
                   userIdHeader[0],
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out userId) &&
               userId > 0;
    }

    private static bool HasMatchingSecret(Microsoft.Extensions.Primitives.StringValues presentedSecret, string expectedSecret)
    {
        if (string.IsNullOrWhiteSpace(expectedSecret) || presentedSecret.Count != 1)
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(expectedSecret);
        var actual = Encoding.UTF8.GetBytes(presentedSecret[0]!);

        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static Task WriteUnauthorizedAsync(HttpContext context, string detail)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return context.Response.WriteAsJsonAsync(new { error = "unauthorized", detail });
    }
}

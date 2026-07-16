using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NotificationService.Security;

namespace NotificationService.Tests.Security;

public sealed class InternalRequestAuthenticationMiddlewareTests
{
    private static readonly InternalAuthenticationOptions Options = new()
    {
        GatewaySecret = "gateway-test-secret",
        NotificationServiceSecret = "social-graph-test-secret"
    };

    [Fact]
    public async Task GraphQl_request_with_valid_gateway_headers_sets_the_trusted_user()
    {
        var called = false;
        var middleware = new InternalRequestAuthenticationMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });
        var context = NewContext("/graphql");
        context.Request.Headers["X-Gateway-Secret"] = Options.GatewaySecret;
        context.Request.Headers["X-User-Id"] = "42";

        await middleware.InvokeAsync(context, Microsoft.Extensions.Options.Options.Create(Options));

        Assert.True(called);
        Assert.Equal(42L, context.Items[InternalRequestAuthenticationMiddleware.GatewayUserIdItemKey]);
        Assert.Equal("42", context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);
    }

    [Fact]
    public async Task GraphQl_request_with_missing_secret_is_rejected_before_the_next_handler()
    {
        var called = false;
        var middleware = new InternalRequestAuthenticationMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });
        var context = NewContext("/graphql");
        context.Request.Headers["X-User-Id"] = "42";

        await middleware.InvokeAsync(context, Microsoft.Extensions.Options.Options.Create(Options));

        Assert.False(called);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task Internal_rest_request_requires_the_notification_service_secret()
    {
        var called = false;
        var middleware = new InternalRequestAuthenticationMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });
        var context = NewContext("/internal/notifications");
        context.Request.Headers["X-Internal-NotificationService-Secret"] = Options.NotificationServiceSecret;

        await middleware.InvokeAsync(context, Microsoft.Extensions.Options.Options.Create(Options));

        Assert.True(called);
    }

    private static DefaultHttpContext NewContext(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }
}

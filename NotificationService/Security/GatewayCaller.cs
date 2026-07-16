namespace NotificationService.Security;

public interface ICurrentGatewayUser
{
    long GetRequiredUserId();
}

public sealed class CurrentGatewayUser(IHttpContextAccessor httpContextAccessor) : ICurrentGatewayUser
{
    public long GetRequiredUserId()
    {
        var context = httpContextAccessor.HttpContext;

        if (context?.Items.TryGetValue(InternalRequestAuthenticationMiddleware.GatewayUserIdItemKey, out var value) == true &&
            value is long userId &&
            userId > 0)
        {
            return userId;
        }

        throw new InvalidOperationException("The GraphQL request does not have a trusted Gateway user.");
    }
}

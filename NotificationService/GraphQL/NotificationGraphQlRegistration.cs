using HotChocolate.ApolloFederation;
using HotChocolate.Execution.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NotificationService.Security;

namespace NotificationService.GraphQL;

/// <summary>
/// Host-registration helpers. The host supplies the concrete
/// INotificationGraphqlService and selects the subscription transport provider.
/// </summary>
public static class NotificationGraphQlRegistration
{
    public static IServiceCollection AddNotificationGraphQlServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentGatewayUser, CurrentGatewayUser>();
        services.AddScoped<INotificationGraphqlService, EfNotificationGraphqlService>();

        return services;
    }

    public static IRequestExecutorBuilder AddNotificationGraphQl(this IRequestExecutorBuilder graphQl)
    {
        ArgumentNullException.ThrowIfNull(graphQl);

        return graphQl
            .AddApolloFederation(FederationVersion.Federation10)
            .AddQueryType<NotificationQueries>()
            .AddMutationType<NotificationMutations>()
            .AddSubscriptionType<NotificationSubscriptions>()
            .AddType<Notification>();
    }
}

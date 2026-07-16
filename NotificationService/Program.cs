using HotChocolate.Subscriptions;
using NotificationService.Api;
using NotificationService.Data;
using NotificationService.GraphQL;
using NotificationService.Security;
using NotificationService.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var app = Program.BuildApplication(builder);
app.Run();

public partial class Program
{
    /// <summary>
    /// Builds the application pipeline. Kept separate from <c>Run</c> so tests
    /// can host the same secured GraphQL and REST endpoints in TestServer.
    /// </summary>
    public static WebApplication BuildApplication(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        // Deployment configuration is deliberately required: this service must never
        // start with an implicit database, Snowflake node, or shared internal secret.
        builder.Services.AddNotificationPersistence(builder.Configuration);
        builder.Services
            .AddOptions<InternalAuthenticationOptions>()
            .BindConfiguration(InternalAuthenticationOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(
                options =>
                    !string.IsNullOrWhiteSpace(options.GatewaySecret) &&
                    !string.IsNullOrWhiteSpace(options.NotificationServiceSecret),
                "InternalAuthentication secrets must not be empty or whitespace.")
            .ValidateOnStart();

        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddScoped<INotificationWriter, NotificationWriter>();

        builder.Services.AddNotificationGraphQlServices();
        builder.Services
            .AddGraphQLServer()
            .AddInMemorySubscriptions()
            .AddNotificationGraphQl();

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseHttpsRedirection();
        app.UseWebSockets();
        app.UseMiddleware<InternalRequestAuthenticationMiddleware>();

        app.MapInternalNotificationEndpoints();
        app.MapGraphQL("/graphql");
        app.MapGet("/health/live", () => Results.Ok(new { status = "ok", service = "Notification" }));
        app.MapGet("/health/ready", async (
            NotificationDbContext dbContext,
            CancellationToken cancellationToken) =>
            await dbContext.Database.CanConnectAsync(cancellationToken)
                ? Results.Ok(new { status = "ready", service = "Notification" })
                : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

        return app;
    }
}

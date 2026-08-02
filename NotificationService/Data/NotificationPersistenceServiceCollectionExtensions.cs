using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace NotificationService.Data;

public static class NotificationPersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers PostgreSQL persistence and the application-wide Snowflake generator.
    /// </summary>
    public static IServiceCollection AddNotificationPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("NotificationDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:NotificationDb must be configured.");
        }

        services.AddDbContext<NotificationDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                postgres => postgres.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    "notification")));

        services.AddOptions<SnowflakeOptions>()
            .BindConfiguration(SnowflakeOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SnowflakeOptions>, SnowflakeOptionsValidator>();
        services.AddSingleton<ISnowflakeIdGenerator, SnowflakeIdGenerator>();

        return services;
    }

    /// <summary>
    /// Applies the compiled, versioned EF migrations before request-processing
    /// workers start. Automatic migration is the default; deployments can opt out
    /// with <c>Database:ApplyMigrationsOnStartup=false</c> when migrations are run
    /// as a separate release job.
    /// </summary>
    public static IServiceCollection AddNotificationDatabaseMigrations(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.GetValue("Database:ApplyMigrationsOnStartup", true))
        {
            services.AddHostedService<NotificationMigrationHostedService>();
        }

        return services;
    }
}

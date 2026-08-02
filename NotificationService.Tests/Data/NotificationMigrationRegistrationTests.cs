using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NotificationService.Data;

namespace NotificationService.Tests.Data;

public sealed class NotificationMigrationRegistrationTests
{
    [Fact]
    public void Automatic_migrations_are_registered_by_default()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddNotificationDatabaseMigrations(configuration);

        Assert.Contains(services, IsMigrationHostedService);
    }

    [Fact]
    public void Automatic_migrations_can_be_disabled_for_an_external_release_job()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ApplyMigrationsOnStartup"] = "false"
            })
            .Build();

        services.AddNotificationDatabaseMigrations(configuration);

        Assert.DoesNotContain(services, IsMigrationHostedService);
    }

    private static bool IsMigrationHostedService(ServiceDescriptor descriptor) =>
        descriptor.ServiceType == typeof(IHostedService) &&
        descriptor.ImplementationType == typeof(NotificationMigrationHostedService);
}

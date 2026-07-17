using Microsoft.EntityFrameworkCore;
using System.Data;

namespace NotificationService.Data;

public sealed class NotificationMigrationHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<NotificationMigrationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        logger.LogInformation("Applying Notification database migrations.");
        await EnsureLegacyBaselineAsync(dbContext, cancellationToken);
        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task EnsureLegacyBaselineAsync(
        NotificationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var existsCommand = connection.CreateCommand();
            existsCommand.CommandText = "SELECT to_regclass('notification.notification') IS NOT NULL;";
            var tableExists = Convert.ToBoolean(await existsCommand.ExecuteScalarAsync(cancellationToken));
            if (!tableExists)
            {
                return;
            }

            await using var columnsCommand = connection.CreateCommand();
            columnsCommand.CommandText =
                """
                SELECT count(*)
                FROM information_schema.columns
                WHERE table_schema = 'notification'
                  AND table_name = 'notification'
                  AND column_name IN (
                      'id', 'creator_id', 'receiver_id', 'action_type', 'object_id',
                      'created_at', 'is_read', 'idempotency_key'
                  );
                """;
            var coreColumnCount = Convert.ToInt32(await columnsCommand.ExecuteScalarAsync(cancellationToken));
            if (coreColumnCount != 8)
            {
                throw new InvalidOperationException(
                    "The existing notification.notification table does not match the required baseline schema.");
            }

            await using var baselineCommand = connection.CreateCommand();
            baselineCommand.CommandText =
                """
                CREATE TABLE IF NOT EXISTS notification."__EFMigrationsHistory" (
                    "MigrationId" character varying(150) NOT NULL,
                    "ProductVersion" character varying(32) NOT NULL,
                    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
                );
                INSERT INTO notification."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES ('20260714140000_InitialNotificationSchema', '8.0.11')
                ON CONFLICT ("MigrationId") DO NOTHING;
                """;
            await baselineCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Data;

namespace NotificationService.Data;

public sealed class NotificationMigrationHostedService(
    IConfiguration configuration,
    ILogger<NotificationMigrationHostedService> logger) : IHostedService
{
    // A service-specific session lock serializes legacy baseline adoption as well
    // as EF's versioned migrations across concurrently starting replicas.
    private const long MigrationLockId = 4_609_001_001_001;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("NotificationMigrationDb");
        var usesDedicatedMigrationRole = !string.IsNullOrWhiteSpace(connectionString);
        if (!usesDedicatedMigrationRole)
        {
            connectionString = configuration.GetConnectionString("NotificationDb");
        }
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:NotificationMigrationDb or ConnectionStrings:NotificationDb must be configured.");
        }

        var commandTimeoutSeconds = configuration.GetValue(
            "Database:MigrationCommandTimeoutSeconds",
            300);
        if (commandTimeoutSeconds is < 1 or > 3_600)
        {
            throw new InvalidOperationException(
                "Database:MigrationCommandTimeoutSeconds must be between 1 and 3600.");
        }

        var connectionOptions = new NpgsqlConnectionStringBuilder(connectionString)
        {
            CommandTimeout = commandTimeoutSeconds,
            Enlist = false,
            Multiplexing = false,
            Pooling = false
        };
        var options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseNpgsql(
                connectionOptions.ConnectionString,
                postgres =>
                {
                    postgres.CommandTimeout(commandTimeoutSeconds);
                    postgres.MigrationsHistoryTable(
                        "__EFMigrationsHistory",
                        "notification");
                })
            .Options;

        await using var dbContext = new NotificationDbContext(options);
        var lockAcquired = false;
        try
        {
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
            await ExecuteAdvisoryLockAsync(
                dbContext,
                acquire: true,
                commandTimeoutSeconds,
                cancellationToken);
            lockAcquired = true;
            await EnsureNotificationSchemaAsync(
                dbContext,
                commandTimeoutSeconds,
                cancellationToken);

            logger.LogInformation(
                "Applying Notification database migrations with {MigrationRoleMode} credentials.",
                usesDedicatedMigrationRole ? "dedicated" : "runtime fallback");
            await EnsureLegacyBaselineAsync(
                dbContext,
                commandTimeoutSeconds,
                cancellationToken);
            await dbContext.Database.MigrateAsync(cancellationToken);
            await ValidatePhysicalSchemaAsync(
                dbContext,
                commandTimeoutSeconds,
                cancellationToken);
            logger.LogInformation("Notification database migrations are current.");
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "Notification database migration failed; startup is aborted.");
            throw;
        }
        finally
        {
            if (lockAcquired)
            {
                try
                {
                    await ExecuteAdvisoryLockAsync(
                        dbContext,
                        acquire: false,
                        commandTimeoutSeconds,
                        CancellationToken.None);
                }
                catch (Exception exception)
                {
                    // Closing the PostgreSQL session below also releases a session lock.
                    logger.LogWarning(exception, "Could not explicitly release the Notification migration lock.");
                }
            }

            if (dbContext.Database.GetDbConnection().State != ConnectionState.Closed)
            {
                await dbContext.Database.CloseConnectionAsync();
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task EnsureNotificationSchemaAsync(
        NotificationDbContext dbContext,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandTimeout = commandTimeoutSeconds;
        command.CommandText =
            """
            CREATE SCHEMA IF NOT EXISTS notification;
            SET search_path TO notification, public;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteAdvisoryLockAsync(
        NotificationDbContext dbContext,
        bool acquire,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandTimeout = commandTimeoutSeconds;
        command.CommandText = acquire
            ? $"SELECT pg_advisory_lock({MigrationLockId});"
            : $"SELECT pg_advisory_unlock({MigrationLockId});";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ValidatePhysicalSchemaAsync(
        NotificationDbContext dbContext,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        await using (var columnsCommand = connection.CreateCommand())
        {
            columnsCommand.CommandTimeout = commandTimeoutSeconds;
            columnsCommand.CommandText =
                """
                WITH expected(column_name, data_type, is_nullable, character_maximum_length) AS (
                    VALUES
                        ('idempotency_key', 'character varying', 'NO', 128),
                        ('last_publish_error', 'character varying', 'YES', 2000),
                        ('id', 'bigint', 'NO', NULL),
                        ('creator_id', 'bigint', 'NO', NULL),
                        ('receiver_id', 'bigint', 'NO', NULL),
                        ('action_type', 'smallint', 'NO', NULL),
                        ('object_id', 'bigint', 'NO', NULL),
                        ('created_at', 'timestamp with time zone', 'NO', NULL),
                        ('is_read', 'boolean', 'NO', NULL),
                        ('next_publish_attempt_at', 'timestamp with time zone', 'YES', NULL),
                        ('publish_attempt_count', 'integer', 'NO', NULL),
                        ('realtime_published_at', 'timestamp with time zone', 'YES', NULL)
                )
                SELECT expected.column_name,
                       expected.data_type,
                       expected.is_nullable,
                       expected.character_maximum_length,
                       columns.data_type,
                       columns.is_nullable,
                       columns.character_maximum_length
                FROM expected
                LEFT JOIN information_schema.columns AS columns
                  ON columns.table_schema = 'notification'
                 AND columns.table_name = 'notification'
                 AND columns.column_name = expected.column_name
                WHERE columns.column_name IS NULL
                   OR columns.data_type IS DISTINCT FROM expected.data_type
                   OR columns.is_nullable IS DISTINCT FROM expected.is_nullable
                   OR columns.character_maximum_length IS DISTINCT FROM expected.character_maximum_length
                ORDER BY expected.column_name;
                """;

            await using var reader = await columnsCommand.ExecuteReaderAsync(cancellationToken);
            var mismatches = new List<string>();
            while (await reader.ReadAsync(cancellationToken))
            {
                var expectedLength = reader.IsDBNull(3) ? string.Empty : $"({reader.GetInt32(3)})";
                var actual = reader.IsDBNull(4)
                    ? "missing"
                    : $"{reader.GetString(4)}" +
                      (reader.IsDBNull(6) ? string.Empty : $"({reader.GetInt32(6)})") +
                      $" nullable={reader.GetString(5)}";
                mismatches.Add(
                    $"{reader.GetString(0)} expected {reader.GetString(1)}{expectedLength} " +
                    $"nullable={reader.GetString(2)}, found {actual}");
            }

            if (mismatches.Count != 0)
            {
                throw new InvalidOperationException(
                    "The migrated Notification schema is physically incompatible: " +
                    string.Join("; ", mismatches));
            }
        }

        await using var indexCommand = connection.CreateCommand();
        indexCommand.CommandTimeout = commandTimeoutSeconds;
        indexCommand.CommandText =
            """
            SELECT index_record.indisvalid,
                   pg_get_expr(index_record.indpred, index_record.indrelid),
                   index_record.indnkeyatts,
                   pg_get_indexdef(index_record.indexrelid, 1, true),
                   pg_get_indexdef(index_record.indexrelid, 2, true)
            FROM pg_catalog.pg_index AS index_record
            JOIN pg_catalog.pg_class AS index_class
              ON index_class.oid = index_record.indexrelid
            JOIN pg_catalog.pg_class AS table_class
              ON table_class.oid = index_record.indrelid
            JOIN pg_catalog.pg_namespace AS table_namespace
              ON table_namespace.oid = table_class.relnamespace
            WHERE table_namespace.nspname = 'notification'
              AND table_class.relname = 'notification'
              AND index_class.relname = 'ix_notification_pending_realtime';
            """;
        await using var indexReader = await indexCommand.ExecuteReaderAsync(cancellationToken);
        if (!await indexReader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "The migrated Notification schema is missing ix_notification_pending_realtime.");
        }

        var predicate = indexReader.IsDBNull(1) ? string.Empty : indexReader.GetString(1);
        if (!indexReader.GetBoolean(0) ||
            !predicate.Contains("realtime_published_at IS NULL", StringComparison.OrdinalIgnoreCase) ||
            indexReader.GetInt16(2) != 2 ||
            !string.Equals(indexReader.GetString(3), "next_publish_attempt_at", StringComparison.Ordinal) ||
            !string.Equals(indexReader.GetString(4), "created_at", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The migrated Notification pending-realtime index has an invalid definition.");
        }
    }

    private static async Task EnsureLegacyBaselineAsync(
        NotificationDbContext dbContext,
        int commandTimeoutSeconds,
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
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using var existsCommand = connection.CreateCommand();
            existsCommand.CommandTimeout = commandTimeoutSeconds;
            existsCommand.Transaction = transaction;
            existsCommand.CommandText =
                """
                SELECT to_regclass('notification.notification') IS NOT NULL,
                       to_regclass('public.notification') IS NOT NULL;
                """;
            bool canonicalTableExists;
            bool publicTableExists;
            await using (var existsReader = await existsCommand.ExecuteReaderAsync(cancellationToken))
            {
                await existsReader.ReadAsync(cancellationToken);
                canonicalTableExists = existsReader.GetBoolean(0);
                publicTableExists = existsReader.GetBoolean(1);
            }

            if (canonicalTableExists && publicTableExists)
            {
                throw new InvalidOperationException(
                    "Both notification.notification and public.notification exist; automatic adoption is ambiguous.");
            }

            if (!canonicalTableExists && !publicTableExists)
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            if (!canonicalTableExists)
            {
                await using var adoptPublicTableCommand = connection.CreateCommand();
                adoptPublicTableCommand.CommandTimeout = commandTimeoutSeconds;
                adoptPublicTableCommand.Transaction = transaction;
                adoptPublicTableCommand.CommandText =
                    "ALTER TABLE public.notification SET SCHEMA notification;";
                await adoptPublicTableCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var columnsCommand = connection.CreateCommand();
            columnsCommand.CommandTimeout = commandTimeoutSeconds;
            columnsCommand.Transaction = transaction;
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

            await using var realtimeColumnsCommand = connection.CreateCommand();
            realtimeColumnsCommand.CommandTimeout = commandTimeoutSeconds;
            realtimeColumnsCommand.Transaction = transaction;
            realtimeColumnsCommand.CommandText =
                """
                SELECT count(*)
                FROM information_schema.columns
                WHERE table_schema = 'notification'
                  AND table_name = 'notification'
                  AND column_name IN (
                      'last_publish_error', 'next_publish_attempt_at',
                      'publish_attempt_count', 'realtime_published_at'
                  );
                """;
            var realtimeColumnCount = Convert.ToInt32(
                await realtimeColumnsCommand.ExecuteScalarAsync(cancellationToken));

            await using var realtimeIndexCommand = connection.CreateCommand();
            realtimeIndexCommand.CommandTimeout = commandTimeoutSeconds;
            realtimeIndexCommand.Transaction = transaction;
            realtimeIndexCommand.CommandText =
                "SELECT to_regclass('notification.ix_notification_pending_realtime') IS NOT NULL;";
            var realtimeIndexExists = Convert.ToBoolean(
                await realtimeIndexCommand.ExecuteScalarAsync(cancellationToken));
            var hasCompleteRealtimeMigration = realtimeColumnCount == 4 && realtimeIndexExists;
            if ((realtimeColumnCount != 0 && realtimeColumnCount != 4) ||
                (realtimeColumnCount == 0 && realtimeIndexExists) ||
                (realtimeColumnCount == 4 && !realtimeIndexExists))
            {
                throw new InvalidOperationException(
                    "The existing notification.notification table has a partial realtime-outbox migration.");
            }

            await using var baselineCommand = connection.CreateCommand();
            baselineCommand.CommandTimeout = commandTimeoutSeconds;
            baselineCommand.Transaction = transaction;
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

            if (hasCompleteRealtimeMigration)
            {
                await using var realtimeBaselineCommand = connection.CreateCommand();
                realtimeBaselineCommand.CommandTimeout = commandTimeoutSeconds;
                realtimeBaselineCommand.Transaction = transaction;
                realtimeBaselineCommand.CommandText =
                    """
                    INSERT INTO notification."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                    VALUES ('20260716003000_AddNotificationRealtimeOutbox', '10.0.10')
                    ON CONFLICT ("MigrationId") DO NOTHING;
                    """;
                await realtimeBaselineCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
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

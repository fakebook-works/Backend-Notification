using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationService.Data.Migrations;

public partial class InitialNotificationSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "notification");

        migrationBuilder.CreateTable(
            name: "notification",
            schema: "notification",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false),
                creator_id = table.Column<long>(type: "bigint", nullable: false),
                receiver_id = table.Column<long>(type: "bigint", nullable: false),
                action_type = table.Column<short>(type: "smallint", nullable: false),
                object_id = table.Column<long>(type: "bigint", nullable: false),
                created_at = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false,
                    defaultValueSql: "CURRENT_TIMESTAMP"),
                is_read = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_notification", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ux_notification_idempotency_key",
            schema: "notification",
            table: "notification",
            column: "idempotency_key",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_notification_receiver_created_id",
            schema: "notification",
            table: "notification",
            columns: new[] { "receiver_id", "created_at", "id" },
            descending: new[] { false, true, true });

        migrationBuilder.CreateIndex(
            name: "ix_notification_unread_receiver",
            schema: "notification",
            table: "notification",
            column: "receiver_id",
            filter: "is_read = false");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "notification", schema: "notification");
    }
}

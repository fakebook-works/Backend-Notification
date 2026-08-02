using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace NotificationService.Data.Migrations;

[DbContext(typeof(NotificationDbContext))]
[Migration("20260716003000_AddNotificationRealtimeOutbox")]
public partial class AddNotificationRealtimeOutbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "last_publish_error",
            schema: "notification",
            table: "notification",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "next_publish_attempt_at",
            schema: "notification",
            table: "notification",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "publish_attempt_count",
            schema: "notification",
            table: "notification",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "realtime_published_at",
            schema: "notification",
            table: "notification",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_notification_pending_realtime",
            schema: "notification",
            table: "notification",
            columns: new[] { "next_publish_attempt_at", "created_at" },
            filter: "realtime_published_at IS NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_notification_pending_realtime",
            schema: "notification",
            table: "notification");

        migrationBuilder.DropColumn(name: "last_publish_error", schema: "notification", table: "notification");
        migrationBuilder.DropColumn(name: "next_publish_attempt_at", schema: "notification", table: "notification");
        migrationBuilder.DropColumn(name: "publish_attempt_count", schema: "notification", table: "notification");
        migrationBuilder.DropColumn(name: "realtime_published_at", schema: "notification", table: "notification");
    }
}

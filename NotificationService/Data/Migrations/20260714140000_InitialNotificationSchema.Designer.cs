using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using NotificationService.Domain;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NotificationService.Data.Migrations;

[DbContext(typeof(NotificationDbContext))]
[Migration("20260714140000_InitialNotificationSchema")]
partial class InitialNotificationSchema
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder
            .HasAnnotation("ProductVersion", "8.0.11")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

        modelBuilder.Entity("NotificationService.Domain.Notification", b =>
        {
            b.Property<long>("Id")
                .ValueGeneratedNever()
                .HasColumnType("bigint")
                .HasColumnName("id");

            b.Property<NotificationActionType>("ActionType")
                .HasConversion<short>()
                .HasColumnType("smallint")
                .HasColumnName("action_type");

            b.Property<DateTimeOffset>("CreatedAt")
                .ValueGeneratedOnAdd()
                .HasColumnType("timestamp with time zone")
                .HasColumnName("created_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            b.Property<long>("CreatorId")
                .HasColumnType("bigint")
                .HasColumnName("creator_id");

            b.Property<string>("IdempotencyKey")
                .IsRequired()
                .HasMaxLength(128)
                .IsUnicode(false)
                .HasColumnType("character varying(128)")
                .HasColumnName("idempotency_key");

            b.Property<bool>("IsRead")
                .ValueGeneratedOnAdd()
                .HasColumnType("boolean")
                .HasColumnName("is_read")
                .HasDefaultValue(false);

            b.Property<long>("ObjectId")
                .HasColumnType("bigint")
                .HasColumnName("object_id");

            b.Property<long>("ReceiverId")
                .HasColumnType("bigint")
                .HasColumnName("receiver_id");

            b.HasKey("Id");

            b.HasIndex("IdempotencyKey")
                .IsUnique()
                .HasDatabaseName("ux_notification_idempotency_key");

            b.HasIndex("ReceiverId", "CreatedAt", "Id")
                .IsDescending(false, true, true)
                .HasDatabaseName("ix_notification_receiver_created_id");

            b.HasIndex("ReceiverId")
                .HasDatabaseName("ix_notification_unread_receiver")
                .HasFilter("is_read = false");

            b.ToTable("notification", (string)null);
        });
#pragma warning restore 612, 618
    }
}

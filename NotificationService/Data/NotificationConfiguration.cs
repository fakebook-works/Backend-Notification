using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationService.Domain;

namespace NotificationService.Data;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notification");

        builder.HasKey(notification => notification.Id);

        builder.Property(notification => notification.Id)
            .HasColumnName("id")
            .HasColumnType("bigint")
            .ValueGeneratedNever();

        builder.Property(notification => notification.CreatorId)
            .HasColumnName("creator_id")
            .HasColumnType("bigint")
            .IsRequired();

        builder.Property(notification => notification.ReceiverId)
            .HasColumnName("receiver_id")
            .HasColumnType("bigint")
            .IsRequired();

        builder.Property(notification => notification.ActionType)
            .HasColumnName("action_type")
            .HasColumnType("smallint")
            .HasConversion<short>()
            .IsRequired();

        builder.Property(notification => notification.ObjectId)
            .HasColumnName("object_id")
            .HasColumnType("bigint")
            .IsRequired();

        builder.Property(notification => notification.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .ValueGeneratedOnAdd()
            .IsRequired();

        builder.Property(notification => notification.IsRead)
            .HasColumnName("is_read")
            .HasColumnType("boolean")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(notification => notification.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(128)
            .IsUnicode(false)
            .IsRequired();

        builder.HasIndex(notification => notification.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("ux_notification_idempotency_key");

        builder.HasIndex(notification => new { notification.ReceiverId, notification.CreatedAt, notification.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_notification_receiver_created_id");

        builder.HasIndex(notification => notification.ReceiverId)
            .HasDatabaseName("ix_notification_unread_receiver")
            .HasFilter("is_read = false");
    }
}

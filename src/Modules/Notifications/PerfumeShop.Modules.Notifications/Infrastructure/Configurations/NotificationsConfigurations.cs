using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PerfumeShop.Modules.Notifications.Domain;

namespace PerfumeShop.Modules.Notifications.Infrastructure.Configurations;

internal sealed class NotificationTemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> builder)
    {
        builder.ToTable("templates");
        builder.HasKey(t => t.Key);
        builder.Property(t => t.Key).HasMaxLength(50);
        builder.Property(t => t.Channel).HasConversion<string>().HasMaxLength(10);
        builder.Property(t => t.Audience).HasConversion<string>().HasMaxLength(10);
        builder.Property(t => t.Subject).HasMaxLength(NotificationTemplate.SubjectMaxLength);
        builder.Property(t => t.Body).HasMaxLength(NotificationTemplate.EmailMaxLength);
        builder.Property(t => t.UpdatedBy).HasMaxLength(100);
        builder.Property<uint>("xmin").IsRowVersion();
    }
}

internal sealed class NotificationMessageConfiguration : IEntityTypeConfiguration<NotificationMessage>
{
    public void Configure(EntityTypeBuilder<NotificationMessage> builder)
    {
        builder.ToTable("messages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Channel).HasConversion<string>().HasMaxLength(10);
        builder.Property(m => m.Audience).HasConversion<string>().HasMaxLength(10);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(m => m.Recipient).HasMaxLength(200);
        builder.Property(m => m.TemplateKey).HasMaxLength(50);
        builder.Property(m => m.Subject).HasMaxLength(NotificationTemplate.SubjectMaxLength + 200);
        builder.Property(m => m.Body).HasMaxLength(20_000);
        builder.Property(m => m.DedupKey).HasMaxLength(400);
        builder.Property(m => m.SourceId).HasMaxLength(100);
        builder.Property(m => m.OrderNumber).HasMaxLength(20);
        builder.Property(m => m.Encoding).HasMaxLength(10);
        builder.Property(m => m.Provider).HasMaxLength(100);
        builder.Property(m => m.ProviderMessageId).HasMaxLength(200);
        builder.Property(m => m.LastError).HasMaxLength(1000);
        builder.Property(m => m.ResentBy).HasMaxLength(100);
        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasIndex(m => m.DedupKey).IsUnique().HasDatabaseName("ux_messages_dedup_key");
        builder.HasIndex(m => new { m.Status, m.NextAttemptAtUtc }).HasDatabaseName("ix_messages_status_next_attempt_at_utc");
        builder.HasIndex(m => m.CreatedAtUtc).HasDatabaseName("ix_messages_created_at_utc");
        builder.HasIndex(m => m.OrderNumber).HasDatabaseName("ix_messages_order_number");
        builder.HasIndex(m => m.Recipient).HasDatabaseName("ix_messages_recipient");

        builder.HasMany(m => m.AttemptLog).WithOne().HasForeignKey(a => a.MessageId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(m => m.AttemptLog).HasField("_attempts").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(m => m.IsCustomerSms);
    }
}

internal sealed class NotificationAttemptConfiguration : IEntityTypeConfiguration<NotificationAttempt>
{
    public void Configure(EntityTypeBuilder<NotificationAttempt> builder)
    {
        builder.ToTable("attempts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Provider).HasMaxLength(100);
        builder.Property(a => a.ProviderMessageId).HasMaxLength(200);
        builder.Property(a => a.Response).HasMaxLength(2000);
        builder.Property(a => a.Error).HasMaxLength(1000);
        builder.HasIndex(a => new { a.MessageId, a.AttemptedAtUtc }).HasDatabaseName("ix_attempts_message_id_attempted_at_utc");
    }
}

internal sealed class StaffAlertSettingsConfiguration : IEntityTypeConfiguration<StaffAlertSettings>
{
    public void Configure(EntityTypeBuilder<StaffAlertSettings> builder)
    {
        builder.ToTable("staff_alert_settings", t => t.HasCheckConstraint("ck_staff_alert_settings_singleton", "id = 1"));
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Recipients).HasColumnType("text[]");
        builder.Property(s => s.UpdatedBy).HasMaxLength(100);
        builder.Property<uint>("xmin").IsRowVersion();
    }
}

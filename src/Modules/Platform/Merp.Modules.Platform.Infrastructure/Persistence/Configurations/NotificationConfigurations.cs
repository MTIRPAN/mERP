using Merp.Infrastructure.Persistence.Conventions;
using Merp.Modules.Platform.Domain.Notifications;
using Merp.Modules.Platform.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merp.Modules.Platform.Infrastructure.Persistence.Configurations;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever();
        builder.Property(n => n.Type).HasMaxLength(Notification.TypeMaxLength);
        builder.Property(n => n.Link).HasMaxLength(Notification.LinkMaxLength);
        builder.Property(n => n.Args).HasJson();
        builder.HasOne<User>().WithMany().HasForeignKey(n => n.RecipientId).OnDelete(DeleteBehavior.Cascade);

        // The bell: a user's newest notifications and the unread count
        builder.HasIndex(n => new { n.RecipientId, n.CreatedAt });
        builder.HasIndex(n => n.RecipientId).HasFilter("read_at IS NULL").HasDatabaseName("ix_notifications_unread");
    }
}

internal sealed class EmailMessageConfiguration : IEntityTypeConfiguration<EmailMessage>
{
    public void Configure(EntityTypeBuilder<EmailMessage> builder)
    {
        builder.ToTable("email_messages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.NotificationType).HasMaxLength(Notification.TypeMaxLength);
        builder.Property(m => m.To).HasMaxLength(EmailMessage.AddressMaxLength);
        builder.Property(m => m.ToName).HasMaxLength(255);
        builder.Property(m => m.Subject).HasMaxLength(EmailMessage.SubjectMaxLength);
        builder.Property(m => m.LastError).HasMaxLength(EmailMessage.ErrorMaxLength);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(m => m.NextAttemptAt).HasFilter("status = 'Pending'").HasDatabaseName("ix_email_messages_due");
    }
}

internal sealed class NotificationTemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> builder)
    {
        builder.ToTable("notification_templates");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.NotificationType).HasMaxLength(Notification.TypeMaxLength);
        builder.Property(t => t.Culture).HasMaxLength(20);
        builder.Property(t => t.Subject).HasMaxLength(NotificationTemplate.SubjectMaxLength);

        // One template per type (or all types), company (or all) and culture; null counts as a value
        builder.HasIndex(t => new { t.NotificationType, t.CompanyId, t.Culture }).IsUnique().AreNullsDistinct(false);
    }
}

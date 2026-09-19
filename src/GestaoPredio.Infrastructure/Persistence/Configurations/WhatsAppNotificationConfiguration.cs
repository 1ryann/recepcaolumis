using GestaoPredio.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GestaoPredio.Infrastructure.Persistence.Configurations;

public sealed class WhatsAppNotificationConfiguration : IEntityTypeConfiguration<WhatsAppNotification>
{
    public const string TableName = "WhatsAppNotifications";
    public const string IdempotencyIndex = "UX_WhatsAppNotifications_IdempotencyKey";

    public void Configure(EntityTypeBuilder<WhatsAppNotification> entity)
    {
        entity.ToTable(TableName, table =>
        {
            table.HasCheckConstraint("CK_WhatsAppNotifications_Type",
                $"\"Type\" IN ({Quoted(TypeStorage.Values)})");
            table.HasCheckConstraint("CK_WhatsAppNotifications_Recipient",
                $"\"Recipient\" IN ({Quoted(RecipientStorage.Values)})");
            table.HasCheckConstraint("CK_WhatsAppNotifications_Status",
                $"\"Status\" IN ({Quoted(StatusStorage.Values)})");
            table.HasCheckConstraint("CK_WhatsAppNotifications_Attempts", "\"Attempts\" >= 0");
            // A wamid exists exactly from the moment Meta accepted the send.
            table.HasCheckConstraint("CK_WhatsAppNotifications_MessageId",
                "(\"Status\" IN ('ACCEPTED', 'SENT', 'DELIVERED', 'READ') AND \"MessageId\" IS NOT NULL) OR " +
                "(\"Status\" IN ('PENDING', 'PROCESSING', 'SENDING', 'UNCONFIRMED', 'SKIPPED') AND \"MessageId\" IS NULL) OR " +
                "\"Status\" = 'FAILED'");
            // A lease exists exactly while one dispatcher owns the notification.
            table.HasCheckConstraint("CK_WhatsAppNotifications_Lock",
                "(\"Status\" IN ('PROCESSING', 'SENDING')) = (\"LockedUntil\" IS NOT NULL)");
        });
        entity.HasKey(x => x.Id);
        // xmin: a dispatcher whose claim was taken over (or raced by a webhook) cannot overwrite the row.
        entity.Property(x => x.Version).IsRowVersion();
        entity.Property(x => x.Type).HasConversion(Converter(TypeStorage)).HasMaxLength(32).IsRequired();
        entity.Property(x => x.Recipient).HasConversion(Converter(RecipientStorage)).HasMaxLength(16).IsRequired();
        entity.Property(x => x.Status).HasConversion(Converter(StatusStorage)).HasMaxLength(16).IsRequired();
        entity.Property(x => x.IdempotencyKey).HasMaxLength(WhatsAppNotification.IdempotencyKeyMaxLength).IsRequired();
        entity.Property(x => x.MessageId).HasMaxLength(WhatsAppNotification.MessageIdMaxLength);
        entity.Property(x => x.LastErrorCode).HasMaxLength(WhatsAppNotification.ErrorCodeMaxLength);
        entity.Property(x => x.NextAttemptAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.LockedUntil).HasColumnType("timestamp with time zone");
        entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone");

        entity.HasIndex(x => x.IdempotencyKey).IsUnique().HasDatabaseName(IdempotencyIndex);
        // The dispatcher's claim query: due work first.
        entity.HasIndex(x => new { x.Status, x.NextAttemptAt }).HasDatabaseName("IX_WhatsAppNotifications_Status_NextAttemptAt");
        // Webhook status → notification lookup.
        entity.HasIndex(x => x.MessageId).HasDatabaseName("IX_WhatsAppNotifications_MessageId")
            .HasFilter("\"MessageId\" IS NOT NULL");
        entity.HasIndex(x => x.ReservationId).HasDatabaseName("IX_WhatsAppNotifications_ReservationId");
    }

    public static readonly IReadOnlyDictionary<WhatsAppNotificationType, string> TypeStorage =
        new Dictionary<WhatsAppNotificationType, string>
        {
            [WhatsAppNotificationType.ClientCheckedIn] = "CLIENT_CHECKED_IN",
            [WhatsAppNotificationType.ProfessionalCancelled] = "PROFESSIONAL_CANCELLED",
            [WhatsAppNotificationType.ProfessionalDelayed] = "PROFESSIONAL_DELAYED",
            [WhatsAppNotificationType.AppointmentRescheduled] = "APPOINTMENT_RESCHEDULED",
            [WhatsAppNotificationType.AppointmentConfirmed] = "APPOINTMENT_CONFIRMED",
            [WhatsAppNotificationType.AppointmentCancelled] = "APPOINTMENT_CANCELLED",
            [WhatsAppNotificationType.AppointmentReminder] = "APPOINTMENT_REMINDER"
        };

    public static readonly IReadOnlyDictionary<WhatsAppNotificationRecipient, string> RecipientStorage =
        new Dictionary<WhatsAppNotificationRecipient, string>
        {
            [WhatsAppNotificationRecipient.Professional] = "PROFESSIONAL",
            [WhatsAppNotificationRecipient.Customer] = "CUSTOMER"
        };

    public static readonly IReadOnlyDictionary<WhatsAppNotificationStatus, string> StatusStorage =
        new Dictionary<WhatsAppNotificationStatus, string>
        {
            [WhatsAppNotificationStatus.Pending] = "PENDING",
            [WhatsAppNotificationStatus.Processing] = "PROCESSING",
            [WhatsAppNotificationStatus.Accepted] = "ACCEPTED",
            [WhatsAppNotificationStatus.Sent] = "SENT",
            [WhatsAppNotificationStatus.Delivered] = "DELIVERED",
            [WhatsAppNotificationStatus.Read] = "READ",
            [WhatsAppNotificationStatus.Failed] = "FAILED",
            [WhatsAppNotificationStatus.Skipped] = "SKIPPED",
            [WhatsAppNotificationStatus.Sending] = "SENDING",
            [WhatsAppNotificationStatus.Unconfirmed] = "UNCONFIRMED"
        };

    private static ValueConverter<TEnum, string> Converter<TEnum>(IReadOnlyDictionary<TEnum, string> storage)
        where TEnum : struct, Enum
    {
        var reverse = storage.ToDictionary(x => x.Value, x => x.Key);
        return new ValueConverter<TEnum, string>(
            value => storage[value],
            value => reverse[value]);
    }

    private static string Quoted(IEnumerable<string> values) => string.Join(", ", values.Select(x => $"'{x}'"));
}

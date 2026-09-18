using GestaoPredio.Domain.Whatsapp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GestaoPredio.Infrastructure.Persistence.Configurations;

public sealed class WhatsAppMessageConfiguration : IEntityTypeConfiguration<WhatsAppMessage>
{
    public void Configure(EntityTypeBuilder<WhatsAppMessage> entity)
    {
        entity.ToTable("WhatsAppMessages", table =>
        {
            table.HasCheckConstraint("CK_WhatsAppMessages_Direction", "\"Direction\" IN ('OUTBOUND', 'INBOUND')");
            table.HasCheckConstraint("CK_WhatsAppMessages_Status",
                "\"Status\" IN ('ACCEPTED', 'SENT', 'DELIVERED', 'READ', 'FAILED')");
            table.HasCheckConstraint("CK_WhatsAppMessages_FailureFields",
                "(\"Status\" = 'FAILED') OR (\"ErrorCode\" IS NULL AND \"ErrorTitle\" IS NULL AND \"ErrorDetails\" IS NULL)");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.MessageId).HasMaxLength(WhatsAppMessage.MessageIdMaxLength).IsRequired();
        entity.Property(x => x.RecipientPhone).HasMaxLength(16).IsRequired();
        entity.Property(x => x.PhoneNumberId).HasMaxLength(32);
        entity.Property(x => x.WhatsAppBusinessAccountId).HasMaxLength(32);
        entity.Property(x => x.Direction)
            .HasConversion(new ValueConverter<WhatsAppMessageDirection, string>(
                value => DirectionToStorage(value), value => DirectionFromStorage(value)))
            .HasMaxLength(10).IsRequired();
        entity.Property(x => x.MessageType)
            .HasConversion(new ValueConverter<WhatsAppMessageType, string>(
                value => TypeToStorage(value), value => TypeFromStorage(value)))
            .HasMaxLength(10).IsRequired();
        entity.Property(x => x.Status)
            .HasConversion(new ValueConverter<WhatsAppDeliveryStatus, string>(
                value => StatusToStorage(value), value => StatusFromStorage(value)))
            .HasMaxLength(10).IsRequired();
        entity.Property(x => x.ErrorTitle).HasMaxLength(WhatsAppMessage.ErrorTitleMaxLength);
        entity.Property(x => x.ErrorDetails).HasMaxLength(WhatsAppMessage.ErrorDetailsMaxLength);
        entity.Property(x => x.LastStatusAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        entity.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone");
        // The wamid is the provider's identity for the message: it is what makes send/webhook/retries idempotent.
        entity.HasIndex(x => x.MessageId).IsUnique().HasDatabaseName("UX_WhatsAppMessages_MessageId");
        entity.HasIndex(x => new { x.Status, x.LastStatusAt }).HasDatabaseName("IX_WhatsAppMessages_Status_LastStatusAt");
        entity.HasIndex(x => new { x.RecipientPhone, x.CreatedAt }).HasDatabaseName("IX_WhatsAppMessages_Recipient_CreatedAt");
    }

    private static string DirectionToStorage(WhatsAppMessageDirection value) => value switch
    {
        WhatsAppMessageDirection.Outbound => "OUTBOUND",
        WhatsAppMessageDirection.Inbound => "INBOUND",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Invalid WhatsApp message direction.")
    };

    private static WhatsAppMessageDirection DirectionFromStorage(string value) => value switch
    {
        "OUTBOUND" => WhatsAppMessageDirection.Outbound,
        "INBOUND" => WhatsAppMessageDirection.Inbound,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Invalid stored WhatsApp message direction.")
    };

    private static string TypeToStorage(WhatsAppMessageType value) => value switch
    {
        WhatsAppMessageType.Text => "TEXT",
        WhatsAppMessageType.Unknown => "UNKNOWN",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Invalid WhatsApp message type.")
    };

    private static WhatsAppMessageType TypeFromStorage(string value) => value switch
    {
        "TEXT" => WhatsAppMessageType.Text,
        "UNKNOWN" => WhatsAppMessageType.Unknown,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Invalid stored WhatsApp message type.")
    };

    private static string StatusToStorage(WhatsAppDeliveryStatus value) => value switch
    {
        WhatsAppDeliveryStatus.Accepted => "ACCEPTED",
        WhatsAppDeliveryStatus.Sent => "SENT",
        WhatsAppDeliveryStatus.Delivered => "DELIVERED",
        WhatsAppDeliveryStatus.Read => "READ",
        WhatsAppDeliveryStatus.Failed => "FAILED",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Invalid WhatsApp delivery status.")
    };

    private static WhatsAppDeliveryStatus StatusFromStorage(string value) => value switch
    {
        "ACCEPTED" => WhatsAppDeliveryStatus.Accepted,
        "SENT" => WhatsAppDeliveryStatus.Sent,
        "DELIVERED" => WhatsAppDeliveryStatus.Delivered,
        "READ" => WhatsAppDeliveryStatus.Read,
        "FAILED" => WhatsAppDeliveryStatus.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Invalid stored WhatsApp delivery status.")
    };
}

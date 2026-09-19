using GestaoPredio.Domain.Whatsapp;

namespace GestaoPredio.Application.Whatsapp;

/// <summary>
/// One webhook status update, already parsed and free of raw payload or message content. <see cref="CallbackData"/>
/// is the <c>biz_opaque_callback_data</c> this application attached to the send, echoed back by Meta.
/// </summary>
public sealed record WhatsAppStatusUpdate(
    string MessageId,
    WhatsAppDeliveryStatus Status,
    string RecipientId,
    DateTimeOffset ReportedAt,
    string? PhoneNumberId,
    string? WhatsAppBusinessAccountId,
    int? ErrorCode,
    string? ErrorTitle,
    string? ErrorDetails,
    string? CallbackData = null);

/// <summary>
/// Durable record of outbound messages and their delivery status. Implementations must be idempotent across
/// processes: the send response and the webhook may arrive in any order, more than once, on different nodes.
/// </summary>
public interface IWhatsAppMessageStore
{
    /// <summary>Records (or reconciles) the wamid the Cloud API returned for a successful send.</summary>
    Task RecordAcceptedAsync(string messageId, string recipientPhone, string? phoneNumberId,
        WhatsAppMessageType messageType, DateTimeOffset occurredAt, CancellationToken cancellationToken);

    /// <summary>Records a successful free-text send.</summary>
    Task RecordAcceptedAsync(string messageId, string recipientPhone, string? phoneNumberId,
        DateTimeOffset occurredAt, CancellationToken cancellationToken) =>
        RecordAcceptedAsync(messageId, recipientPhone, phoneNumberId, WhatsAppMessageType.Text, occurredAt, cancellationToken);

    /// <summary>
    /// Applies a webhook status, creating the record when the send response has not been stored yet.
    /// Returns false when the update changed nothing (replay, out-of-order status, unknown status).
    /// </summary>
    Task<bool> ApplyStatusAsync(WhatsAppStatusUpdate update, CancellationToken cancellationToken);
}

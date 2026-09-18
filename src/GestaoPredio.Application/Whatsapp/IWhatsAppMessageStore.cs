using GestaoPredio.Domain.Whatsapp;

namespace GestaoPredio.Application.Whatsapp;

/// <summary>One webhook status update, already parsed and free of raw payload or message content.</summary>
public sealed record WhatsAppStatusUpdate(
    string MessageId,
    WhatsAppDeliveryStatus Status,
    string RecipientId,
    DateTimeOffset ReportedAt,
    string? PhoneNumberId,
    string? WhatsAppBusinessAccountId,
    int? ErrorCode,
    string? ErrorTitle,
    string? ErrorDetails);

/// <summary>
/// Durable record of outbound messages and their delivery status. Implementations must be idempotent across
/// processes: the send response and the webhook may arrive in any order, more than once, on different nodes.
/// </summary>
public interface IWhatsAppMessageStore
{
    /// <summary>Records (or reconciles) the wamid the Cloud API returned for a successful send.</summary>
    Task RecordAcceptedAsync(string messageId, string recipientPhone, string? phoneNumberId,
        DateTimeOffset occurredAt, CancellationToken cancellationToken);

    /// <summary>
    /// Applies a webhook status, creating the record when the send response has not been stored yet.
    /// Returns false when the update changed nothing (replay, out-of-order status, unknown status).
    /// </summary>
    Task<bool> ApplyStatusAsync(WhatsAppStatusUpdate update, CancellationToken cancellationToken);
}

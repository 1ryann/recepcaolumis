using GestaoPredio.Domain.Common;

namespace GestaoPredio.Domain.Whatsapp;

public enum WhatsAppMessageDirection
{
    Outbound = 1,
    Inbound = 2
}

public enum WhatsAppMessageType
{
    Unknown = 0,
    Text = 1,
    Template = 2
}

/// <summary>
/// Delivery lifecycle of an outbound message. Accepted is the Cloud API's synchronous acknowledgement (a wamid
/// was returned); Sent/Delivered/Read arrive later through the webhook and only ever move forward. Failed is
/// terminal: a late replay of an earlier status must not overwrite it.
/// </summary>
public enum WhatsAppDeliveryStatus
{
    Unknown = 0,
    Accepted = 1,
    Sent = 2,
    Delivered = 3,
    Read = 4,
    Failed = 5
}

/// <summary>
/// One outbound WhatsApp message and its current delivery status. Deliberately carries no message content,
/// no raw webhook payload and no credential: only identifiers, the recipient, the status and Meta's error fields.
/// </summary>
public sealed class WhatsAppMessage
{
    public const int MessageIdMaxLength = 128;
    public const int ErrorTitleMaxLength = 200;
    public const int ErrorDetailsMaxLength = 500;

    private WhatsAppMessage()
    {
    }

    public Guid Id { get; private set; }
    public string MessageId { get; private set; } = "";
    public string RecipientPhone { get; private set; } = "";
    public string? PhoneNumberId { get; private set; }
    public string? WhatsAppBusinessAccountId { get; private set; }
    public WhatsAppMessageDirection Direction { get; private set; }
    public WhatsAppMessageType MessageType { get; private set; }
    public WhatsAppDeliveryStatus Status { get; private set; }
    public DateTimeOffset LastStatusAt { get; private set; }
    public int? ErrorCode { get; private set; }
    public string? ErrorTitle { get; private set; }
    public string? ErrorDetails { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>The Cloud API accepted the send and returned this wamid; no webhook status has arrived yet.</summary>
    public static WhatsAppMessage CreateAccepted(string messageId, string recipient, string? phoneNumberId,
        DateTimeOffset occurredAt, WhatsAppMessageType messageType = WhatsAppMessageType.Text) =>
        Create(messageId, recipient, phoneNumberId, null, messageType, WhatsAppDeliveryStatus.Accepted,
            occurredAt, null, null, null, occurredAt);

    /// <summary>A webhook status arrived for a message this instance has no send record for (restart, other node).</summary>
    public static WhatsAppMessage CreateFromStatus(string messageId, WhatsAppDeliveryStatus status, string recipient,
        string? phoneNumberId, string? whatsAppBusinessAccountId, DateTimeOffset reportedAt, int? errorCode,
        string? errorTitle, string? errorDetails, DateTimeOffset occurredAt) =>
        Create(messageId, recipient, phoneNumberId, whatsAppBusinessAccountId, WhatsAppMessageType.Unknown,
            status == WhatsAppDeliveryStatus.Unknown ? WhatsAppDeliveryStatus.Accepted : status,
            reportedAt, errorCode, errorTitle, errorDetails, occurredAt);

    private static WhatsAppMessage Create(string messageId, string recipient, string? phoneNumberId,
        string? whatsAppBusinessAccountId, WhatsAppMessageType messageType, WhatsAppDeliveryStatus status,
        DateTimeOffset reportedAt, int? errorCode, string? errorTitle, string? errorDetails, DateTimeOffset occurredAt)
    {
        var id = messageId?.Trim() ?? "";
        if (id.Length is < 1 or > MessageIdMaxLength) throw new ArgumentException("O identificador da mensagem é inválido.", nameof(messageId));
        var phone = NormalizeRecipient(recipient);
        var timestamp = TimestampNormalizer.ToUtcMicroseconds(occurredAt);

        return new WhatsAppMessage
        {
            Id = Guid.NewGuid(),
            MessageId = id,
            RecipientPhone = phone,
            PhoneNumberId = Clean(phoneNumberId, 32),
            WhatsAppBusinessAccountId = Clean(whatsAppBusinessAccountId, 32),
            Direction = WhatsAppMessageDirection.Outbound,
            MessageType = messageType,
            Status = status,
            LastStatusAt = TimestampNormalizer.ToUtcMicroseconds(reportedAt),
            ErrorCode = errorCode,
            ErrorTitle = Clean(errorTitle, ErrorTitleMaxLength),
            ErrorDetails = Clean(errorDetails, ErrorDetailsMaxLength),
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
    }

    /// <summary>
    /// Applies a webhook status. Returns false — without changing anything — when the update would move the
    /// status backwards, repeat the current one, overwrite a failure, or carry an unrecognised status.
    /// </summary>
    public bool ApplyStatus(WhatsAppDeliveryStatus status, DateTimeOffset reportedAt, int? errorCode,
        string? errorTitle, string? errorDetails, DateTimeOffset occurredAt)
    {
        if (status == WhatsAppDeliveryStatus.Unknown || Status == WhatsAppDeliveryStatus.Failed) return false;
        if (status != WhatsAppDeliveryStatus.Failed && Rank(status) <= Rank(Status)) return false;

        Status = status;
        LastStatusAt = TimestampNormalizer.ToUtcMicroseconds(reportedAt);
        if (status == WhatsAppDeliveryStatus.Failed)
        {
            ErrorCode = errorCode;
            ErrorTitle = Clean(errorTitle, ErrorTitleMaxLength);
            ErrorDetails = Clean(errorDetails, ErrorDetailsMaxLength);
        }

        UpdatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
        return true;
    }

    /// <summary>
    /// Fills in what only the send response knows (recipient in E.164, phone number id, message type) on a record
    /// the webhook created first. Never touches the status. Returns false when nothing changed.
    /// </summary>
    public bool ReconcileAccepted(string recipient, string? phoneNumberId, WhatsAppMessageType messageType,
        DateTimeOffset occurredAt)
    {
        var phone = NormalizeRecipient(recipient);
        var normalizedPhoneNumberId = Clean(phoneNumberId, 32);
        var changed = false;

        if (!string.Equals(RecipientPhone, phone, StringComparison.Ordinal)) { RecipientPhone = phone; changed = true; }
        if (normalizedPhoneNumberId is not null && !string.Equals(PhoneNumberId, normalizedPhoneNumberId, StringComparison.Ordinal))
        {
            PhoneNumberId = normalizedPhoneNumberId;
            changed = true;
        }
        if (messageType != WhatsAppMessageType.Unknown && MessageType != messageType) { MessageType = messageType; changed = true; }
        if (changed) UpdatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
        return changed;
    }

    private static int Rank(WhatsAppDeliveryStatus status) => status switch
    {
        WhatsAppDeliveryStatus.Accepted => 1,
        WhatsAppDeliveryStatus.Sent => 2,
        WhatsAppDeliveryStatus.Delivered => 3,
        WhatsAppDeliveryStatus.Read => 4,
        WhatsAppDeliveryStatus.Failed => 5,
        _ => 0
    };

    /// <summary>
    /// The send side supplies E.164 ("+55…"); the webhook supplies bare digits ("55…"). Both are stored as E.164
    /// so the two sources reconcile to the same value.
    /// </summary>
    public static string NormalizeRecipient(string? recipient)
    {
        var value = recipient?.Trim() ?? "";
        if (value.StartsWith('+')) value = value[1..];
        Span<char> digits = stackalloc char[15];
        var length = 0;
        foreach (var character in value)
        {
            if (character is ' ' or '-' or '(' or ')' or '.') continue;
            if (character is < '0' or > '9' || length == digits.Length)
                throw new ArgumentException("O telefone do destinatário é inválido.", nameof(recipient));
            digits[length++] = character;
        }

        if (length is < 8 or > 15) throw new ArgumentException("O telefone do destinatário é inválido.", nameof(recipient));
        return $"+{new string(digits[..length])}";
    }

    private static string? Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}

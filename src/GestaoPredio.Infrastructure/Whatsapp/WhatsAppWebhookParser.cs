using System.Collections.Concurrent;
using System.Text.Json;
using GestaoPredio.Application.Whatsapp;
using GestaoPredio.Domain.Whatsapp;

namespace GestaoPredio.Infrastructure.Whatsapp;

/// <summary>
/// One outbound-message status update from the Cloud API webhook. Carries no message content: only ids,
/// the status, the instant and Meta's error fields when the delivery failed.
/// </summary>
public sealed record WhatsAppStatusEvent(
    string MessageId,
    string RawStatus,
    WhatsAppDeliveryStatus Status,
    string RecipientId,
    DateTimeOffset OccurredAt,
    string? PhoneNumberId,
    string? WhatsAppBusinessAccountId,
    int? ErrorCode,
    string? ErrorTitle,
    string? ErrorDetails)
{
    /// <summary>The recipient is personal data, so logs only ever see the last four digits.</summary>
    public string MaskedRecipient => MaskRecipient(RecipientId);

    public static string MaskRecipient(string? recipient) =>
        recipient is { Length: > 4 } value ? $"***{value[^4..]}" : "***";

    /// <summary>Projection handed to the durable store; only the raw provider status string is dropped.</summary>
    public WhatsAppStatusUpdate ToUpdate() => new(MessageId, Status, RecipientId, OccurredAt, PhoneNumberId,
        WhatsAppBusinessAccountId, ErrorCode, ErrorTitle, ErrorDetails);
}

public sealed record WhatsAppWebhookPayload(
    bool Parsed,
    string? Object,
    int EntryCount,
    int InboundMessageCount,
    IReadOnlyList<WhatsAppStatusEvent> Statuses)
{
    public static WhatsAppWebhookPayload Unparsed { get; } = new(false, null, 0, 0, []);

    // Keeps inbound content out of any accidental ToString()/structured-log rendering.
    public override string ToString() =>
        $"{nameof(WhatsAppWebhookPayload)} {{ Parsed = {Parsed}, EntryCount = {EntryCount}, InboundMessageCount = {InboundMessageCount}, Statuses = {Statuses.Count} }}";
}

/// <summary>
/// Reads the WhatsApp Cloud API webhook body. Pure and allocation-light: it never keeps message text,
/// contact names or the raw payload, and unknown fields are ignored instead of failing the delivery.
/// </summary>
public static class WhatsAppWebhookParser
{
    public static WhatsAppWebhookPayload Parse(ReadOnlySpan<byte> body)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body.ToArray());
        }
        catch (JsonException)
        {
            return WhatsAppWebhookPayload.Unparsed;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return WhatsAppWebhookPayload.Unparsed;

            var payloadObject = root.TryGetProperty("object", out var obj) && obj.ValueKind == JsonValueKind.String
                ? obj.GetString()
                : null;
            var statuses = new List<WhatsAppStatusEvent>();
            int entries = 0, inbound = 0;

            if (root.TryGetProperty("entry", out var entryArray) && entryArray.ValueKind == JsonValueKind.Array)
                foreach (var entry in entryArray.EnumerateArray())
                {
                    entries++;
                    var wabaId = entry.TryGetProperty("id", out var entryId) && entryId.ValueKind == JsonValueKind.String
                        ? entryId.GetString()
                        : null;
                    if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array) continue;

                    foreach (var change in changes.EnumerateArray())
                    {
                        if (!change.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Object) continue;
                        inbound += Count(value, "messages");
                        var phoneNumberId = value.TryGetProperty("metadata", out var metadata) &&
                                            metadata.ValueKind == JsonValueKind.Object &&
                                            metadata.TryGetProperty("phone_number_id", out var phone) &&
                                            phone.ValueKind == JsonValueKind.String
                            ? phone.GetString()
                            : null;

                        if (!value.TryGetProperty("statuses", out var statusArray) || statusArray.ValueKind != JsonValueKind.Array)
                            continue;
                        foreach (var status in statusArray.EnumerateArray())
                            if (Read(status, phoneNumberId, wabaId) is { } parsed)
                                statuses.Add(parsed);
                    }
                }

            return new WhatsAppWebhookPayload(true, payloadObject, entries, inbound, statuses);
        }
    }

    private static WhatsAppStatusEvent? Read(JsonElement status, string? phoneNumberId, string? wabaId)
    {
        if (status.ValueKind != JsonValueKind.Object) return null;
        var id = Text(status, "id");
        var raw = Text(status, "status");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(raw)) return null;

        var occurredAt = long.TryParse(Text(status, "timestamp"), out var seconds) && seconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : DateTimeOffset.UnixEpoch;

        int? errorCode = null;
        string? errorTitle = null, errorDetails = null;
        if (status.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
        {
            var error = errors[0];
            if (error.TryGetProperty("code", out var code) && code.TryGetInt32(out var parsedCode)) errorCode = parsedCode;
            errorTitle = Text(error, "title");
            if (error.TryGetProperty("error_data", out var data) && data.ValueKind == JsonValueKind.Object)
                errorDetails = Text(data, "details");
        }

        return new WhatsAppStatusEvent(id!, raw!, Map(raw!), Text(status, "recipient_id") ?? "", occurredAt,
            phoneNumberId, wabaId, errorCode, errorTitle, errorDetails);
    }

    private static WhatsAppDeliveryStatus Map(string raw) => raw switch
    {
        "sent" => WhatsAppDeliveryStatus.Sent,
        "delivered" => WhatsAppDeliveryStatus.Delivered,
        "read" => WhatsAppDeliveryStatus.Read,
        "failed" => WhatsAppDeliveryStatus.Failed,
        _ => WhatsAppDeliveryStatus.Unknown
    };

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int Count(JsonElement value, string property) =>
        value.TryGetProperty(property, out var array) && array.ValueKind == JsonValueKind.Array ? array.GetArrayLength() : 0;
}

/// <summary>
/// Bounded in-memory log of the most recent status updates, mirroring the existing DemoNotificationRecorder /
/// AccessControlDemoRecorder pattern. Telemetry and tests only: the source of truth is the WhatsAppMessages
/// table behind IWhatsAppMessageStore, which is what keeps idempotency across restarts and multiple instances.
/// </summary>
public sealed class WhatsAppStatusRecorder
{
    public const int Capacity = 200;

    private readonly object gate = new();
    private readonly LinkedList<WhatsAppStatusEvent> recent = new();
    private readonly ConcurrentDictionary<string, byte> seen = new(StringComparer.Ordinal);

    public IReadOnlyList<WhatsAppStatusEvent> Recent
    {
        get { lock (gate) return [.. recent]; }
    }

    /// <summary>Returns false when this exact (message id, status) pair was already recorded (webhook retry).</summary>
    public bool TryRecord(WhatsAppStatusEvent status)
    {
        if (!seen.TryAdd($"{status.MessageId}|{status.RawStatus}", 0)) return false;
        lock (gate)
        {
            recent.AddLast(status);
            while (recent.Count > Capacity)
            {
                var oldest = recent.First!.Value;
                recent.RemoveFirst();
                seen.TryRemove($"{oldest.MessageId}|{oldest.RawStatus}", out _);
            }
        }
        return true;
    }

    public IReadOnlyList<WhatsAppStatusEvent> ForMessage(string messageId)
    {
        lock (gate) return [.. recent.Where(x => string.Equals(x.MessageId, messageId, StringComparison.Ordinal))];
    }

    public void Clear()
    {
        lock (gate)
        {
            recent.Clear();
            seen.Clear();
        }
    }
}

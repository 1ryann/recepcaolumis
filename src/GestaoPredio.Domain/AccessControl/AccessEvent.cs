using GestaoPredio.Domain.Common;

namespace GestaoPredio.Domain.AccessControl;

public enum AccessEventDisposition
{
    /// <summary>Recorded only. The wire format is not yet known, so nothing was acted on.</summary>
    Captured = 0,

    /// <summary>A repeat of an event already recorded. Nothing was acted on a second time.</summary>
    Duplicate = 1,

    /// <summary>The request did not match a known, active device.</summary>
    Rejected = 2
}

/// <summary>
/// One call received from an access controller.
///
/// Idempotency is a requirement, not a precaution: the Bio-T manual documents "transmissão continuada",
/// which replays the events a device queued while offline. Without <see cref="IdempotencyKey"/> a network
/// outage would produce duplicate arrivals and duplicate WhatsApp notices once the link came back.
///
/// Until the integration manual confirms whether the payload carries a unique event id, the key is a hash
/// of the device and the exact bytes received, so an identical replay collapses onto the same row.
/// </summary>
public sealed class AccessEvent
{
    private AccessEvent() { }

    public Guid Id { get; private set; }
    public Guid? DeviceId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public AccessEventDisposition Disposition { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>
    /// The <em>structure</em> of the payload — key names, value kinds and lengths — never the values.
    /// The device can be configured to attach an access photo, and the payload carries whatever identifies
    /// a person, so storing it verbatim would put biometric and personal data in this table for no reason.
    /// The names are what the normalizer needs; the values are not.
    /// </summary>
    public string? PayloadShape { get; private set; }

    public static AccessEvent Capture(Guid? deviceId, string idempotencyKey,
        AccessEventDisposition disposition, string? payloadShape, DateTimeOffset receivedAt)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
            throw new ArgumentException("A chave de idempotência deve ser informada.", nameof(idempotencyKey));
        if (!Enum.IsDefined(disposition)) throw new ArgumentOutOfRangeException(nameof(disposition));
        if (payloadShape?.Length > 4000)
            throw new ArgumentException("A estrutura do payload excede o limite.", nameof(payloadShape));

        return new AccessEvent
        {
            Id = Guid.NewGuid(),
            DeviceId = deviceId,
            IdempotencyKey = idempotencyKey,
            Disposition = disposition,
            PayloadShape = payloadShape,
            ReceivedAt = TimestampNormalizer.ToUtcMicroseconds(receivedAt)
        };
    }
}

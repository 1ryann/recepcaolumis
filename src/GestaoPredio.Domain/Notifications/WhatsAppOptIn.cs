using GestaoPredio.Domain.Common;

namespace GestaoPredio.Domain.Notifications;

/// <summary>
/// Whether a person agreed to receive operational WhatsApp messages from LUMIS on their number. Nothing is assumed:
/// a recipient starts as <see cref="NotRecorded"/> and receives nothing until an explicit, recorded opt-in.
/// See docs/operations/whatsapp-consent.md.
/// </summary>
public enum WhatsAppOptInStatus
{
    NotRecorded = 0,
    Granted = 1,
    Revoked = 2
}

/// <summary>Where the decision was captured; together with the wording version it is the proof of the opt-in.</summary>
public enum WhatsAppOptInSource
{
    CustomerRegistration = 1,
    CustomerPortal = 2,
    Totem = 3,
    Reception = 4,
    ProfessionalPortal = 5
}

/// <summary>
/// Current opt-in of one recipient. Operational (transactional) messages only: marketing is out of scope and would
/// need its own, separate consent. Every change is also written to the audit log by the endpoint that made it.
/// </summary>
public readonly record struct WhatsAppOptInState(
    WhatsAppOptInStatus Status,
    DateTimeOffset? ChangedAt,
    WhatsAppOptInSource? Source,
    string? TextVersion)
{
    /// <summary>Identifies the exact opt-in wording shown to the person (docs/operations/whatsapp-consent.md).</summary>
    public const string CurrentTextVersion = "whatsapp-operacional-v1";
    public const int TextVersionMaxLength = 32;

    public static WhatsAppOptInState None => new(WhatsAppOptInStatus.NotRecorded, null, null, null);

    public bool IsGranted => Status == WhatsAppOptInStatus.Granted;

    /// <summary>Null when nothing changes (already granted with the current wording).</summary>
    public WhatsAppOptInState? Grant(WhatsAppOptInSource source, DateTimeOffset occurredAt) =>
        IsGranted && TextVersion == CurrentTextVersion
            ? null
            : new WhatsAppOptInState(WhatsAppOptInStatus.Granted, TimestampNormalizer.ToUtcMicroseconds(occurredAt),
                Defined(source), CurrentTextVersion);

    /// <summary>An objection is always recorded, even without a previous opt-in. Null when already revoked.</summary>
    public WhatsAppOptInState? Revoke(WhatsAppOptInSource source, DateTimeOffset occurredAt) =>
        Status == WhatsAppOptInStatus.Revoked
            ? null
            : new WhatsAppOptInState(WhatsAppOptInStatus.Revoked, TimestampNormalizer.ToUtcMicroseconds(occurredAt),
                Defined(source), null);

    /// <summary>The number changed: an opt-in given for another number does not carry over.</summary>
    public WhatsAppOptInState? ResetForNewNumber(DateTimeOffset occurredAt) =>
        Status == WhatsAppOptInStatus.NotRecorded
            ? null
            : new WhatsAppOptInState(WhatsAppOptInStatus.NotRecorded, TimestampNormalizer.ToUtcMicroseconds(occurredAt), null, null);

    private static WhatsAppOptInSource Defined(WhatsAppOptInSource source) =>
        Enum.IsDefined(source) ? source : throw new ArgumentOutOfRangeException(nameof(source));
}

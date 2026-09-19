using GestaoPredio.Domain.Common;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Visits;
using GestaoPredio.Domain.Whatsapp;

namespace GestaoPredio.Domain.Notifications;

/// <summary>What happened in the business, which decides the template and the recipient.</summary>
public enum WhatsAppNotificationType
{
    ClientCheckedIn = 1,
    ProfessionalCancelled = 2,
    ProfessionalDelayed = 3,
    AppointmentRescheduled = 4,
    AppointmentConfirmed = 5,
    AppointmentCancelled = 6,
    AppointmentReminder = 7
}

public enum WhatsAppNotificationRecipient
{
    Professional = 1,
    Customer = 2
}

/// <summary>
/// Pending → Processing (claimed by one dispatcher) → Accepted (Meta returned a wamid) → Sent → Delivered → Read.
/// Processing goes back to Pending on a transient failure. Failed and Skipped are terminal.
/// </summary>
public enum WhatsAppNotificationStatus
{
    Pending = 1,
    Processing = 2,
    Accepted = 3,
    Sent = 4,
    Delivered = 5,
    Read = 6,
    Failed = 7,
    Skipped = 8
}

/// <summary>
/// Durable outbox row for one operational WhatsApp notification. It is written in the same transaction as the
/// business change that caused it (check-in, cancellation, …) and sent later by the dispatcher, so the business
/// operation never waits on Meta. It stores only identifiers: the recipient's phone, the names and the times are
/// read from the real records when the message is sent, and no message content or credential is kept here.
/// </summary>
public sealed class WhatsAppNotification
{
    public const int IdempotencyKeyMaxLength = 120;
    public const int ErrorCodeMaxLength = 64;
    public const int MessageIdMaxLength = WhatsAppMessage.MessageIdMaxLength;

    private WhatsAppNotification()
    {
    }

    public Guid Id { get; private set; }
    public WhatsAppNotificationType Type { get; private set; }
    public WhatsAppNotificationRecipient Recipient { get; private set; }
    /// <summary>Deterministic and unique: the same business event can never enqueue a second message.</summary>
    public string IdempotencyKey { get; private set; } = "";
    public Guid? ReservationId { get; private set; }
    public Guid? VisitId { get; private set; }
    public Guid? ProfessionalId { get; private set; }
    public Guid? CustomerId { get; private set; }
    public WhatsAppNotificationStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }
    public string? MessageId { get; private set; }
    /// <summary>A stable code (e.g. WHATSAPP_PROVIDER_UNAVAILABLE), never provider free text or a recipient.</summary>
    public string? LastErrorCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsTerminal => Status is WhatsAppNotificationStatus.Failed or WhatsAppNotificationStatus.Skipped;

    public static WhatsAppNotification ClientCheckedIn(Visit visit, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(visit);
        return Create(WhatsAppNotificationType.ClientCheckedIn, WhatsAppNotificationRecipient.Professional,
            $"CHECKIN:{visit.Id}", visit.ReservationId, visit.Id, visit.ProfessionalId, visit.CustomerId, occurredAt);
    }

    /// <summary>
    /// A cancelled reservation's customer notification: <see cref="WhatsAppNotificationType.ProfessionalCancelled"/>
    /// when the professional became unavailable (the message carries a reschedule link), otherwise
    /// <see cref="WhatsAppNotificationType.AppointmentCancelled"/>. Null when there is no customer to notify.
    /// </summary>
    public static WhatsAppNotification? ReservationCancelled(Reservation reservation, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        if (reservation.Status != ReservationStatus.Cancelled)
            throw new InvalidOperationException("Somente uma reserva cancelada gera aviso de cancelamento.");
        if (reservation.CustomerId is null) return null;
        var type = reservation.CancellationReason == ReservationCancellationReason.ProfessionalUnavailable
            ? WhatsAppNotificationType.ProfessionalCancelled
            : WhatsAppNotificationType.AppointmentCancelled;
        return ForCustomer(type, $"CANCEL:{reservation.Id}", reservation, occurredAt);
    }

    /// <summary>Keyed by the replacement reservation, which carries the new date and time.</summary>
    public static WhatsAppNotification? AppointmentRescheduled(Reservation replacement, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (replacement.OriginalReservationId is null)
            throw new InvalidOperationException("Somente uma reserva de reagendamento gera aviso de reagendamento.");
        return replacement.CustomerId is null
            ? null
            : ForCustomer(WhatsAppNotificationType.AppointmentRescheduled, $"RESCHEDULE:{replacement.Id}", replacement, occurredAt);
    }

    public static WhatsAppNotification? AppointmentConfirmed(Reservation reservation, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        return reservation.CustomerId is null
            ? null
            : ForCustomer(WhatsAppNotificationType.AppointmentConfirmed, $"CONFIRM:{reservation.Id}", reservation, occurredAt);
    }

    /// <summary>One row per repeat step (0 = first threshold), so a scheduler cycle can never send the same step twice.</summary>
    public static WhatsAppNotification ProfessionalDelayed(Reservation reservation, int step, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentOutOfRangeException.ThrowIfNegative(step);
        if (reservation.CustomerId is null)
            throw new InvalidOperationException("O aviso de atraso exige um cliente.");
        return ForCustomer(WhatsAppNotificationType.ProfessionalDelayed, DelayKey(reservation.Id, step), reservation, occurredAt);
    }

    public static string DelayKey(Guid reservationId, int step) => $"DELAY:{reservationId}:{step}";

    /// <summary>Taken by exactly one dispatcher until <paramref name="lockedUntil"/>; each claim is one attempt.</summary>
    public void Claim(DateTimeOffset occurredAt, DateTimeOffset lockedUntil)
    {
        var reclaimable = Status == WhatsAppNotificationStatus.Processing && LockedUntil < occurredAt;
        if (Status != WhatsAppNotificationStatus.Pending && !reclaimable)
            throw new InvalidOperationException("A notificação não está disponível para envio.");
        Status = WhatsAppNotificationStatus.Processing;
        LockedUntil = TimestampNormalizer.ToUtcMicroseconds(lockedUntil);
        Attempts++;
        Touch(occurredAt);
    }

    public void MarkAccepted(string messageId, DateTimeOffset occurredAt)
    {
        EnsureProcessing();
        var id = messageId?.Trim() ?? "";
        if (id.Length is < 1 or > MessageIdMaxLength) throw new ArgumentException("O identificador da mensagem é inválido.", nameof(messageId));
        Status = WhatsAppNotificationStatus.Accepted;
        MessageId = id;
        LastErrorCode = null;
        LockedUntil = null;
        Touch(occurredAt);
    }

    public void ScheduleRetry(string errorCode, DateTimeOffset nextAttemptAt, DateTimeOffset occurredAt)
    {
        EnsureProcessing();
        Status = WhatsAppNotificationStatus.Pending;
        LastErrorCode = Code(errorCode);
        NextAttemptAt = TimestampNormalizer.ToUtcMicroseconds(nextAttemptAt);
        LockedUntil = null;
        Touch(occurredAt);
    }

    public void MarkFailed(string errorCode, DateTimeOffset occurredAt) =>
        Finish(WhatsAppNotificationStatus.Failed, errorCode, occurredAt);

    /// <summary>Not sent on purpose: the event became obsolete, expired, or its template is not configured.</summary>
    public void MarkSkipped(string reasonCode, DateTimeOffset occurredAt) =>
        Finish(WhatsAppNotificationStatus.Skipped, reasonCode, occurredAt);

    /// <summary>
    /// Mirrors a webhook status for the accepted message. Forward-only (Accepted → Sent → Delivered → Read);
    /// a delivery failure is terminal. Returns false when nothing changed (replay, out of order, not yet accepted).
    /// </summary>
    public bool ApplyDeliveryStatus(WhatsAppDeliveryStatus status, int? errorCode, DateTimeOffset occurredAt)
    {
        if (Status is < WhatsAppNotificationStatus.Accepted or WhatsAppNotificationStatus.Failed or WhatsAppNotificationStatus.Skipped)
            return false;
        var target = status switch
        {
            WhatsAppDeliveryStatus.Sent => WhatsAppNotificationStatus.Sent,
            WhatsAppDeliveryStatus.Delivered => WhatsAppNotificationStatus.Delivered,
            WhatsAppDeliveryStatus.Read => WhatsAppNotificationStatus.Read,
            WhatsAppDeliveryStatus.Failed => WhatsAppNotificationStatus.Failed,
            _ => (WhatsAppNotificationStatus?)null
        };
        if (target is null) return false;
        if (target != WhatsAppNotificationStatus.Failed && target <= Status) return false;

        Status = target.Value;
        if (target == WhatsAppNotificationStatus.Failed)
            LastErrorCode = Code(errorCode is null ? "WHATSAPP_DELIVERY_FAILED" : $"WHATSAPP_DELIVERY_FAILED:{errorCode}");
        Touch(occurredAt);
        return true;
    }

    private static WhatsAppNotification ForCustomer(WhatsAppNotificationType type, string key, Reservation reservation,
        DateTimeOffset occurredAt) =>
        Create(type, WhatsAppNotificationRecipient.Customer, key, reservation.Id, null, reservation.ProfessionalId,
            reservation.CustomerId, occurredAt);

    private static WhatsAppNotification Create(WhatsAppNotificationType type, WhatsAppNotificationRecipient recipient,
        string key, Guid? reservationId, Guid? visitId, Guid? professionalId, Guid? customerId, DateTimeOffset occurredAt)
    {
        var timestamp = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
        return new WhatsAppNotification
        {
            Id = Guid.NewGuid(),
            Type = type,
            Recipient = recipient,
            IdempotencyKey = key,
            ReservationId = reservationId,
            VisitId = visitId,
            ProfessionalId = professionalId,
            CustomerId = customerId,
            Status = WhatsAppNotificationStatus.Pending,
            Attempts = 0,
            NextAttemptAt = timestamp,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
    }

    private void Finish(WhatsAppNotificationStatus status, string code, DateTimeOffset occurredAt)
    {
        if (IsTerminal) throw new InvalidOperationException("A notificação já foi finalizada.");
        Status = status;
        LastErrorCode = Code(code);
        LockedUntil = null;
        Touch(occurredAt);
    }

    private void EnsureProcessing()
    {
        if (Status != WhatsAppNotificationStatus.Processing)
            throw new InvalidOperationException("A notificação não está em processamento.");
    }

    private void Touch(DateTimeOffset occurredAt) => UpdatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt);

    private static string Code(string code)
    {
        var value = code?.Trim() ?? "";
        if (value.Length == 0) throw new ArgumentException("O código é obrigatório.", nameof(code));
        return value.Length <= ErrorCodeMaxLength ? value : value[..ErrorCodeMaxLength];
    }
}

using GestaoPredio.Domain.Common;

namespace GestaoPredio.Domain.Reservations;

public sealed class Reservation
{
    public static readonly TimeSpan ProfessionalMinimumNotice = TimeSpan.FromHours(1);

    private Reservation()
    {
    }

    public Guid Id { get; private set; }
    public Guid RoomId { get; private set; }
    public Guid ProfessionalId { get; private set; }
    public Guid? CustomerId { get; private set; }
    public Guid? OriginalReservationId { get; private set; }
    public ReservationKind Kind { get; private set; }
    public ReservationStatus Status { get; private set; }
    public DateTimeOffset StartAt { get; private set; }
    public DateTimeOffset EndAt { get; private set; }
    public string RequestedByUserId { get; private set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; private set; }
    public string? DecidedByUserId { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? RejectionReason { get; private set; }
    public ReservationCancellationReason CancellationReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public uint Version { get; private set; }

    public bool BlocksResources => Status == ReservationStatus.Approved && Kind != ReservationKind.Cancellation;

    public static Reservation RequestNew(Guid roomId, Guid professionalId, DateTimeOffset startAt,
        DateTimeOffset endAt, string requestedByUserId, DateTimeOffset occurredAt, Guid? customerId = null)
    {
        EnsureMinimumNotice(startAt, occurredAt);
        return Create(roomId, professionalId, startAt, endAt, requestedByUserId, occurredAt,
            ReservationKind.New, ReservationStatus.Pending, null, customerId);
    }

    public static Reservation CreateApproved(Guid roomId, Guid professionalId, DateTimeOffset startAt,
        DateTimeOffset endAt, string actorUserId, DateTimeOffset occurredAt, Guid? customerId = null)
    {
        var reservation = Create(roomId, professionalId, startAt, endAt, actorUserId, occurredAt,
            ReservationKind.New, ReservationStatus.Approved, null, customerId);
        reservation.DecidedByUserId = actorUserId;
        reservation.DecidedAt = reservation.CreatedAt;
        return reservation;
    }

    public static Reservation RequestReschedule(Reservation original, DateTimeOffset startAt,
        DateTimeOffset endAt, string requestedByUserId, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(original);
        original.EnsureApprovedActualReservation();
        EnsureMinimumNotice(original.StartAt, occurredAt);
        EnsureMinimumNotice(startAt, occurredAt);
        return Create(original.RoomId, original.ProfessionalId, startAt, endAt, requestedByUserId,
            occurredAt, ReservationKind.Reschedule, ReservationStatus.Pending, original.Id, original.CustomerId);
    }

    public static Reservation CreateApprovedReschedule(
        Reservation original,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        string actorUserId,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(original);
        original.EnsureApprovedActualReservation();
        var replacement = Create(original.RoomId, original.ProfessionalId, startAt, endAt, actorUserId,
            occurredAt, ReservationKind.Reschedule, ReservationStatus.Approved, original.Id, original.CustomerId);
        replacement.DecidedByUserId = actorUserId;
        replacement.DecidedAt = replacement.CreatedAt;
        return replacement;
    }

    public static Reservation RequestCancellation(Reservation original, string requestedByUserId,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(original);
        original.EnsureApprovedActualReservation();
        EnsureMinimumNotice(original.StartAt, occurredAt);
        return Create(original.RoomId, original.ProfessionalId, original.StartAt, original.EndAt,
            requestedByUserId, occurredAt, ReservationKind.Cancellation, ReservationStatus.Pending, original.Id, original.CustomerId);
    }

    public void Approve(string actorUserId, DateTimeOffset occurredAt)
    {
        EnsurePending();
        Status = ReservationStatus.Approved;
        SetDecision(actorUserId, occurredAt);
    }

    public void Reject(string reason, string actorUserId, DateTimeOffset occurredAt)
    {
        EnsurePending();
        var normalizedReason = reason?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedReason) || normalizedReason.Length > 500)
            throw new ArgumentException("A recusa exige justificativa válida.", nameof(reason));
        Status = ReservationStatus.Rejected;
        RejectionReason = normalizedReason;
        SetDecision(actorUserId, occurredAt);
    }

    public void Cancel(string actorUserId, DateTimeOffset occurredAt,
        ReservationCancellationReason reason = ReservationCancellationReason.None)
    {
        EnsureApprovedActualReservation();
        Status = ReservationStatus.Cancelled;
        CancellationReason = reason;
        SetDecision(actorUserId, occurredAt);
    }

    public static Reservation CreateApprovedReplacementForIncident(
        Reservation original,
        Guid roomId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        string actorUserId,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (original.Status != ReservationStatus.Cancelled ||
            original.CancellationReason != ReservationCancellationReason.ProfessionalUnavailable)
            throw new InvalidOperationException("A reserva original não foi cancelada por indisponibilidade do profissional.");
        var replacement = Create(roomId, original.ProfessionalId, startAt, endAt, actorUserId, occurredAt,
            ReservationKind.Reschedule, ReservationStatus.Approved, original.Id, original.CustomerId);
        replacement.DecidedByUserId = actorUserId;
        replacement.DecidedAt = replacement.CreatedAt;
        return replacement;
    }

    private static Reservation Create(Guid roomId, Guid professionalId, DateTimeOffset startAt,
        DateTimeOffset endAt, string requestedByUserId, DateTimeOffset occurredAt, ReservationKind kind,
        ReservationStatus status, Guid? originalReservationId, Guid? customerId)
    {
        if (roomId == Guid.Empty) throw new ArgumentException("A sala deve ser informada.", nameof(roomId));
        if (professionalId == Guid.Empty)
            throw new ArgumentException("O profissional deve ser informado.", nameof(professionalId));
        if (string.IsNullOrWhiteSpace(requestedByUserId) || requestedByUserId.Length > 450)
            throw new ArgumentException("O solicitante deve ser informado.", nameof(requestedByUserId));
        var start = TimestampNormalizer.ToUtcMicroseconds(startAt);
        var end = TimestampNormalizer.ToUtcMicroseconds(endAt);
        if (end <= start) throw new ArgumentException("O término deve ser posterior ao início.", nameof(endAt));
        var timestamp = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
        return new Reservation
        {
            Id = Guid.NewGuid(), RoomId = roomId, ProfessionalId = professionalId,
            OriginalReservationId = originalReservationId, CustomerId = customerId, Kind = kind, Status = status,
            StartAt = start, EndAt = end, RequestedByUserId = requestedByUserId,
            RequestedAt = timestamp, CreatedAt = timestamp, UpdatedAt = timestamp
        };
    }

    private static void EnsureMinimumNotice(DateTimeOffset startAt, DateTimeOffset occurredAt)
    {
        var start = TimestampNormalizer.ToUtcMicroseconds(startAt);
        var timestamp = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
        if (start < timestamp.Add(ProfessionalMinimumNotice))
            throw new ArgumentException("A solicitação exige antecedência mínima de uma hora.", nameof(startAt));
    }

    private void SetDecision(string actorUserId, DateTimeOffset occurredAt)
    {
        if (string.IsNullOrWhiteSpace(actorUserId) || actorUserId.Length > 450)
            throw new ArgumentException("O responsável pela decisão deve ser informado.", nameof(actorUserId));
        var timestamp = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
        DecidedByUserId = actorUserId;
        DecidedAt = timestamp;
        UpdatedAt = timestamp;
    }

    private void EnsurePending()
    {
        if (Status != ReservationStatus.Pending)
            throw new InvalidOperationException("A solicitação não está pendente.");
    }

    private void EnsureApprovedActualReservation()
    {
        if (Status != ReservationStatus.Approved || Kind == ReservationKind.Cancellation)
            throw new InvalidOperationException("A reserva não está aprovada.");
    }
}

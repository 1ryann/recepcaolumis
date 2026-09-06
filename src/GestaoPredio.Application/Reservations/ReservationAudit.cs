using GestaoPredio.Domain.Auditing;

namespace GestaoPredio.Application.Reservations;

public static class ReservationAudit
{
    private static readonly HashSet<string> ApprovedActions =
    [
        AuditActions.ReservationCreated,
        AuditActions.ReservationRequested,
        AuditActions.ReservationApproved,
        AuditActions.ReservationRejected,
        AuditActions.ReservationCancelled,
        AuditActions.ReservationRescheduleRequested,
        AuditActions.ReservationCancellationRequested,
        AuditActions.ReservationRescheduled
    ];

    public static AuditEntry CreateSucceeded(
        Guid reservationId,
        string action,
        DateTimeOffset occurredAt,
        string correlationId,
        string? actorUserId,
        string? ipAddress)
    {
        if (reservationId == Guid.Empty)
            throw new ArgumentException("A reserva deve ser informada.", nameof(reservationId));
        if (!ApprovedActions.Contains(action))
            throw new ArgumentException("A ação não pertence a Reservas.", nameof(action));
        if (string.IsNullOrWhiteSpace(correlationId))
            throw new ArgumentException("A correlação deve ser informada.", nameof(correlationId));

        return new AuditEntry
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorUserId,
            IpAddress = ipAddress,
            Action = action,
            Result = "SUCCEEDED",
            OccurredAt = occurredAt.ToUniversalTime(),
            CorrelationId = correlationId,
            TargetEntityType = AuditTargetTypes.Reservation,
            TargetEntityId = reservationId
        };
    }
}

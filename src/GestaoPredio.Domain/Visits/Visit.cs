using GestaoPredio.Domain.Common;

namespace GestaoPredio.Domain.Visits;

public sealed class Visit
{
    private Visit() { }

    public Guid Id { get; private set; }
    public Guid ProfessionalId { get; private set; }
    public Guid? RoomId { get; private set; }
    public Guid? ReservationId { get; private set; }
    public string VisitorName { get; private set; } = string.Empty;
    public VisitStatus Status { get; private set; }
    public DateTimeOffset ArrivedAt { get; private set; }
    public DateTimeOffset? ServiceStartedAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public uint Version { get; private set; }

    public bool IsOpen => Status is VisitStatus.Waiting or VisitStatus.InService;

    public static Visit Arrive(Guid professionalId, Guid? roomId, Guid? reservationId,
        string visitorName, string actorUserId, DateTimeOffset occurredAt)
    {
        if (professionalId == Guid.Empty) throw new ArgumentException("O profissional deve ser informado.", nameof(professionalId));
        if (roomId == Guid.Empty) throw new ArgumentException("A sala deve possuir identidade válida.", nameof(roomId));
        if (reservationId == Guid.Empty) throw new ArgumentException("A reserva deve possuir identidade válida.", nameof(reservationId));
        ValidateActor(actorUserId);
        var name = visitorName?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            throw new ArgumentException("O nome do visitante deve ser informado.", nameof(visitorName));
        var timestamp = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
        return new Visit
        {
            Id = Guid.NewGuid(), ProfessionalId = professionalId, RoomId = roomId,
            ReservationId = reservationId, VisitorName = name, Status = VisitStatus.Waiting,
            ArrivedAt = timestamp, CreatedAt = timestamp, UpdatedAt = timestamp
        };
    }

    public void StartService(string actorUserId, DateTimeOffset occurredAt)
    {
        ValidateActor(actorUserId);
        if (Status != VisitStatus.Waiting)
            throw new InvalidOperationException("Somente uma visita aguardando pode iniciar atendimento.");
        var timestamp = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
        Status = VisitStatus.InService;
        ServiceStartedAt = timestamp;
        UpdatedAt = timestamp;
    }

    public void End(string actorUserId, DateTimeOffset occurredAt)
    {
        ValidateActor(actorUserId);
        if (Status != VisitStatus.InService)
            throw new InvalidOperationException("Somente uma visita em atendimento pode ser encerrada.");
        var timestamp = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
        Status = VisitStatus.Ended;
        EndedAt = timestamp;
        UpdatedAt = timestamp;
    }

    public void Cancel(string actorUserId, DateTimeOffset occurredAt)
    {
        ValidateActor(actorUserId);
        if (Status is not (VisitStatus.Waiting or VisitStatus.InService))
            throw new InvalidOperationException("A visita não pode ser cancelada neste estado.");
        var timestamp = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
        Status = VisitStatus.Cancelled;
        CancelledAt = timestamp;
        UpdatedAt = timestamp;
    }

    public void Correct(VisitStatus targetStatus, string reason, string actorUserId, DateTimeOffset occurredAt)
    {
        if (!Enum.IsDefined(targetStatus) || targetStatus == Status)
            throw new InvalidOperationException("A correção deve alterar o estado atual.");
        var normalizedReason = reason?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedReason) || normalizedReason.Length > 500)
            throw new ArgumentException("A correção exige justificativa válida.", nameof(reason));
        ValidateActor(actorUserId);
        var timestamp = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
        Status = targetStatus;
        ServiceStartedAt = targetStatus is VisitStatus.InService or VisitStatus.Ended
            ? ServiceStartedAt ?? timestamp
            : null;
        EndedAt = targetStatus == VisitStatus.Ended ? timestamp : null;
        CancelledAt = targetStatus == VisitStatus.Cancelled ? timestamp : null;
        UpdatedAt = timestamp;
    }

    private static void ValidateActor(string actorUserId)
    {
        if (string.IsNullOrWhiteSpace(actorUserId) || actorUserId.Length > 450)
            throw new ArgumentException("O ator deve ser informado.", nameof(actorUserId));
    }
}

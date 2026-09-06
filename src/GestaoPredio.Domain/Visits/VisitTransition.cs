using GestaoPredio.Domain.Common;

namespace GestaoPredio.Domain.Visits;

public sealed class VisitTransition
{
    private VisitTransition() { }

    public Guid Id { get; private set; }
    public Guid VisitId { get; private set; }
    public VisitStatus? PreviousStatus { get; private set; }
    public VisitStatus NewStatus { get; private set; }
    public string ActorUserId { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; }
    public string? Reason { get; private set; }
    public bool IsCorrection { get; private set; }

    public static VisitTransition Record(Guid visitId, VisitStatus? previousStatus, VisitStatus newStatus,
        string actorUserId, DateTimeOffset occurredAt, string? reason = null, bool isCorrection = false)
    {
        if (visitId == Guid.Empty) throw new ArgumentException("A visita deve ser informada.", nameof(visitId));
        if (!Enum.IsDefined(newStatus)) throw new ArgumentOutOfRangeException(nameof(newStatus));
        if (string.IsNullOrWhiteSpace(actorUserId) || actorUserId.Length > 450)
            throw new ArgumentException("O ator deve ser informado.", nameof(actorUserId));
        var normalizedReason = reason?.Trim();
        if (isCorrection && string.IsNullOrWhiteSpace(normalizedReason))
            throw new ArgumentException("A correção exige justificativa.", nameof(reason));
        if (normalizedReason?.Length > 500)
            throw new ArgumentException("A justificativa excede o limite.", nameof(reason));
        return new VisitTransition
        {
            Id = Guid.NewGuid(), VisitId = visitId, PreviousStatus = previousStatus, NewStatus = newStatus,
            ActorUserId = actorUserId, OccurredAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt),
            Reason = normalizedReason, IsCorrection = isCorrection
        };
    }
}

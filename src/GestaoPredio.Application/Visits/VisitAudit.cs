using GestaoPredio.Domain.Auditing;

namespace GestaoPredio.Application.Visits;

public static class VisitAudit
{
    private static readonly HashSet<string> Actions =
    [
        AuditActions.VisitArrived, AuditActions.VisitCheckedIn, AuditActions.VisitCheckedInManual,
        AuditActions.VisitServiceStarted, AuditActions.VisitEnded,
        AuditActions.VisitCancelled, AuditActions.VisitCorrected
    ];

    public static AuditEntry CreateSucceeded(Guid visitId, string action, DateTimeOffset occurredAt,
        string correlationId, string? actorUserId, string? ipAddress)
    {
        if (visitId == Guid.Empty) throw new ArgumentException("A visita deve ser informada.", nameof(visitId));
        if (!Actions.Contains(action)) throw new ArgumentException("A ação não pertence a Visitas.", nameof(action));
        if (string.IsNullOrWhiteSpace(correlationId))
            throw new ArgumentException("A correlação deve ser informada.", nameof(correlationId));
        return new AuditEntry
        {
            Id = Guid.NewGuid(), ActorUserId = actorUserId, IpAddress = ipAddress,
            Action = action, Result = "SUCCEEDED", OccurredAt = occurredAt.ToUniversalTime(),
            CorrelationId = correlationId, TargetEntityType = AuditTargetTypes.Visit, TargetEntityId = visitId
        };
    }
}

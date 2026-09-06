using GestaoPredio.Domain.Auditing;

namespace GestaoPredio.Application.Leases;

public static class LeaseAudit
{
    private static readonly HashSet<string> ApprovedActions =
    [
        AuditActions.LeaseCreated,
        AuditActions.LeaseUpdated,
        AuditActions.LeaseOccupancyPostponed,
        AuditActions.LeaseCancelled,
        AuditActions.LeaseEndScheduled,
        AuditActions.LeaseEndingPending,
        AuditActions.LeaseEnded
    ];

    public static AuditEntry CreateSucceeded(
        Guid leaseId,
        string action,
        DateTimeOffset occurredAt,
        string correlationId,
        string? actorUserId,
        string? ipAddress,
        IEnumerable<string>? changedFields = null)
    {
        if (leaseId == Guid.Empty) throw new ArgumentException("A locação deve ser informada.", nameof(leaseId));
        if (!ApprovedActions.Contains(action)) throw new ArgumentException("A ação não pertence a Locações.", nameof(action));
        if (string.IsNullOrWhiteSpace(correlationId)) throw new ArgumentException("A correlação deve ser informada.", nameof(correlationId));

        var entry = new AuditEntry
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorUserId,
            IpAddress = ipAddress,
            Action = action,
            Result = "SUCCESS",
            OccurredAt = occurredAt.ToUniversalTime(),
            CorrelationId = correlationId,
            TargetEntityType = AuditTargetTypes.Lease,
            TargetEntityId = leaseId
        };
        entry.SetChangedFields(changedFields);
        return entry;
    }
}

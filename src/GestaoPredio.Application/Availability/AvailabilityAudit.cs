using GestaoPredio.Domain.Auditing;

namespace GestaoPredio.Application.Availability;

public static class AvailabilityAudit
{
    private static readonly HashSet<string> Actions =
    [
        AuditActions.OperatingHoursUpdated, AuditActions.RoomBlockCreated,
        AuditActions.RoomBlockUpdated, AuditActions.RoomBlockCancelled
    ];

    public static AuditEntry CreateSucceeded(Guid targetId, string targetType, string action,
        DateTimeOffset occurredAt, string correlationId, string? actorUserId, string? ipAddress)
    {
        if (targetId == Guid.Empty) throw new ArgumentException("O alvo deve ser informado.", nameof(targetId));
        if (targetType is not (AuditTargetTypes.OperatingHours or AuditTargetTypes.RoomBlock))
            throw new ArgumentException("O tipo de alvo não pertence à disponibilidade.", nameof(targetType));
        if (!Actions.Contains(action)) throw new ArgumentException("A ação não pertence à disponibilidade.", nameof(action));
        if (string.IsNullOrWhiteSpace(correlationId))
            throw new ArgumentException("A correlação deve ser informada.", nameof(correlationId));
        return new AuditEntry
        {
            Id = Guid.NewGuid(), ActorUserId = actorUserId, IpAddress = ipAddress,
            Action = action, Result = "SUCCEEDED", OccurredAt = occurredAt.ToUniversalTime(),
            CorrelationId = correlationId, TargetEntityType = targetType, TargetEntityId = targetId
        };
    }
}

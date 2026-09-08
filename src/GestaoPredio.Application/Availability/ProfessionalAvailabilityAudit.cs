using GestaoPredio.Domain.Auditing;

namespace GestaoPredio.Application.Availability;

public static class ProfessionalAvailabilityAudit
{
    public static AuditEntry CreateSucceeded(Guid targetId, string targetType, string action,
        DateTimeOffset occurredAt, string correlationId, string? actorUserId, string? ipAddress)
    {
        if (targetId == Guid.Empty) throw new ArgumentException("O alvo deve ser informado.", nameof(targetId));
        if (targetType is not (AuditTargetTypes.Professional or AuditTargetTypes.ProfessionalAvailabilityException))
            throw new ArgumentException("O tipo de alvo não pertence à disponibilidade profissional.", nameof(targetType));
        if (!AuditActions.ProfessionalAvailabilityActions.Contains(action, StringComparer.Ordinal))
            throw new ArgumentException("A ação não pertence à disponibilidade profissional.", nameof(action));
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

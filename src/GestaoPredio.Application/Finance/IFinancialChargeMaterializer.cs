namespace GestaoPredio.Application.Finance;

public interface IFinancialChargeMaterializer
{
    Task<FinancialMaterializationResult> MaterializeAsync(
        DateTimeOffset throughAt, DateTimeOffset occurredAt, string? actorUserId, string correlationId, string? ipAddress,
        CancellationToken cancellationToken);
}

public sealed record FinancialMaterializationResult(DateTimeOffset ThroughAt, int CreatedCount);

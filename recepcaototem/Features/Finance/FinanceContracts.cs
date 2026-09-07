using recepcaototem.Features.Common;

namespace recepcaototem.Features.Finance;

public sealed record MaterializeFinanceRequest(DateOnly? ThroughDate) : IStrictModuleRequest;
public sealed record ChargeConcurrencyRequest(string? ConcurrencyToken) : IStrictModuleRequest;
public sealed record AdjustChargeRequest(decimal FinalAmount, string? AdjustmentReason, string? ConcurrencyToken) : IStrictModuleRequest;
public sealed record CancelChargeRequest(string? Reason, string? ConcurrencyToken) : IStrictModuleRequest;
public sealed record FinancialChargeResponse(Guid Id, Guid LeaseId, Guid ProfessionalId, Guid TenantId,
    DateTimeOffset ReferencePeriodStart, DateTimeOffset ReferencePeriodEnd, DateOnly DueDate,
    decimal CalculatedAmount, decimal FinalAmount, string Status, string CalculationDetails,
    string? AdjustmentReason, string? CancellationReason, DateTimeOffset? PaidAt,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string ConcurrencyToken,
    string? ProfessionalName = null, string? TenantName = null);
public sealed record FinancialMaterializationResponse(DateOnly ThroughDate, int CreatedCount);
public sealed record FinancialSummaryResponse(decimal PendingAmount, decimal OverdueAmount, decimal PaidAmount,
    int PendingCount, int OverdueCount, int PaidCount);

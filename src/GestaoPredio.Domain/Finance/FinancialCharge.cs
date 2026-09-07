using GestaoPredio.Domain.Common;
using GestaoPredio.Domain.Rooms;

namespace GestaoPredio.Domain.Finance;

public sealed class FinancialCharge
{
    public const int MaximumDetailsLength = 4000;
    public const int MaximumReasonLength = 500;

    private FinancialCharge() { }

    public Guid Id { get; private set; }
    public Guid LeaseId { get; private set; }
    public Guid ProfessionalId { get; private set; }
    public Guid TenantId { get; private set; }
    public DateTimeOffset ReferencePeriodStart { get; private set; }
    public DateTimeOffset ReferencePeriodEnd { get; private set; }
    public DateOnly DueDate { get; private set; }
    public decimal CalculatedAmount { get; private set; }
    public decimal FinalAmount { get; private set; }
    public FinancialChargeStatus Status { get; private set; }
    public string CalculationDetails { get; private set; } = "";
    public string? AdjustmentReason { get; private set; }
    public string? CancellationReason { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public uint Version { get; private set; }

    public static FinancialCharge Create(
        Guid leaseId, Guid professionalId, Guid tenantId,
        DateTimeOffset periodStart, DateTimeOffset periodEnd, DateOnly dueDate,
        decimal calculatedAmount, string calculationDetails, DateTimeOffset occurredAt)
    {
        if (leaseId == Guid.Empty || professionalId == Guid.Empty || tenantId == Guid.Empty)
            throw new ArgumentException("Os recursos da cobrança são obrigatórios.");
        if (periodEnd <= periodStart) throw new ArgumentException("O período financeiro é inválido.");
        if (!RoomRate.IsValid(calculatedAmount)) throw new ArgumentOutOfRangeException(nameof(calculatedAmount));
        if (string.IsNullOrWhiteSpace(calculationDetails) || calculationDetails.Length > MaximumDetailsLength)
            throw new ArgumentException("Os detalhes do cálculo são inválidos.", nameof(calculationDetails));
        var at = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
        return new FinancialCharge
        {
            Id = Guid.NewGuid(), LeaseId = leaseId, ProfessionalId = professionalId, TenantId = tenantId,
            ReferencePeriodStart = TimestampNormalizer.ToUtcMicroseconds(periodStart),
            ReferencePeriodEnd = TimestampNormalizer.ToUtcMicroseconds(periodEnd), DueDate = dueDate,
            CalculatedAmount = calculatedAmount, FinalAmount = calculatedAmount,
            Status = FinancialChargeStatus.Pending, CalculationDetails = calculationDetails,
            CreatedAt = at, UpdatedAt = at
        };
    }

    public bool IsOverdue(DateOnly operationalDate) => Status == FinancialChargeStatus.Pending && DueDate < operationalDate;

    public void MarkPaid(DateTimeOffset occurredAt)
    {
        if (Status != FinancialChargeStatus.Pending) throw new InvalidOperationException("A cobrança não pode ser paga neste estado.");
        Status = FinancialChargeStatus.Paid;
        PaidAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
        UpdatedAt = PaidAt.Value;
    }

    public void Adjust(decimal finalAmount, string reason, DateTimeOffset occurredAt)
    {
        if (Status != FinancialChargeStatus.Pending) throw new InvalidOperationException("Somente cobranças pendentes podem ser ajustadas.");
        if (!RoomRate.IsValid(finalAmount)) throw new ArgumentOutOfRangeException(nameof(finalAmount));
        ValidateReason(reason);
        FinalAmount = finalAmount;
        AdjustmentReason = reason.Trim();
        UpdatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
    }

    public void Cancel(string reason, DateTimeOffset occurredAt)
    {
        if (Status != FinancialChargeStatus.Pending) throw new InvalidOperationException("Somente cobranças pendentes podem ser canceladas.");
        ValidateReason(reason);
        Status = FinancialChargeStatus.Cancelled;
        CancellationReason = reason.Trim();
        UpdatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
    }

    private static void ValidateReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > MaximumReasonLength)
            throw new ArgumentException("A justificativa é obrigatória.", nameof(reason));
    }
}

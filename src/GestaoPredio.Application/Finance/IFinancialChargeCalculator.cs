using GestaoPredio.Domain.Leases;

namespace GestaoPredio.Application.Finance;

public interface IFinancialChargeCalculator
{
    IReadOnlyList<FinancialChargeDraft> Calculate(Lease lease, DateTimeOffset throughAt);
}

public sealed record FinancialChargeDraft(
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    DateOnly DueDate,
    decimal Amount,
    string CalculationDetails);

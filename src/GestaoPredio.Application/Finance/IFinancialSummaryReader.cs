namespace GestaoPredio.Application.Finance;

public sealed record FinancialSummary(
    decimal PendingAmount, decimal OverdueAmount, decimal PaidAmount,
    int PendingCount, int OverdueCount, int PaidCount);

public interface IFinancialSummaryReader
{
    Task<FinancialSummary> ReadAsync(DateOnly? from, DateOnly? to, DateOnly operationalDate,
        CancellationToken cancellationToken);
}

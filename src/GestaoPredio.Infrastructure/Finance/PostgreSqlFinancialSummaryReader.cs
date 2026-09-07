using GestaoPredio.Application.Finance;
using GestaoPredio.Domain.Finance;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GestaoPredio.Infrastructure.Finance;

public sealed class PostgreSqlFinancialSummaryReader(ApplicationDbContext db) : IFinancialSummaryReader
{
    public async Task<FinancialSummary> ReadAsync(DateOnly? from, DateOnly? to, DateOnly operationalDate,
        CancellationToken cancellationToken)
    {
        var query = db.FinancialCharges.AsNoTracking();
        if (from is not null) query = query.Where(x => x.DueDate >= from.Value);
        if (to is not null) query = query.Where(x => x.DueDate <= to.Value);
        var pending = query.Where(x => x.Status == FinancialChargeStatus.Pending && x.DueDate >= operationalDate);
        var overdue = query.Where(x => x.Status == FinancialChargeStatus.Pending && x.DueDate < operationalDate);
        var paid = query.Where(x => x.Status == FinancialChargeStatus.Paid);
        return new FinancialSummary(
            await pending.SumAsync(x => (decimal?)x.FinalAmount, cancellationToken) ?? 0m,
            await overdue.SumAsync(x => (decimal?)x.FinalAmount, cancellationToken) ?? 0m,
            await paid.SumAsync(x => (decimal?)x.FinalAmount, cancellationToken) ?? 0m,
            await pending.CountAsync(cancellationToken), await overdue.CountAsync(cancellationToken),
            await paid.CountAsync(cancellationToken));
    }
}

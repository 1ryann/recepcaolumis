using GestaoPredio.Application.Finance;
using GestaoPredio.Application.Leases;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Finance;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GestaoPredio.Infrastructure.Finance;

public sealed class PostgreSqlFinancialChargeMaterializer(
    ApplicationDbContext db,
    IFinancialChargeCalculator calculator,
    ILeaseResourceLock resourceLock) : IFinancialChargeMaterializer
{
    public async Task<FinancialMaterializationResult> MaterializeAsync(
        DateTimeOffset throughAt, DateTimeOffset occurredAt, string? actorUserId, string correlationId, string? ipAddress,
        CancellationToken cancellationToken)
    {
        var through = throughAt.ToUniversalTime();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var leases = await db.Leases.Where(x => x.LifecycleState != Domain.Leases.LeaseLifecycleState.Cancelled)
            .ToListAsync(cancellationToken);
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest(
            leases.Select(x => x.TenantId).ToArray(), leases.Select(x => x.RoomId).ToArray(),
            leases.Select(x => x.ProfessionalId).ToArray()), cancellationToken);
        leases = await db.Leases.Where(x => x.LifecycleState != Domain.Leases.LeaseLifecycleState.Cancelled)
            .ToListAsync(cancellationToken);

        var created = 0;
        foreach (var lease in leases)
        {
            var drafts = calculator.Calculate(lease, through);
            if (drafts.Count == 0) continue;
            var existing = await db.FinancialCharges.Where(x => x.LeaseId == lease.Id)
                .Select(x => new { x.ReferencePeriodStart, x.ReferencePeriodEnd }).ToListAsync(cancellationToken);
            var existingKeys = existing.Select(x => (x.ReferencePeriodStart, x.ReferencePeriodEnd)).ToHashSet();
            foreach (var draft in drafts)
            {
                if (!existingKeys.Add((draft.PeriodStart, draft.PeriodEnd))) continue;
                var charge = FinancialCharge.Create(lease.Id, lease.ProfessionalId, lease.TenantId,
                    draft.PeriodStart, draft.PeriodEnd, draft.DueDate, draft.Amount, draft.CalculationDetails, occurredAt);
                db.FinancialCharges.Add(charge);
                db.AuditEntries.Add(CreateAudit(charge.Id, AuditActions.FinancialChargeMaterialized,
                    occurredAt, correlationId, actorUserId, ipAddress));
                created++;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new FinancialMaterializationResult(through, created);
    }

    private static AuditEntry CreateAudit(Guid id, string action, DateTimeOffset at,
        string correlationId, string? actorUserId, string? ipAddress) => new()
    {
        Id = Guid.NewGuid(), TargetEntityId = id, TargetEntityType = AuditTargetTypes.FinancialCharge,
        Action = action, Result = "SUCCEEDED", OccurredAt = at, CorrelationId = correlationId,
        ActorUserId = actorUserId, IpAddress = ipAddress
    };
}

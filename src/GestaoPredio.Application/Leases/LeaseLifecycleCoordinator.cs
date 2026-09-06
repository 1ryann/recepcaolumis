using GestaoPredio.Domain.Leases;

namespace GestaoPredio.Application.Leases;

public sealed class LeaseLifecycleCoordinator(ILeaseOpenVisitProbe openVisitProbe) : ILeaseLifecycleCoordinator
{
    public async Task<LeaseLifecycleReconciliation> ReconcileAsync(
        Lease lease,
        IReadOnlyCollection<LeaseOccurrence> occurrences,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(occurrences);

        if (lease.LifecycleState is LeaseLifecycleState.Ended or LeaseLifecycleState.Cancelled)
            return LeaseLifecycleReconciliation.None;
        if (lease.LifecycleState == LeaseLifecycleState.Open &&
            (lease.OccupancyEndAt is null || lease.OccupancyEndAt > now))
            return LeaseLifecycleReconciliation.None;

        foreach (var occurrence in occurrences.Where(item =>
                     item.State == LeaseOccurrenceState.Planned && item.StartAt >= now))
            occurrence.Cancel(now);

        if (await openVisitProbe.HasOpenVisitsAsync(lease.Id, cancellationToken))
        {
            if (lease.LifecycleState == LeaseLifecycleState.Open)
                lease.MarkEndingPending(now);
            return LeaseLifecycleReconciliation.EndingPending;
        }

        lease.MarkEnded(now);
        return LeaseLifecycleReconciliation.Ended;
    }
}

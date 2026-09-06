using GestaoPredio.Domain.Leases;

namespace GestaoPredio.Application.Leases;

public interface ILeaseLifecycleCoordinator
{
    Task<LeaseLifecycleReconciliation> ReconcileAsync(
        Lease lease,
        IReadOnlyCollection<LeaseOccurrence> occurrences,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public enum LeaseLifecycleReconciliation
{
    None = 0,
    EndingPending = 1,
    Ended = 2
}

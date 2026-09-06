using GestaoPredio.Domain.Leases;

namespace GestaoPredio.Application.Scheduling;

public interface ILeaseOccurrencePlanner
{
    LeaseOccurrencePlan Plan(
        Lease lease,
        DateTimeOffset now,
        IReadOnlyCollection<LeaseOccurrence> existingOccurrences);
}

public sealed record OccurrencePeriod(DateTimeOffset StartAt, DateTimeOffset EndAt);

public sealed record LeaseOccurrencePlan(
    IReadOnlyList<OccurrencePeriod> ToCreate,
    IReadOnlyList<Guid> ToCancel,
    DateTimeOffset MaterializedThroughAt);

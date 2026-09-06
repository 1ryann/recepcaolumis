using GestaoPredio.Domain.Leases;

namespace GestaoPredio.Application.Leases;

public static class LeaseAvailability
{
    public static bool Overlaps(
        DateTimeOffset firstStart,
        DateTimeOffset? firstEnd,
        DateTimeOffset secondStart,
        DateTimeOffset? secondEnd) =>
        (firstEnd is null || secondStart < firstEnd.Value) &&
        (secondEnd is null || firstStart < secondEnd.Value);

    public static bool BlocksResources(LeaseLifecycleState state) =>
        state is LeaseLifecycleState.Open or LeaseLifecycleState.EndingPending;
}

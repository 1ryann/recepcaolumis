using GestaoPredio.Application.Leases;
using GestaoPredio.Domain.Leases;

namespace GestaoPredio.UnitTests;

public sealed class LeaseLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(-2, -1, 0, 1, false)]
    [InlineData(-2, 0, 0, 1, false)]
    [InlineData(-2, 1, 0, 2, true)]
    [InlineData(0, 2, -1, 1, true)]
    public void Overlap_uses_half_open_intervals(
        int firstStartHours,
        int firstEndHours,
        int secondStartHours,
        int secondEndHours,
        bool expected)
    {
        Assert.Equal(expected, LeaseAvailability.Overlaps(
            Now.AddHours(firstStartHours), Now.AddHours(firstEndHours),
            Now.AddHours(secondStartHours), Now.AddHours(secondEndHours)));
    }

    [Fact]
    public void Overlap_treats_a_null_end_as_infinity()
    {
        Assert.True(LeaseAvailability.Overlaps(Now, null, Now.AddYears(100), Now.AddYears(101)));
        Assert.False(LeaseAvailability.Overlaps(Now, Now.AddHours(1), Now.AddHours(1), null));
    }

    [Theory]
    [InlineData(LeaseLifecycleState.Open, true)]
    [InlineData(LeaseLifecycleState.EndingPending, true)]
    [InlineData(LeaseLifecycleState.Ended, false)]
    [InlineData(LeaseLifecycleState.Cancelled, false)]
    public void Only_open_and_pending_leases_block_resources(LeaseLifecycleState state, bool expected)
    {
        Assert.Equal(expected, LeaseAvailability.BlocksResources(state));
    }

    [Fact]
    public async Task Reconcile_ends_an_expired_lease_without_open_visits()
    {
        var lease = CreateExpiredLease();
        var past = LeaseOccurrence.Create(lease.Id, Now.AddDays(-2), Now.AddDays(-1), Now.AddDays(-2));
        var future = LeaseOccurrence.Create(lease.Id, Now.AddHours(1), Now.AddHours(2), Now);
        var coordinator = new LeaseLifecycleCoordinator(new StubVisitProbe(false));

        var result = await coordinator.ReconcileAsync(lease, [past, future], Now, CancellationToken.None);

        Assert.Equal(LeaseLifecycleReconciliation.Ended, result);
        Assert.Equal(LeaseLifecycleState.Ended, lease.LifecycleState);
        Assert.Equal(LeaseOccurrenceState.Planned, past.State);
        Assert.Equal(LeaseOccurrenceState.Cancelled, future.State);
    }

    [Fact]
    public async Task Reconcile_keeps_expired_lease_pending_when_an_open_visit_exists()
    {
        var lease = CreateExpiredLease();
        var coordinator = new LeaseLifecycleCoordinator(new StubVisitProbe(true));

        var result = await coordinator.ReconcileAsync(lease, [], Now, CancellationToken.None);

        Assert.Equal(LeaseLifecycleReconciliation.EndingPending, result);
        Assert.Equal(LeaseLifecycleState.EndingPending, lease.LifecycleState);
        Assert.True(LeaseAvailability.BlocksResources(lease.LifecycleState));
    }

    [Fact]
    public async Task Default_visit_probe_reports_no_pending_visits()
    {
        Assert.False(await new NoOpenVisitProbe().HasOpenVisitsAsync(Guid.NewGuid(), CancellationToken.None));
    }

    private static Lease CreateExpiredLease() => Lease.Create(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LeaseMode.Monthly, 100m,
        Now.AddDays(-3), null, Now.AddDays(-2), Now.AddDays(-1), 3, Now.AddDays(-3));

    private sealed class StubVisitProbe(bool result) : ILeaseOpenVisitProbe
    {
        public Task<bool> HasOpenVisitsAsync(Guid leaseId, CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }
}

using GestaoPredio.Domain.Leases;

namespace GestaoPredio.UnitTests;

public sealed class LeaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0)]
    [InlineData(0.01)]
    [InlineData(100.99)]
    [InlineData(9999999999999.99)]
    public void Create_accepts_the_approved_rates(decimal rate)
    {
        var lease = CreateMonthly(rate: rate);

        Assert.Equal(rate, lease.ContractedRate);
        Assert.Equal(LeaseLifecycleState.Open, lease.LifecycleState);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.999)]
    [InlineData(10000000000000.00)]
    public void Create_rejects_an_invalid_rate(decimal rate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateMonthly(rate: rate));
    }

    [Theory]
    [InlineData(LeaseMode.Daily)]
    [InlineData(LeaseMode.Hourly)]
    public void Finite_modes_require_an_end(LeaseMode mode)
    {
        Assert.Throws<ArgumentException>(() => Create(mode, Now.AddDays(1), null, null));
    }

    [Fact]
    public void Monthly_accepts_an_indefinite_end_and_requires_an_anchor()
    {
        var lease = Create(LeaseMode.Monthly, Now.AddDays(1), null, 5);

        Assert.Null(lease.OccupancyEndAt);
        Assert.Equal(5, lease.MonthlyAnchorDay);
        Assert.Throws<ArgumentException>(() => Create(LeaseMode.Monthly, Now.AddDays(1), null, null));
    }

    [Fact]
    public void Non_monthly_mode_rejects_a_monthly_anchor()
    {
        Assert.Throws<ArgumentException>(() =>
            Create(LeaseMode.Hourly, Now.AddHours(1), Now.AddHours(2), 5));
    }

    [Fact]
    public void Create_rejects_invalid_dates_and_due_day()
    {
        Assert.Throws<ArgumentException>(() => CreateMonthly(billingStartAt: Now.AddDays(2)));
        Assert.Throws<ArgumentException>(() => CreateMonthly(endAt: Now.AddDays(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateMonthly(billingDueDay: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateMonthly(billingDueDay: 32));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create((LeaseMode)999, Now.AddDays(1), Now.AddDays(2), null));
    }

    [Fact]
    public void Operational_status_is_derived_from_dates_and_lifecycle()
    {
        var scheduled = CreateMonthly(startAt: Now.AddHours(1));
        var active = CreateMonthly(startAt: Now.AddHours(-1), endAt: Now.AddHours(1));
        var expired = CreateMonthly(startAt: Now.AddHours(-2), endAt: Now.AddHours(-1));
        var cancelled = CreateMonthly(startAt: Now.AddHours(1));
        var ended = CreateMonthly(startAt: Now.AddHours(-2), endAt: Now.AddHours(-1));

        cancelled.Cancel(Now);
        ended.MarkEnded(Now);

        Assert.Equal(LeaseOperationalStatus.Scheduled, scheduled.GetOperationalStatus(Now));
        Assert.Equal(LeaseOperationalStatus.Active, active.GetOperationalStatus(Now));
        Assert.Equal(LeaseOperationalStatus.EndingPending, expired.GetOperationalStatus(Now));
        Assert.Equal(LeaseOperationalStatus.Cancelled, cancelled.GetOperationalStatus(Now));
        Assert.Equal(LeaseOperationalStatus.Ended, ended.GetOperationalStatus(Now));
    }

    [Fact]
    public void Postpone_preserves_billing_and_explicit_end()
    {
        var originalBilling = Now;
        var originalEnd = Now.AddDays(20);
        var lease = CreateMonthly(startAt: Now.AddDays(1), endAt: originalEnd, billingStartAt: originalBilling);

        lease.PostponeOccupancy(Now.AddDays(2), monthlyAnchorDay: 7, occurredAt: Now.AddMinutes(1));

        Assert.Equal(originalBilling, lease.BillingStartAt);
        Assert.Equal(originalEnd, lease.OccupancyEndAt);
        Assert.Equal(Now.AddDays(2), lease.OccupancyStartAt);
        Assert.Equal(7, lease.MonthlyAnchorDay);
    }

    [Fact]
    public void Postpone_rejects_started_or_impossible_occupancy()
    {
        var started = CreateMonthly(startAt: Now.AddHours(-1), endAt: Now.AddDays(1));
        var scheduled = CreateMonthly(startAt: Now.AddDays(1), endAt: Now.AddDays(2));

        Assert.Throws<InvalidOperationException>(() =>
            started.PostponeOccupancy(Now.AddHours(1), 5, Now));
        Assert.Throws<ArgumentException>(() =>
            scheduled.PostponeOccupancy(Now.AddDays(2), 7, Now));
    }

    [Fact]
    public void Terminal_lifecycle_cannot_be_rewritten()
    {
        var cancelled = CreateMonthly(startAt: Now.AddDays(1));
        cancelled.Cancel(Now);

        Assert.Throws<InvalidOperationException>(() => cancelled.MarkEnded(Now));
        Assert.Throws<InvalidOperationException>(() => cancelled.MarkEndingPending(Now));
    }

    [Fact]
    public void Occurrence_validates_interval_and_supports_explicit_state_changes()
    {
        var occurrence = LeaseOccurrence.Create(Guid.NewGuid(), Now, Now.AddHours(1), Now);

        occurrence.Cancel(Now.AddMinutes(1));

        Assert.NotEqual(Guid.Empty, occurrence.Id);
        Assert.Equal(LeaseOccurrenceState.Cancelled, occurrence.State);
        Assert.Throws<ArgumentException>(() =>
            LeaseOccurrence.Create(Guid.NewGuid(), Now, Now, Now));
    }

    private static Lease CreateMonthly(
        decimal rate = 100m,
        DateTimeOffset? startAt = null,
        DateTimeOffset? endAt = null,
        DateTimeOffset? billingStartAt = null,
        int? billingDueDay = null)
    {
        var effectiveStart = startAt ?? Now.AddDays(1);
        return Create(
            LeaseMode.Monthly,
            effectiveStart,
            endAt,
            effectiveStart.Day,
            rate,
            billingStartAt ?? (effectiveStart < Now ? effectiveStart : Now),
            billingDueDay);
    }


    [Fact]
    public void Reactivation_demands_a_future_end_and_only_a_cancelled_or_ended_lease()
    {
        var open = Create(LeaseMode.Hourly, Now.AddDays(1), Now.AddDays(1).AddHours(2), null);
        // An open lease has nothing to reactivate: it is already working.
        Assert.Throws<InvalidOperationException>(() => open.Reactivate(Now.AddDays(5), Now));

        var cancelled = Create(LeaseMode.Hourly, Now.AddDays(1), Now.AddDays(1).AddHours(2), null);
        cancelled.Cancel(Now);
        // Restoring the old window would hand back a lease that is already over, so the end has to be future.
        Assert.Throws<ArgumentException>(() => cancelled.Reactivate(Now.AddHours(-1), Now));

        cancelled.Reactivate(Now.AddDays(5), Now);
        Assert.Equal(LeaseLifecycleState.Open, cancelled.LifecycleState);
        Assert.Equal(Now.AddDays(5), cancelled.OccupancyEndAt);
        // The start is untouched: reactivating resumes the contract, it does not write a new one.
        Assert.Equal(Now.AddDays(1), cancelled.OccupancyStartAt);
        Assert.Null(cancelled.MaterializedThroughAt);
    }
    private static Lease Create(
        LeaseMode mode,
        DateTimeOffset startAt,
        DateTimeOffset? endAt,
        int? monthlyAnchorDay,
        decimal rate = 100m,
        DateTimeOffset? billingStartAt = null,
        int? billingDueDay = null) =>
        Lease.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), mode, rate,
            billingStartAt ?? Now, billingDueDay, startAt, endAt, monthlyAnchorDay, Now);
}

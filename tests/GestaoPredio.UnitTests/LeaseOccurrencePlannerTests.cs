using GestaoPredio.Application.Scheduling;
using GestaoPredio.Domain.Leases;

namespace GestaoPredio.UnitTests;

public sealed class LeaseOccurrencePlannerTests
{
    private static readonly TimeZoneInfo PortoVelho = OperationalTimeZone.Resolve("America/Porto_Velho");
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Time_zone_is_required_and_must_exist()
    {
        Assert.Equal("America/Porto_Velho", PortoVelho.Id);
        Assert.Throws<ArgumentException>(() => OperationalTimeZone.Resolve(""));
        Assert.Throws<TimeZoneNotFoundException>(() => OperationalTimeZone.Resolve("Not/A_Real_Zone"));
    }

    [Fact]
    public void Daily_interval_is_the_local_civil_day_converted_to_utc()
    {
        var interval = OperationalTimeZone.GetCivilDayInterval(new DateOnly(2026, 9, 5), PortoVelho);

        Assert.Equal(new DateTimeOffset(2026, 9, 5, 4, 0, 0, TimeSpan.Zero), interval.StartAt);
        Assert.Equal(new DateTimeOffset(2026, 9, 6, 4, 0, 0, TimeSpan.Zero), interval.EndAt);
    }

    [Fact]
    public void Hourly_plan_uses_the_exact_contract_interval()
    {
        var start = Now.AddHours(2);
        var end = start.AddHours(3);
        var lease = Create(LeaseMode.Hourly, start, end, null);

        var plan = new LeaseOccurrencePlanner(PortoVelho).Plan(lease, Now, []);

        var occurrence = Assert.Single(plan.ToCreate);
        Assert.Equal(start, occurrence.StartAt);
        Assert.Equal(end, occurrence.EndAt);
    }

    [Fact]
    public void Monthly_plan_restores_anchor_after_short_months_and_truncates_at_contract_end()
    {
        var start = new DateTimeOffset(2026, 1, 31, 14, 0, 0, TimeSpan.Zero); // 10:00 local
        var end = new DateTimeOffset(2026, 4, 15, 14, 0, 0, TimeSpan.Zero);
        var lease = Create(LeaseMode.Monthly, start, end, 31);

        var plan = new LeaseOccurrencePlanner(PortoVelho).Plan(lease, start.AddMinutes(-1), []);

        Assert.Collection(
            plan.ToCreate,
            item => Assert.Equal((start, new DateTimeOffset(2026, 2, 28, 14, 0, 0, TimeSpan.Zero)), (item.StartAt, item.EndAt)),
            item => Assert.Equal((new DateTimeOffset(2026, 2, 28, 14, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 3, 31, 14, 0, 0, TimeSpan.Zero)), (item.StartAt, item.EndAt)),
            item => Assert.Equal((new DateTimeOffset(2026, 3, 31, 14, 0, 0, TimeSpan.Zero), end), (item.StartAt, item.EndAt)));
    }

    [Fact]
    public void Indefinite_monthly_plan_is_limited_to_the_180_day_window()
    {
        var start = Now.AddDays(-10);
        var lease = Create(LeaseMode.Monthly, start, null, 5);

        var plan = new LeaseOccurrencePlanner(PortoVelho).Plan(lease, Now, []);

        Assert.NotEmpty(plan.ToCreate);
        Assert.InRange(plan.ToCreate.Count, 1, 8);
        Assert.Equal(Now.AddDays(LeaseOccurrencePlanner.HorizonDays), plan.MaterializedThroughAt);
        Assert.All(plan.ToCreate, item => Assert.True(item.StartAt < Now.AddDays(LeaseOccurrencePlanner.HorizonDays)));
    }

    [Fact]
    public void Existing_occurrences_are_not_duplicated_and_past_history_is_not_cancelled()
    {
        var start = Now.AddDays(-1);
        var end = Now.AddDays(1);
        var lease = Create(LeaseMode.Hourly, start, end, null);
        var existing = LeaseOccurrence.Create(lease.Id, start, end, start);

        var plan = new LeaseOccurrencePlanner(PortoVelho).Plan(lease, Now, [existing]);

        Assert.Empty(plan.ToCreate);
        Assert.Empty(plan.ToCancel);
    }

    private static Lease Create(LeaseMode mode, DateTimeOffset start, DateTimeOffset? end, int? anchor) =>
        Lease.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), mode, 100m,
            start.AddDays(-1), null, start, end, anchor, start.AddDays(-1));
}

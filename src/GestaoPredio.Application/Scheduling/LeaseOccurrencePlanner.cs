using GestaoPredio.Domain.Leases;

namespace GestaoPredio.Application.Scheduling;

public sealed class LeaseOccurrencePlanner(TimeZoneInfo timeZone) : ILeaseOccurrencePlanner
{
    public const int HorizonDays = 180;

    public LeaseOccurrencePlan Plan(
        Lease lease,
        DateTimeOffset now,
        IReadOnlyCollection<LeaseOccurrence> existingOccurrences)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(existingOccurrences);

        var utcNow = now.ToUniversalTime();
        var horizon = utcNow.AddDays(HorizonDays);
        var desired = lease.LifecycleState is LeaseLifecycleState.Open or LeaseLifecycleState.EndingPending
            ? BuildDesiredPeriods(lease, utcNow, horizon)
            : [];
        var existingKeys = existingOccurrences
            .Select(item => (item.StartAt, item.EndAt))
            .ToHashSet();
        var toCreate = desired
            .Where(item => !existingKeys.Contains((item.StartAt, item.EndAt)))
            .ToArray();
        var desiredKeys = desired.Select(item => (item.StartAt, item.EndAt)).ToHashSet();
        var toCancel = existingOccurrences
            .Where(item => item.State == LeaseOccurrenceState.Planned && item.StartAt >= utcNow)
            .Where(item => !desiredKeys.Contains((item.StartAt, item.EndAt)))
            .Select(item => item.Id)
            .ToArray();
        var through = lease.OccupancyEndAt is { } end && end < horizon ? end : horizon;

        return new LeaseOccurrencePlan(toCreate, toCancel, through);
    }

    private IReadOnlyList<OccurrencePeriod> BuildDesiredPeriods(
        Lease lease,
        DateTimeOffset now,
        DateTimeOffset horizon)
    {
        if (lease.OccupancyEndAt is { } contractEnd && contractEnd <= now)
            return [];
        if (lease.OccupancyStartAt >= horizon)
            return [];

        return lease.Mode switch
        {
            LeaseMode.Hourly or LeaseMode.Daily => BuildSinglePeriod(lease, now),
            LeaseMode.Monthly => BuildMonthlyPeriods(lease, now, horizon),
            _ => throw new InvalidOperationException("Modalidade de locação desconhecida.")
        };
    }

    private static IReadOnlyList<OccurrencePeriod> BuildSinglePeriod(Lease lease, DateTimeOffset now)
    {
        if (lease.OccupancyEndAt is not { } end || end <= now) return [];
        return [new OccurrencePeriod(lease.OccupancyStartAt, end)];
    }

    private IReadOnlyList<OccurrencePeriod> BuildMonthlyPeriods(
        Lease lease,
        DateTimeOffset now,
        DateTimeOffset horizon)
    {
        var anchor = lease.MonthlyAnchorDay
            ?? throw new InvalidOperationException("Locação mensal sem dia âncora.");
        var periods = new List<OccurrencePeriod>();
        var periodStart = lease.OccupancyStartAt;

        while (periodStart < horizon &&
               (lease.OccupancyEndAt is null || periodStart < lease.OccupancyEndAt.Value))
        {
            var nextBoundary = NextMonthlyBoundary(periodStart, anchor);
            var periodEnd = lease.OccupancyEndAt is { } end && end < nextBoundary ? end : nextBoundary;
            if (periodEnd > now)
                periods.Add(new OccurrencePeriod(periodStart, periodEnd));
            periodStart = nextBoundary;
        }

        return periods;
    }

    private DateTimeOffset NextMonthlyBoundary(DateTimeOffset currentStart, int anchor)
    {
        var local = TimeZoneInfo.ConvertTime(currentStart, timeZone).DateTime;
        var firstOfNextMonth = new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified).AddMonths(1);
        var day = Math.Min(anchor, DateTime.DaysInMonth(firstOfNextMonth.Year, firstOfNextMonth.Month));
        var nextLocal = firstOfNextMonth.AddDays(day - 1).Add(local.TimeOfDay);
        return OperationalTimeZone.ToUtc(nextLocal, timeZone);
    }
}

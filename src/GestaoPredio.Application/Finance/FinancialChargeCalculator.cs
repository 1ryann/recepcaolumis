using System.Text.Json;
using GestaoPredio.Application.Scheduling;
using GestaoPredio.Domain.Leases;

namespace GestaoPredio.Application.Finance;

public sealed class FinancialChargeCalculator(TimeZoneInfo operationalTimeZone) : IFinancialChargeCalculator
{
    public IReadOnlyList<FinancialChargeDraft> Calculate(Lease lease, DateTimeOffset throughAt)
    {
        ArgumentNullException.ThrowIfNull(lease);
        var through = throughAt.ToUniversalTime();
        if (lease.LifecycleState == LeaseLifecycleState.Cancelled || through <= lease.BillingStartAt)
            return [];
        return lease.Mode switch
        {
            LeaseMode.Monthly => Monthly(lease, through),
            LeaseMode.Daily => Daily(lease, through),
            LeaseMode.Hourly => Hourly(lease, through),
            _ => throw new InvalidOperationException("Modalidade de locação desconhecida.")
        };
    }

    private IReadOnlyList<FinancialChargeDraft> Monthly(Lease lease, DateTimeOffset through)
    {
        var result = new List<FinancialChargeDraft>();
        var start = lease.BillingStartAt;
        var anchor = lease.MonthlyAnchorDay ?? TimeZoneInfo.ConvertTime(start, operationalTimeZone).Day;
        while (start < through && (lease.OccupancyEndAt is null || start < lease.OccupancyEndAt.Value))
        {
            var end = NextMonthly(start, anchor);
            if (lease.OccupancyEndAt is { } leaseEnd && leaseEnd < end) end = leaseEnd;
            if (end <= start) break;
            if (end <= through)
                result.Add(Draft(lease, start, end, DueDate(lease, end), lease.ContractedRate,
                    new { mode = "MONTHLY", contractedRate = lease.ContractedRate }));
            start = NextMonthly(start, anchor);
        }
        return result;
    }

    private IReadOnlyList<FinancialChargeDraft> Daily(Lease lease, DateTimeOffset through)
    {
        var end = lease.OccupancyEndAt ?? through;
        var firstLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(lease.BillingStartAt, operationalTimeZone).DateTime);
        var last = end < through ? end : through;
        var result = new List<FinancialChargeDraft>();
        for (var date = firstLocal; ; date = date.AddDays(1))
        {
            var civil = OperationalTimeZone.GetCivilDayInterval(date, operationalTimeZone);
            if (civil.EndAt > last || civil.StartAt >= end) break;
            result.Add(Draft(lease, civil.StartAt, civil.EndAt, DueDate(lease, civil.StartAt), lease.ContractedRate,
                new { mode = "DAILY", contractedRate = lease.ContractedRate, civilDay = date.ToString("yyyy-MM-dd") }));
        }
        return result;
    }

    private IReadOnlyList<FinancialChargeDraft> Hourly(Lease lease, DateTimeOffset through)
    {
        if (lease.OccupancyEndAt is not { } end || end > through) return [];
        var start = lease.OccupancyStartAt < lease.BillingStartAt ? lease.BillingStartAt : lease.OccupancyStartAt;
        if (end <= start) return [];
        var minutes = (int)Math.Round((end - start).TotalMinutes, MidpointRounding.AwayFromZero);
        var unroundedAmount = lease.ContractedRate * minutes / 60m;
        var amount = decimal.Round(unroundedAmount, 2, MidpointRounding.AwayFromZero);
        return [Draft(lease, start, end, DueDate(lease, start), amount,
            new { mode = "HOURLY", contractedRate = lease.ContractedRate, minutes,
                unroundedAmount, calculatedAmount = amount })];
    }

    private FinancialChargeDraft Draft(Lease lease, DateTimeOffset start, DateTimeOffset end,
        DateOnly dueDate, decimal amount, object details) =>
        new(start.ToUniversalTime(), end.ToUniversalTime(), dueDate, amount,
            JsonSerializer.Serialize(details));

    private DateTimeOffset NextMonthly(DateTimeOffset current, int anchor)
    {
        var local = TimeZoneInfo.ConvertTime(current, operationalTimeZone).DateTime;
        var nextMonth = new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified).AddMonths(1);
        var day = Math.Min(anchor, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month));
        return OperationalTimeZone.GetCivilDayInterval(DateOnly.FromDateTime(nextMonth), operationalTimeZone).StartAt
            .Add(local.TimeOfDay).AddDays(day - 1);
    }

    private DateOnly DueDate(Lease lease, DateTimeOffset instant)
    {
        var local = TimeZoneInfo.ConvertTime(instant, operationalTimeZone);
        var day = lease.BillingDueDay is { } configured
            ? Math.Min(configured, DateTime.DaysInMonth(local.Year, local.Month))
            : local.Day;
        return new DateOnly(local.Year, local.Month, day);
    }
}

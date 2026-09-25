using GestaoPredio.Domain.Availability;

namespace GestaoPredio.Application.Availability;

public sealed class OperatingHoursEvaluator(TimeZoneInfo timeZone)
{
    public bool IsCivilDayOpen(IReadOnlyCollection<OperatingHourInterval> intervals,
        DateTimeOffset instantInDay)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        var day = TimeZoneInfo.ConvertTime(instantInDay, timeZone).DayOfWeek;
        return intervals.Any(interval => interval.DayOfWeek == day);
    }

    /// <summary>
    /// Whether a schedule keeps a period that is already committed inside the opening hours. A period that lives in
    /// one civil day has to fit inside a single opening interval — that is <see cref="Contains"/>. A period that
    /// crosses midnight cannot fit inside any daily interval by construction, so it is judged by the days it covers,
    /// each of which has to be open: the same rule a daily lease already uses.
    /// <para>
    /// Without that second branch a single multi-day occupancy made every possible schedule invalid. Production,
    /// 2026-09-25: after the database was rebuilt there was no schedule, so the guard that rejects an out-of-hours
    /// lease (it only runs when a schedule exists) let through an hourly lease covering a whole month — and that
    /// lease then made the operating-hours screen unsavable for good, whatever hours were typed.
    /// </para>
    /// </summary>
    public bool CoversPeriod(IReadOnlyCollection<OperatingHourInterval> intervals,
        DateTimeOffset startAt, DateTimeOffset endAt)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        if (endAt <= startAt) return false;
        var localStart = TimeZoneInfo.ConvertTime(startAt, timeZone).DateTime;
        var localEnd = TimeZoneInfo.ConvertTime(endAt, timeZone).DateTime;
        if (localStart.Date == localEnd.Date) return Contains(intervals, startAt, endAt);
        // The closing instant is not occupied: an occupancy that ends at midnight does not need that day open.
        var lastDay = localEnd.TimeOfDay == TimeSpan.Zero ? localEnd.Date.AddDays(-1) : localEnd.Date;
        // A week or more covers every weekday, so asking day by day would only repeat the same seven answers.
        if ((lastDay - localStart.Date).Days >= 6)
            return Enum.GetValues<DayOfWeek>().All(day => intervals.Any(interval => interval.DayOfWeek == day));
        for (var day = localStart.Date; day <= lastDay; day = day.AddDays(1))
            if (!intervals.Any(interval => interval.DayOfWeek == day.DayOfWeek)) return false;
        return true;
    }

    public bool Contains(IReadOnlyCollection<OperatingHourInterval> intervals,
        DateTimeOffset startAt, DateTimeOffset endAt)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        if (endAt <= startAt) return false;
        var localStart = TimeZoneInfo.ConvertTime(startAt, timeZone).DateTime;
        var localEnd = TimeZoneInfo.ConvertTime(endAt, timeZone).DateTime;
        if (localStart.Date != localEnd.Date) return false;
        var startTime = TimeOnly.FromDateTime(localStart);
        var endTime = TimeOnly.FromDateTime(localEnd);
        return intervals.Any(interval => interval.DayOfWeek == localStart.DayOfWeek &&
            interval.OpensAt <= startTime && endTime <= interval.ClosesAt);
    }
}

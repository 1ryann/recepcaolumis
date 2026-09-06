using GestaoPredio.Domain.Availability;

namespace GestaoPredio.Application.Availability;

public sealed class OperatingHoursEvaluator(TimeZoneInfo timeZone)
{
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

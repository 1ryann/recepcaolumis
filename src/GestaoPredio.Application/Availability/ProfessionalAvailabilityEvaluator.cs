using GestaoPredio.Domain.Availability;
using GestaoPredio.Domain.Professionals;

namespace GestaoPredio.Application.Availability;

public static class ProfessionalAvailabilityEvaluator
{
    public static IReadOnlyList<ProfessionalLocalTimeRange> GetEffectiveRanges(
        ProfessionalAvailabilityMode mode,
        DateOnly date,
        IReadOnlyCollection<OperatingHourInterval> operatingHours,
        IReadOnlyCollection<ProfessionalAvailabilityInterval> customIntervals,
        IReadOnlyCollection<ProfessionalAvailabilityException> exceptions)
    {
        ArgumentNullException.ThrowIfNull(operatingHours);
        ArgumentNullException.ThrowIfNull(customIntervals);
        ArgumentNullException.ThrowIfNull(exceptions);

        var global = operatingHours
            .Where(interval => interval.DayOfWeek == date.DayOfWeek)
            .Select(interval => new ProfessionalLocalTimeRange(interval.OpensAt, interval.ClosesAt))
            .OrderBy(interval => interval.StartTime)
            .ToArray();

        IEnumerable<ProfessionalLocalTimeRange> ranges = mode switch
        {
            ProfessionalAvailabilityMode.InheritGlobal => global,
            ProfessionalAvailabilityMode.Custom => Intersect(
                customIntervals
                    .Where(interval => interval.DayOfWeek == date.DayOfWeek)
                    .Select(interval => new ProfessionalLocalTimeRange(interval.StartTime, interval.EndTime)),
                global),
            _ => []
        };

        var applicable = exceptions.Where(exception => exception.Date == date).ToArray();
        if (applicable.Any(exception => exception.AllDay)) return [];

        foreach (var exception in applicable.OrderBy(value => value.StartTime))
        {
            var removal = new ProfessionalLocalTimeRange(exception.StartTime!.Value, exception.EndTime!.Value);
            ranges = ranges.SelectMany(range => Subtract(range, removal)).ToArray();
        }

        return ranges.OrderBy(range => range.StartTime).ThenBy(range => range.EndTime).ToArray();
    }

    public static bool Contains(IReadOnlyCollection<ProfessionalLocalTimeRange> ranges,
        TimeOnly startTime, TimeOnly endTime)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        return endTime > startTime && ranges.Any(range =>
            range.StartTime <= startTime && endTime <= range.EndTime);
    }

    private static IEnumerable<ProfessionalLocalTimeRange> Intersect(
        IEnumerable<ProfessionalLocalTimeRange> custom,
        IEnumerable<ProfessionalLocalTimeRange> global)
    {
        var globalRanges = global.ToArray();
        foreach (var customRange in custom)
        foreach (var globalRange in globalRanges)
        {
            var start = customRange.StartTime > globalRange.StartTime ? customRange.StartTime : globalRange.StartTime;
            var end = customRange.EndTime < globalRange.EndTime ? customRange.EndTime : globalRange.EndTime;
            if (end > start) yield return new ProfessionalLocalTimeRange(start, end);
        }
    }

    private static IEnumerable<ProfessionalLocalTimeRange> Subtract(
        ProfessionalLocalTimeRange source,
        ProfessionalLocalTimeRange removal)
    {
        if (removal.EndTime <= source.StartTime || removal.StartTime >= source.EndTime)
        {
            yield return source;
            yield break;
        }

        if (removal.StartTime > source.StartTime)
            yield return new ProfessionalLocalTimeRange(source.StartTime, removal.StartTime);
        if (removal.EndTime < source.EndTime)
            yield return new ProfessionalLocalTimeRange(removal.EndTime, source.EndTime);
    }
}

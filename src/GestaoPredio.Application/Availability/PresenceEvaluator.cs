using GestaoPredio.Domain.Availability;
using GestaoPredio.Domain.Professionals;

namespace GestaoPredio.Application.Availability;

/// <summary>
/// Pure computation of whether a professional's physical presence is effective right now.
/// No state, no scheduler: presence ends by calculation at the establishment's last close
/// for the civil day the presence was opened on (never by the professional's personal
/// availability). Fail-closed when OperatingHours is not configured for that day.
/// </summary>
public static class PresenceEvaluator
{
    public static bool IsEffective(
        ProfessionalPresence? openPresence,
        IReadOnlyCollection<OperatingHourInterval> operatingHoursForCivilDay,
        DateTimeOffset now,
        TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(operatingHoursForCivilDay);
        ArgumentNullException.ThrowIfNull(zone);

        if (openPresence is null || openPresence.EndedAt is not null) return false;

        var localNow = TimeZoneInfo.ConvertTime(now, zone).DateTime;
        var localStart = TimeZoneInfo.ConvertTime(openPresence.StartedAt, zone).DateTime;
        if (localNow.Date != localStart.Date) return false;

        var today = operatingHoursForCivilDay.Where(interval => interval.DayOfWeek == localNow.DayOfWeek).ToArray();
        if (today.Length == 0) return false;

        var lastClose = today.Max(interval => interval.ClosesAt);
        return TimeOnly.FromDateTime(localNow) <= lastClose;
    }

    /// <summary>
    /// The UTC instant of the day's last <c>ClosesAt</c>, for opportunistic materialisation.
    /// Null when the establishment has no OperatingHours for that civil day.
    /// </summary>
    public static DateTimeOffset? OperatingHoursEndInstant(
        DateOnly civilDay,
        IReadOnlyCollection<OperatingHourInterval> operatingHoursForCivilDay,
        TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(operatingHoursForCivilDay);
        ArgumentNullException.ThrowIfNull(zone);

        var today = operatingHoursForCivilDay.Where(interval => interval.DayOfWeek == civilDay.DayOfWeek).ToArray();
        if (today.Length == 0) return null;

        var lastClose = today.Max(interval => interval.ClosesAt);
        var localClose = DateTime.SpecifyKind(civilDay.ToDateTime(lastClose), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localClose, zone), TimeSpan.Zero);
    }
}

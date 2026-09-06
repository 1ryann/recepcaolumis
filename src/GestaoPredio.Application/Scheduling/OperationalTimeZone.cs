namespace GestaoPredio.Application.Scheduling;

public static class OperationalTimeZone
{
    public static TimeZoneInfo Resolve(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            throw new ArgumentException("O fuso operacional deve ser informado.", nameof(timeZoneId));
        return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
    }

    public static OccurrencePeriod GetCivilDayInterval(DateOnly date, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        var localStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var localEnd = date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new OccurrencePeriod(ToUtc(localStart, timeZone), ToUtc(localEnd, timeZone));
    }

    internal static DateTimeOffset ToUtc(DateTime local, TimeZoneInfo timeZone)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(unspecified))
            throw new ArgumentException("O horário informado não existe no fuso operacional.", nameof(local));
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(unspecified, timeZone), TimeSpan.Zero);
    }
}

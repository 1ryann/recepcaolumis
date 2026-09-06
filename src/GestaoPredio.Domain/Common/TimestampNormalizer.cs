namespace GestaoPredio.Domain.Common;

internal static class TimestampNormalizer
{
    public static DateTimeOffset ToUtcMicroseconds(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var ticks = utc.Ticks - utc.Ticks % TimeSpan.TicksPerMicrosecond;
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}

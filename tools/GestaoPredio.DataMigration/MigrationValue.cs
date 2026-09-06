namespace GestaoPredio.DataMigration;

public static class MigrationValue
{
    public static object Normalize(object value) => value switch
    {
        DateTime dateTime => NormalizeDateTime(dateTime),
        DateTimeOffset dateTimeOffset => NormalizeDateTimeOffset(dateTimeOffset),
        _ => value
    };

    private static DateTime NormalizeDateTime(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
        return new DateTime(TruncateToMicroseconds(utc.Ticks), DateTimeKind.Utc);
    }

    private static DateTimeOffset NormalizeDateTimeOffset(DateTimeOffset value) =>
        new(TruncateToMicroseconds(value.UtcTicks), TimeSpan.Zero);

    private static long TruncateToMicroseconds(long ticks) => ticks - ticks % 10;
}

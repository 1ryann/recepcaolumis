namespace GestaoPredio.DataMigration;

public static class MigrationValue
{
    public static object Normalize(object value) => value switch
    {
        DateTime dateTime => NormalizeDateTime(dateTime),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime(),
        _ => value
    };

    private static DateTime NormalizeDateTime(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}

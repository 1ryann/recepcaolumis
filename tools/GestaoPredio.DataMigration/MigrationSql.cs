namespace GestaoPredio.DataMigration;

public static class MigrationSql
{
    public static string SourceSelect(MigrationTable table) =>
        $"SELECT {string.Join(", ", table.Columns.Select(SqlServerIdentifier))} " +
        $"FROM [dbo].{SqlServerIdentifier(table.Name)} ORDER BY {string.Join(", ", table.KeyColumns.Select(SqlServerIdentifier))};";

    public static string TargetSelect(MigrationTable table) =>
        $"SELECT {string.Join(", ", table.Columns.Select(PostgreSqlIdentifier))} " +
        $"FROM {PostgreSqlIdentifier(table.Name)} ORDER BY {string.Join(", ", table.KeyColumns.Select(PostgreSqlIdentifier))};";

    public static string TargetInsert(MigrationTable table) =>
        $"INSERT INTO {PostgreSqlIdentifier(table.Name)} " +
        $"({string.Join(", ", table.Columns.Select(PostgreSqlIdentifier))}) VALUES " +
        $"({string.Join(", ", table.Columns.Select((_, index) => $"@p{index}"))});";

    public static string SourceCount(MigrationTable table) =>
        $"SELECT COUNT_BIG(*) FROM [dbo].{SqlServerIdentifier(table.Name)};";

    public static string TargetCount(MigrationTable table) =>
        $"SELECT COUNT(*) FROM {PostgreSqlIdentifier(table.Name)};";

    public static string TargetDelete(MigrationTable table) =>
        $"DELETE FROM {PostgreSqlIdentifier(table.Name)};";

    private static string SqlServerIdentifier(string value) => $"[{value.Replace("]", "]]", StringComparison.Ordinal)}]";
    private static string PostgreSqlIdentifier(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}

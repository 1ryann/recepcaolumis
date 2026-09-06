using Microsoft.Data.SqlClient;
using Npgsql;

namespace GestaoPredio.DataMigration;

public sealed record DatabaseInventory(
    string Provider,
    string Database,
    string Version,
    IReadOnlyList<string> Tables,
    IReadOnlyDictionary<string, long> Counts,
    IReadOnlyList<string> Migrations);

public static class DatabaseInventoryReader
{
    public const string ApprovedSourceServer = "SOPH-SISPONTO\\SQLEXPRESS";
    public static readonly string[] ExpectedPostgreSqlMigrations =
        ["20260905234344_PostgreSqlBaseline", "20260906034221_LeasesFoundation"];

    public static async Task<DatabaseInventory> ReadSourceAsync(string connectionString,
        CancellationToken cancellationToken = default)
    {
        MigrationGuard.ValidateSource(connectionString);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var identity = new SqlCommand(
            "SELECT CAST(SERVERPROPERTY('ServerName') AS nvarchar(256)), DB_NAME(), CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128));",
            connection);
        await using var identityReader = await identity.ExecuteReaderAsync(cancellationToken);
        await identityReader.ReadAsync(cancellationToken);
        var server = identityReader.GetString(0);
        var database = identityReader.GetString(1);
        var version = identityReader.GetString(2);
        await identityReader.CloseAsync();
        if (!server.Equals(ApprovedSourceServer, StringComparison.OrdinalIgnoreCase) ||
            !database.Equals(MigrationGuard.ApprovedSourceDatabase, StringComparison.Ordinal))
            throw new MigrationSafetyException("Connected SQL Server is not SOPH-SISPONTO\\SQLEXPRESS / GestaoPredioDB.");

        var tables = await ReadSqlServerTablesAsync(connection, cancellationToken);
        EnsureSourceTables(tables);
        var counts = await ReadSqlServerCountsAsync(connection, tables, cancellationToken);
        var migrations = tables.Contains("__EFMigrationsHistory", StringComparer.Ordinal)
            ? await ReadSqlServerMigrationsAsync(connection, cancellationToken)
            : [];
        return new DatabaseInventory("SQL Server", database, MajorVersion(version), tables, counts, migrations);
    }

    public static async Task<DatabaseInventory> ReadTargetAsync(string connectionString,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var identity = new NpgsqlCommand("SELECT current_database(), current_setting('server_version');", connection);
        await using var identityReader = await identity.ExecuteReaderAsync(cancellationToken);
        await identityReader.ReadAsync(cancellationToken);
        var database = identityReader.GetString(0);
        var version = identityReader.GetString(1);
        await identityReader.CloseAsync();
        if (database.Equals("LumisDev", StringComparison.OrdinalIgnoreCase))
            throw new MigrationSafetyException("LumisDev cannot be used as the production target.");

        var tables = await ReadPostgreSqlTablesAsync(connection, cancellationToken);
        EnsureTargetTablesAreKnown(tables);
        var counts = await ReadPostgreSqlCountsAsync(connection, tables, cancellationToken);
        var migrations = tables.Contains("__EFMigrationsHistory", StringComparer.Ordinal)
            ? await ReadPostgreSqlMigrationsAsync(connection, cancellationToken)
            : [];
        return new DatabaseInventory("PostgreSQL", database, MajorVersion(version), tables, counts, migrations);
    }

    public static void EnsureTargetSchemaReady(DatabaseInventory inventory)
    {
        var expected = ExpectedTargetTables();
        if (!expected.SetEquals(inventory.Tables))
            throw new MigrationSafetyException("Target public schema is empty, partial, or differs from the approved PostgreSQL model.");
        if (!ExpectedPostgreSqlMigrations.SequenceEqual(inventory.Migrations, StringComparer.Ordinal))
            throw new MigrationSafetyException("Target migration history does not match the approved PostgreSQL chain.");
    }

    public static void EnsureTargetEmptyForInitialLoad(DatabaseInventory inventory)
    {
        EnsureTargetSchemaReady(inventory);
        var nonEmpty = inventory.Counts.Where(x => x.Key != "__EFMigrationsHistory" && x.Value != 0).Select(x => x.Key).ToArray();
        if (nonEmpty.Length != 0)
            throw new MigrationSafetyException("Target already contains application data; initial load was refused.");
    }

    public static HashSet<string> ExpectedTargetTables() => MigrationManifest.Tables.Select(x => x.Name)
        .Concat(["Tenants", "Leases", "LeaseOccurrences", "__EFMigrationsHistory"])
        .ToHashSet(StringComparer.Ordinal);

    private static void EnsureSourceTables(IReadOnlyList<string> tables)
    {
        var required = MigrationManifest.Tables.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        var actual = tables.Where(x => x != "__EFMigrationsHistory").ToHashSet(StringComparer.Ordinal);
        var missing = required.Except(actual).ToArray();
        var unexpected = actual.Except(required).ToArray();
        if (missing.Length != 0 || unexpected.Length != 0)
            throw new MigrationSafetyException(
                $"Source schema is not fully mapped. Missing={missing.Length}; Unexpected={unexpected.Length}.");
    }

    private static void EnsureTargetTablesAreKnown(IReadOnlyList<string> tables)
    {
        var unexpected = tables.Except(ExpectedTargetTables(), StringComparer.Ordinal).ToArray();
        if (unexpected.Length != 0)
            throw new MigrationSafetyException($"Target public schema contains {unexpected.Length} unexpected table(s).");
    }

    private static async Task<List<string>> ReadSqlServerTablesAsync(SqlConnection connection, CancellationToken ct)
    {
        await using var command = new SqlCommand(
            "SELECT t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE s.name='dbo' ORDER BY t.name;", connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<string>();
        while (await reader.ReadAsync(ct)) result.Add(reader.GetString(0));
        return result;
    }

    private static async Task<List<string>> ReadPostgreSqlTablesAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE' ORDER BY table_name;", connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<string>();
        while (await reader.ReadAsync(ct)) result.Add(reader.GetString(0));
        return result;
    }

    private static async Task<Dictionary<string, long>> ReadSqlServerCountsAsync(SqlConnection connection,
        IEnumerable<string> tables, CancellationToken ct)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            await using var command = new SqlCommand($"SELECT COUNT_BIG(*) FROM [dbo].[{table.Replace("]", "]]", StringComparison.Ordinal)}];", connection);
            result[table] = Convert.ToInt64(await command.ExecuteScalarAsync(ct));
        }
        return result;
    }

    private static async Task<Dictionary<string, long>> ReadPostgreSqlCountsAsync(NpgsqlConnection connection,
        IEnumerable<string> tables, CancellationToken ct)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            await using var command = new NpgsqlCommand($"SELECT COUNT(*) FROM \"{table.Replace("\"", "\"\"", StringComparison.Ordinal)}\";", connection);
            result[table] = Convert.ToInt64(await command.ExecuteScalarAsync(ct));
        }
        return result;
    }

    private static async Task<List<string>> ReadSqlServerMigrationsAsync(SqlConnection connection, CancellationToken ct)
    {
        await using var command = new SqlCommand("SELECT [MigrationId] FROM [dbo].[__EFMigrationsHistory] ORDER BY [MigrationId];", connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<string>();
        while (await reader.ReadAsync(ct)) result.Add(reader.GetString(0));
        return result;
    }

    private static async Task<List<string>> ReadPostgreSqlMigrationsAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\";", connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<string>();
        while (await reader.ReadAsync(ct)) result.Add(reader.GetString(0));
        return result;
    }

    private static string MajorVersion(string version) => version.Split('.', StringSplitOptions.RemoveEmptyEntries)[0];
}

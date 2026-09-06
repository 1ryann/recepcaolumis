using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace GestaoPredio.DataMigration;

public sealed record TableReconciliation(string Table, long SourceCount, long TargetCount, bool ContentMatches);

public static class DataMigrationRunner
{
    public static async Task<IReadOnlyList<TableReconciliation>> ExecuteInitialLoadAsync(
        string sourceConnectionString, string targetConnectionString, CancellationToken cancellationToken = default)
    {
        var sourceInventory = await DatabaseInventoryReader.ReadSourceAsync(sourceConnectionString, cancellationToken);
        var targetInventory = await DatabaseInventoryReader.ReadTargetAsync(targetConnectionString, cancellationToken);
        DatabaseInventoryReader.EnsureTargetEmptyForInitialLoad(targetInventory);

        await using var source = new SqlConnection(sourceConnectionString);
        await source.OpenAsync(cancellationToken);
        var sourceSnapshot = new Dictionary<string, List<object?[]>>(StringComparer.Ordinal);
        foreach (var table in MigrationManifest.Tables)
            sourceSnapshot[table.Name] = await ReadRowsAsync(
                new SqlCommand(MigrationSql.SourceSelect(table), source), cancellationToken);

        await using var target = new NpgsqlConnection(targetConnectionString);
        await target.OpenAsync(cancellationToken);
        await using var transaction = await target.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            foreach (var table in MigrationManifest.Tables)
                await CopyRowsAsync(target, transaction, table, sourceSnapshot[table.Name], cancellationToken);
            foreach (var table in MigrationManifest.Tables.Where(x => x.HasGeneratedIntegerKey))
                await ResetSequenceAsync(target, transaction, table, cancellationToken);

            foreach (var table in MigrationManifest.Tables)
            {
                var latestSource = await ReadRowsAsync(
                    new SqlCommand(MigrationSql.SourceSelect(table), source), cancellationToken);
                if (MigrationDigest.Compute(sourceSnapshot[table.Name]) != MigrationDigest.Compute(latestSource))
                    throw new MigrationSafetyException(
                        $"Source table {table.Name} changed during capture; target transaction was rolled back.");

                var targetCommand = new NpgsqlCommand(MigrationSql.TargetSelect(table), target, transaction);
                var copiedTarget = await ReadRowsAsync(targetCommand, cancellationToken);
                if (MigrationDigest.Compute(sourceSnapshot[table.Name]) != MigrationDigest.Compute(copiedTarget))
                    throw new MigrationSafetyException(
                        $"Target verification failed for {table.Name}; target transaction was rolled back.");
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return await ReconcileAsync(sourceConnectionString, targetConnectionString, cancellationToken);
    }

    public static async Task<IReadOnlyList<TableReconciliation>> ReconcileAsync(
        string sourceConnectionString, string targetConnectionString, CancellationToken cancellationToken = default)
    {
        await DatabaseInventoryReader.ReadSourceAsync(sourceConnectionString, cancellationToken);
        var targetInventory = await DatabaseInventoryReader.ReadTargetAsync(targetConnectionString, cancellationToken);
        DatabaseInventoryReader.EnsureTargetSchemaReady(targetInventory);
        await using var source = new SqlConnection(sourceConnectionString);
        await using var target = new NpgsqlConnection(targetConnectionString);
        await source.OpenAsync(cancellationToken);
        await target.OpenAsync(cancellationToken);
        var result = new List<TableReconciliation>();
        foreach (var table in MigrationManifest.Tables)
        {
            var sourceRows = await ReadRowsAsync(new SqlCommand(MigrationSql.SourceSelect(table), source), cancellationToken);
            var targetRows = await ReadRowsAsync(new NpgsqlCommand(MigrationSql.TargetSelect(table), target), cancellationToken);
            result.Add(new TableReconciliation(table.Name, sourceRows.Count, targetRows.Count,
                sourceRows.Count == targetRows.Count && MigrationDigest.Compute(sourceRows) == MigrationDigest.Compute(targetRows)));
        }
        return result;
    }

    public static async Task<int> CountTargetForeignKeyErrorsAsync(string targetConnectionString,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
              (SELECT COUNT(*) FROM "AspNetUserRoles" ur LEFT JOIN "AspNetUsers" u ON u."Id"=ur."UserId" LEFT JOIN "AspNetRoles" r ON r."Id"=ur."RoleId" WHERE u."Id" IS NULL OR r."Id" IS NULL) +
              (SELECT COUNT(*) FROM "AspNetUserClaims" c LEFT JOIN "AspNetUsers" u ON u."Id"=c."UserId" WHERE u."Id" IS NULL) +
              (SELECT COUNT(*) FROM "AspNetUserLogins" l LEFT JOIN "AspNetUsers" u ON u."Id"=l."UserId" WHERE u."Id" IS NULL) +
              (SELECT COUNT(*) FROM "AspNetUserTokens" t LEFT JOIN "AspNetUsers" u ON u."Id"=t."UserId" WHERE u."Id" IS NULL) +
              (SELECT COUNT(*) FROM "Professionals" p LEFT JOIN "AspNetUsers" u ON u."Id"=p."ApplicationUserId" WHERE p."ApplicationUserId" IS NOT NULL AND u."Id" IS NULL) +
              (SELECT COUNT(*) FROM "Professionals" p LEFT JOIN "PrivateFiles" f ON f."Id"=p."PhotoFileId" WHERE p."PhotoFileId" IS NOT NULL AND f."Id" IS NULL);
            """;
        await using var connection = new NpgsqlConnection(targetConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task CopyRowsAsync(NpgsqlConnection target, NpgsqlTransaction transaction,
        MigrationTable table, IEnumerable<object?[]> rows, CancellationToken ct)
    {
        await using var insert = new NpgsqlCommand(MigrationSql.TargetInsert(table), target, transaction);
        foreach (var row in rows)
        {
            insert.Parameters.Clear();
            for (var index = 0; index < table.Columns.Count; index++)
                insert.Parameters.Add(MigrationParameter.Create($"p{index}", row[index]));
            await insert.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task ResetSequenceAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        MigrationTable table, CancellationToken ct)
    {
        var tableName = table.Name.Replace("'", "''", StringComparison.Ordinal);
        const string key = "Id";
        var sql = $"SELECT setval(pg_get_serial_sequence('\"{tableName}\"', '{key}'), COALESCE(MAX(\"{key}\"), 1), MAX(\"{key}\") IS NOT NULL) FROM \"{tableName}\";";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<List<object?[]>> ReadRowsAsync(IDbCommand command, CancellationToken ct)
    {
        await using var disposable = (IAsyncDisposable)command;
        await using DbDataReader reader = command switch
        {
            SqlCommand sql => await sql.ExecuteReaderAsync(ct),
            NpgsqlCommand postgres => await postgres.ExecuteReaderAsync(ct),
            _ => throw new NotSupportedException()
        };
        var rows = new List<object?[]>();
        while (await reader.ReadAsync(ct))
        {
            var row = new object?[reader.FieldCount];
            for (var index = 0; index < row.Length; index++)
                row[index] = reader.IsDBNull(index) ? null : MigrationValue.Normalize(reader.GetValue(index));
            rows.Add(row);
        }
        return rows;
    }
}

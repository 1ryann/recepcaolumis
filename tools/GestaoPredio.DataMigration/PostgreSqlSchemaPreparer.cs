using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GestaoPredio.DataMigration;

public static class PostgreSqlSchemaPreparer
{
    public static async Task PrepareAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        var before = await DatabaseInventoryReader.ReadTargetAsync(connectionString, cancellationToken);
        if (before.Tables.Count != 0)
        {
            DatabaseInventoryReader.EnsureTargetSchemaReady(before);
            return;
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
        var after = await DatabaseInventoryReader.ReadTargetAsync(connectionString, cancellationToken);
        DatabaseInventoryReader.EnsureTargetSchemaReady(after);
    }
}

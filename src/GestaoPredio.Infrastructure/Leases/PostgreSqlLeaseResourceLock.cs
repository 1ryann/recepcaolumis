using System.Data.Common;
using GestaoPredio.Application.Leases;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace GestaoPredio.Infrastructure.Leases;

public sealed class PostgreSqlLeaseResourceLock(ApplicationDbContext db) : ILeaseResourceLock
{
    private const int CommandTimeoutSeconds = 10;

    public async Task AcquireAsync(LeaseResourceLockRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var transaction = db.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("Lease resource locks require an active database transaction.");
        var connection = db.Database.GetDbConnection();

        await LockRowsAsync(connection, transaction, "Tenants", request.TenantIds, cancellationToken);
        await LockRowsAsync(connection, transaction, "Rooms", request.RoomIds, cancellationToken);
        await LockRowsAsync(connection, transaction, "Professionals", request.ProfessionalIds, cancellationToken);
    }

    public static IReadOnlyList<string> GetOrderedResources(LeaseResourceLockRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ordered("tenant", request.TenantIds)
            .Concat(Ordered("room", request.RoomIds))
            .Concat(Ordered("professional", request.ProfessionalIds))
            .ToArray();
    }

    private static IEnumerable<string> Ordered(string type, IEnumerable<Guid> ids) =>
        ids.Distinct()
            .Select(id => id.ToString("D"))
            .Order(StringComparer.Ordinal)
            .Select(id => $"{type}:{id}");

    private static async Task LockRowsAsync(
        DbConnection connection,
        DbTransaction transaction,
        string table,
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        var orderedIds = ids.Distinct()
            .OrderBy(id => id.ToString("D"), StringComparer.Ordinal)
            .ToArray();
        if (orderedIds.Length == 0) return;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = $"SELECT \"Id\" FROM \"{table}\" WHERE \"Id\" = ANY (@ids) ORDER BY \"Id\" FOR UPDATE";
        command.Parameters.Add(new NpgsqlParameter<Guid[]>("ids", orderedIds));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            // Reading all rows ensures every selected resource lock is acquired before proceeding.
        }
    }
}

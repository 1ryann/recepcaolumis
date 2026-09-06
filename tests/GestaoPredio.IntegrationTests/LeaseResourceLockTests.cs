using GestaoPredio.Application.Leases;
using GestaoPredio.Infrastructure.Leases;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GestaoPredio.IntegrationTests;

public sealed class LeaseResourceLockTests : IAsyncLifetime
{
    private readonly string _baseConnection = LocalPostgreSqlTestDatabase.LoadBaseConnection();
    private readonly string _schema = $"{LocalPostgreSqlTestDatabase.SchemaPrefix}{Guid.NewGuid():N}";
    private string ConnectionString => LocalPostgreSqlTestDatabase.WithSchema(_baseConnection, _schema);

    public async Task InitializeAsync()
    {
        await LocalPostgreSqlTestDatabase.CreateSchemaAsync(_baseConnection, _schema);
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE "Tenants" ("Id" uuid PRIMARY KEY);
            CREATE TABLE "Rooms" ("Id" uuid PRIMARY KEY);
            CREATE TABLE "Professionals" ("Id" uuid PRIMARY KEY);
            CREATE TABLE "Leases" (
                "Id" uuid PRIMARY KEY,
                "RoomId" uuid NOT NULL,
                "ProfessionalId" uuid NOT NULL,
                "LifecycleState" character varying(20) NOT NULL,
                "OccupancyStartAt" timestamp with time zone NOT NULL,
                "OccupancyEndAt" timestamp with time zone NULL
            );
            """;
        await command.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => LocalPostgreSqlTestDatabase.DropSchemaAsync(_baseConnection, _schema);

    [Fact]
    public void Resource_order_is_fixed_by_type_then_guid_without_duplicates()
    {
        var low = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var high = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var request = new LeaseResourceLockRequest([high, low, high], [high, low], [high, low]);

        var ordered = PostgreSqlLeaseResourceLock.GetOrderedResources(request);

        Assert.Equal(
            [
                $"tenant:{low:D}", $"tenant:{high:D}",
                $"room:{low:D}", $"room:{high:D}",
                $"professional:{low:D}", $"professional:{high:D}"
            ],
            ordered);
    }

    [Fact]
    public async Task Second_transaction_waits_for_the_first_lock_on_the_same_resources()
    {
        var roomId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        await using (var seed = CreateDbContext())
            await seed.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO \"Rooms\" (\"Id\") VALUES ({roomId}); INSERT INTO \"Professionals\" (\"Id\") VALUES ({professionalId})");

        await using var firstDb = CreateDbContext();
        await using var secondDb = CreateDbContext();
        await using var firstTransaction = await firstDb.Database.BeginTransactionAsync();
        await using var secondTransaction = await secondDb.Database.BeginTransactionAsync();
        var request = new LeaseResourceLockRequest([], [roomId], [professionalId]);

        await new PostgreSqlLeaseResourceLock(firstDb).AcquireAsync(request, CancellationToken.None);
        var secondLock = new PostgreSqlLeaseResourceLock(secondDb).AcquireAsync(request, CancellationToken.None);

        await Task.Delay(200);
        Assert.False(secondLock.IsCompleted);

        await firstTransaction.CommitAsync();
        await secondLock.WaitAsync(TimeSpan.FromSeconds(5));
        await secondTransaction.RollbackAsync();
    }

    [Fact]
    public async Task Conflict_query_treats_adjacent_intervals_as_available_and_overlap_as_conflict()
    {
        var roomId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var start = new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
        var end = start.AddHours(1);
        await using var db = CreateDbContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Leases" ("Id", "RoomId", "ProfessionalId", "LifecycleState", "OccupancyStartAt", "OccupancyEndAt")
            VALUES ({Guid.NewGuid()}, {roomId}, {professionalId}, {'O' + "PEN"}, {start}, {end})
            """);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var detector = new PostgreSqlLeaseConflictDetector(db);

        var adjacent = await detector.FindConflictAsync(
            roomId, professionalId, end, end.AddHours(1), null, CancellationToken.None);
        var overlapping = await detector.FindConflictAsync(
            roomId, professionalId, end.AddMinutes(-1), end.AddHours(1), null, CancellationToken.None);

        Assert.False(adjacent.Any);
        Assert.True(overlapping.Room);
        Assert.True(overlapping.Professional);
        await transaction.RollbackAsync();
    }

    private ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }
}

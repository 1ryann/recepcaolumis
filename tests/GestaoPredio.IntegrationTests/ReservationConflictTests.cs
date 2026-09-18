using GestaoPredio.Domain.Leases;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Tenants;
using GestaoPredio.Infrastructure.Persistence;
using GestaoPredio.Infrastructure.Reservations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class ReservationConflictTests(ModulesApiFactory factory)
{
    [Fact]
    public async Task Detector_uses_approved_reservations_and_ignores_pending_requests()
    {
        await factory.ResetAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = factory.UtcNow;
        var room = Room.Create("Sala Reserva", null, 10, 50, now);
        var professional = Professional.Create("Ana Reserva", "Fisioterapia", "+5565999999999", now);
        db.AddRange(room, professional);
        var pending = Reservation.RequestNew(room.Id, professional.Id, now.AddDays(2), now.AddDays(2).AddHours(1), "requester", now);
        db.Reservations.Add(pending);
        await db.SaveChangesAsync();

        await using var transaction = await db.Database.BeginTransactionAsync();
        var detector = new PostgreSqlReservationConflictDetector(db);
        var withoutApproved = await detector.FindConflictAsync(
            room.Id, professional.Id, pending.StartAt, pending.EndAt, null, CancellationToken.None);
        pending.Approve("admin", now);
        await db.SaveChangesAsync();
        var withApproved = await detector.FindConflictAsync(
            room.Id, professional.Id, pending.StartAt, pending.EndAt, null, CancellationToken.None);

        Assert.False(withoutApproved.Any);
        Assert.True(withApproved.Room);
        Assert.True(withApproved.Professional);
        Assert.False(withApproved.Lease);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Detector_treats_lease_and_planned_occurrence_as_blocking_resources()
    {
        await factory.ResetAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = factory.UtcNow;
        var room = Room.Create("Sala Locação", null, 10, 50, now);
        var professional = Professional.Create("Bruno Locação", "Psicologia", "+5565988888888", now);
        var tenant = Tenant.Create("Locatário", TenantKind.Individual, now);
        var start = now.AddDays(3);
        var end = start.AddHours(2);
        var lease = Lease.Create(tenant.Id, professional.Id, room.Id, LeaseMode.Hourly, 100,
            start, null, start, end, null, now);
        var occurrence = LeaseOccurrence.Create(lease.Id, start, end, now);
        db.AddRange(room, professional, tenant, lease, occurrence);
        await db.SaveChangesAsync();

        await using var transaction = await db.Database.BeginTransactionAsync();
        var detector = new PostgreSqlReservationConflictDetector(db);
        var conflict = await detector.FindConflictAsync(
            room.Id, professional.Id, start.AddMinutes(30), end.AddHours(1), null, CancellationToken.None);
        var adjacent = await detector.FindConflictAsync(
            room.Id, professional.Id, end, end.AddHours(1), null, CancellationToken.None);

        Assert.True(conflict.Room);
        Assert.True(conflict.Professional);
        Assert.True(conflict.Lease);
        Assert.False(adjacent.Any);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Detector_requires_an_active_transaction()
    {
        await factory.ResetAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var detector = new PostgreSqlReservationConflictDetector(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => detector.FindConflictAsync(
            Guid.NewGuid(), Guid.NewGuid(), factory.UtcNow, factory.UtcNow.AddHours(1),
            null, CancellationToken.None));
    }
}

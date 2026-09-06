using GestaoPredio.Application.Leases;
using GestaoPredio.Domain.Leases;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GestaoPredio.Infrastructure.Leases;

public sealed class PostgreSqlLeaseConflictDetector(ApplicationDbContext db) : ILeaseConflictDetector
{
    public async Task<LeaseResourceConflict> FindConflictAsync(
        Guid roomId,
        Guid professionalId,
        DateTimeOffset occupancyStartAt,
        DateTimeOffset? occupancyEndAt,
        Guid? excludedLeaseId,
        CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Lease conflict detection requires an active database transaction.");

        var blocking = db.Leases
            .AsNoTracking()
            .Where(lease => lease.LifecycleState == LeaseLifecycleState.Open ||
                            lease.LifecycleState == LeaseLifecycleState.EndingPending)
            .Where(lease => excludedLeaseId == null || lease.Id != excludedLeaseId)
            .Where(lease => lease.LifecycleState == LeaseLifecycleState.EndingPending ||
                ((lease.OccupancyEndAt == null || occupancyStartAt < lease.OccupancyEndAt) &&
                 (occupancyEndAt == null || lease.OccupancyStartAt < occupancyEndAt)));

        var roomConflict = await blocking.AnyAsync(lease => lease.RoomId == roomId, cancellationToken);
        var professionalConflict = await blocking.AnyAsync(
            lease => lease.ProfessionalId == professionalId, cancellationToken);
        return new LeaseResourceConflict(roomConflict, professionalConflict);
    }
}

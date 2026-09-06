using GestaoPredio.Application.Reservations;
using GestaoPredio.Domain.Leases;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GestaoPredio.Infrastructure.Reservations;

public sealed class PostgreSqlReservationConflictDetector(ApplicationDbContext db)
    : IReservationConflictDetector
{
    public async Task<ReservationResourceConflict> FindConflictAsync(
        Guid roomId,
        Guid professionalId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        Guid? excludedReservationId,
        CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Reservation conflict detection requires an active database transaction.");

        var reservations = db.Reservations
            .AsNoTracking()
            .Where(reservation => reservation.Status == ReservationStatus.Approved &&
                                  reservation.Kind != ReservationKind.Cancellation)
            .Where(reservation => excludedReservationId == null || reservation.Id != excludedReservationId)
            .Where(reservation => startAt < reservation.EndAt && reservation.StartAt < endAt);

        var blockingLeases = db.Leases
            .AsNoTracking()
            .Where(lease => lease.LifecycleState == LeaseLifecycleState.Open ||
                            lease.LifecycleState == LeaseLifecycleState.EndingPending)
            .Where(lease => lease.LifecycleState == LeaseLifecycleState.EndingPending ||
                            ((lease.OccupancyEndAt == null || startAt < lease.OccupancyEndAt) &&
                             lease.OccupancyStartAt < endAt));

        var blockingOccurrences =
            from occurrence in db.LeaseOccurrences.AsNoTracking()
            join lease in db.Leases.AsNoTracking() on occurrence.LeaseId equals lease.Id
            where occurrence.State == LeaseOccurrenceState.Planned
                  && (lease.LifecycleState == LeaseLifecycleState.Open ||
                      lease.LifecycleState == LeaseLifecycleState.EndingPending)
                  && startAt < occurrence.EndAt
                  && occurrence.StartAt < endAt
            select new { lease.RoomId, lease.ProfessionalId };

        var reservationRoom = await reservations.AnyAsync(
            reservation => reservation.RoomId == roomId, cancellationToken);
        var reservationProfessional = await reservations.AnyAsync(
            reservation => reservation.ProfessionalId == professionalId, cancellationToken);
        var leaseRoom = await blockingLeases.AnyAsync(lease => lease.RoomId == roomId, cancellationToken) ||
                        await blockingOccurrences.AnyAsync(occurrence => occurrence.RoomId == roomId, cancellationToken);
        var leaseProfessional = await blockingLeases.AnyAsync(
                                    lease => lease.ProfessionalId == professionalId, cancellationToken) ||
                                await blockingOccurrences.AnyAsync(
                                    occurrence => occurrence.ProfessionalId == professionalId, cancellationToken);

        return new ReservationResourceConflict(
            reservationRoom || leaseRoom,
            reservationProfessional || leaseProfessional,
            leaseRoom || leaseProfessional);
    }
}

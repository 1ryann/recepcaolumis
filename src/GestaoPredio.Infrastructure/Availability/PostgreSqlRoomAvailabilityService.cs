using GestaoPredio.Application.Availability;
using GestaoPredio.Domain.Availability;
using GestaoPredio.Domain.Leases;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GestaoPredio.Infrastructure.Availability;

public sealed class PostgreSqlRoomAvailabilityService(
    ApplicationDbContext db,
    OperatingHoursEvaluator operatingHours) : IRoomAvailabilityService
{
    public async Task<RoomAvailabilityConflict> CheckScheduleAndBlocksAsync(Guid roomId,
        DateTimeOffset startAt, DateTimeOffset? endAt, Guid? excludedRoomBlockId,
        bool enforceOperatingHours, CancellationToken cancellationToken)
    {
        EnsureTransaction();
        if (enforceOperatingHours && await db.OperatingHoursSchedules.AsNoTracking()
                .AnyAsync(cancellationToken))
        {
            var intervals = await db.OperatingHourIntervals.AsNoTracking().ToListAsync(cancellationToken);
            if (endAt is null || !operatingHours.Contains(intervals, startAt, endAt.Value))
                return RoomAvailabilityConflict.OutsideOperatingHours;
        }

        var blocked = await db.RoomBlocks.AsNoTracking().AnyAsync(block =>
            block.RoomId == roomId && block.Status == RoomBlockStatus.Active &&
            (excludedRoomBlockId == null || block.Id != excludedRoomBlockId) &&
            startAt < block.EndAt && (endAt == null || block.StartAt < endAt), cancellationToken);
        return blocked ? RoomAvailabilityConflict.RoomBlock : RoomAvailabilityConflict.None;
    }

    public async Task<RoomAvailabilityConflict> CheckBlockConflictsAsync(Guid roomId,
        DateTimeOffset startAt, DateTimeOffset endAt, Guid? excludedRoomBlockId,
        CancellationToken cancellationToken)
    {
        var scheduleOrBlock = await CheckScheduleAndBlocksAsync(roomId, startAt, endAt,
            excludedRoomBlockId, enforceOperatingHours: false, cancellationToken);
        if (scheduleOrBlock != RoomAvailabilityConflict.None) return scheduleOrBlock;

        var reservation = await db.Reservations.AsNoTracking().AnyAsync(value =>
            value.RoomId == roomId && value.Status == ReservationStatus.Approved &&
            value.Kind != ReservationKind.Cancellation && startAt < value.EndAt && value.StartAt < endAt,
            cancellationToken);
        if (reservation) return RoomAvailabilityConflict.Reservation;

        var lease = await db.Leases.AsNoTracking().AnyAsync(value =>
            value.RoomId == roomId &&
            (value.LifecycleState == LeaseLifecycleState.Open ||
             value.LifecycleState == LeaseLifecycleState.EndingPending) &&
            (value.OccupancyEndAt == null || startAt < value.OccupancyEndAt) &&
            value.OccupancyStartAt < endAt, cancellationToken);
        if (lease) return RoomAvailabilityConflict.Lease;

        var occurrence = await (
            from value in db.LeaseOccurrences.AsNoTracking()
            join parent in db.Leases.AsNoTracking() on value.LeaseId equals parent.Id
            where parent.RoomId == roomId && value.State == LeaseOccurrenceState.Planned &&
                  (parent.LifecycleState == LeaseLifecycleState.Open ||
                   parent.LifecycleState == LeaseLifecycleState.EndingPending) &&
                  startAt < value.EndAt && value.StartAt < endAt
            select value.Id).AnyAsync(cancellationToken);
        return occurrence ? RoomAvailabilityConflict.Lease : RoomAvailabilityConflict.None;
    }

    public async Task<bool> CanApplyScheduleAsync(IReadOnlyCollection<OperatingHourInterval> proposedIntervals,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        EnsureTransaction();
        await foreach (var period in db.Reservations.AsNoTracking()
                           .Where(value => value.Status == ReservationStatus.Approved &&
                                           value.Kind != ReservationKind.Cancellation && value.EndAt > now)
                           .Select(value => new { value.StartAt, value.EndAt })
                           .AsAsyncEnumerable().WithCancellation(cancellationToken))
            if (!operatingHours.Contains(proposedIntervals, period.StartAt, period.EndAt)) return false;

        await foreach (var period in db.Leases.AsNoTracking()
                           .Where(value => value.Mode == LeaseMode.Hourly &&
                                           (value.LifecycleState == LeaseLifecycleState.Open ||
                                            value.LifecycleState == LeaseLifecycleState.EndingPending) &&
                                           value.OccupancyEndAt != null && value.OccupancyEndAt > now)
                           .Select(value => new { value.OccupancyStartAt, EndAt = value.OccupancyEndAt!.Value })
                           .AsAsyncEnumerable().WithCancellation(cancellationToken))
            if (!operatingHours.Contains(proposedIntervals, period.OccupancyStartAt, period.EndAt)) return false;

        return true;
    }

    private void EnsureTransaction()
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Availability checks for mutations require an active transaction.");
    }
}

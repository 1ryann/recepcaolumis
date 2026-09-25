using GestaoPredio.Application.Availability;
using GestaoPredio.Domain.Availability;
using GestaoPredio.Domain.Leases;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Visits;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using GestaoPredio.Application.Scheduling;

namespace GestaoPredio.Infrastructure.Availability;

public sealed class PostgreSqlRoomAvailabilityService(
    ApplicationDbContext db,
    OperatingHoursEvaluator operatingHours,
    TimeZoneInfo operationalTimeZone) : IRoomAvailabilityService
{
    public async Task<IReadOnlyList<RoomOperationalStatus>> ReadRoomOperationalStatusAsync(
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var current = now.ToUniversalTime();
        var rooms = await db.Rooms.AsNoTracking().Where(x => x.IsActive)
            .Select(x => new RoomOperationalStatusRow(x.Id, x.Name)).ToListAsync(cancellationToken);
        if (rooms.Count == 0) return [];
        var roomIds = rooms.Select(x => x.Id).ToArray();
        var blocks = await db.RoomBlocks.AsNoTracking().Where(x => roomIds.Contains(x.RoomId) &&
            x.Status == RoomBlockStatus.Active && x.StartAt <= current && x.EndAt > current)
            .Select(x => x.RoomId).ToListAsync(cancellationToken);
        var visits = await db.Visits.AsNoTracking().Where(x => x.RoomId != null && roomIds.Contains(x.RoomId.Value) &&
            (x.Status == VisitStatus.Waiting || x.Status == VisitStatus.InService))
            .Select(x => x.RoomId!.Value).ToListAsync(cancellationToken);
        var reservations = await db.Reservations.AsNoTracking().Where(x => roomIds.Contains(x.RoomId) &&
            x.Status == ReservationStatus.Approved && x.Kind != ReservationKind.Cancellation && x.EndAt > current)
            .Select(x => new { x.RoomId, x.StartAt }).ToListAsync(cancellationToken);
        var leases = await db.Leases.AsNoTracking().Where(x => roomIds.Contains(x.RoomId) &&
            (x.LifecycleState == LeaseLifecycleState.Open || x.LifecycleState == LeaseLifecycleState.EndingPending) &&
            (x.OccupancyEndAt == null || x.OccupancyEndAt > current))
            .Select(x => new { x.RoomId, x.OccupancyStartAt }).ToListAsync(cancellationToken);
        var occurrences = await (from occurrence in db.LeaseOccurrences.AsNoTracking()
                                 join lease in db.Leases.AsNoTracking() on occurrence.LeaseId equals lease.Id
                                 where roomIds.Contains(lease.RoomId) && occurrence.State == LeaseOccurrenceState.Planned &&
                                       occurrence.EndAt > current && lease.LifecycleState != LeaseLifecycleState.Cancelled &&
                                       lease.LifecycleState != LeaseLifecycleState.Ended
                                 select new { lease.RoomId, occurrence.StartAt }).ToListAsync(cancellationToken);
        var local = TimeZoneInfo.ConvertTime(current, operationalTimeZone);
        var closed = await IsClosedNowAsync(local, cancellationToken);
        return rooms.Select(room =>
        {
            var occupied = visits.Contains(room.Id) || leases.Any(x => x.RoomId == room.Id && x.OccupancyStartAt <= current) ||
                           reservations.Any(x => x.RoomId == room.Id && x.StartAt <= current) ||
                           occurrences.Any(x => x.RoomId == room.Id && x.StartAt <= current);
            var next = reservations.Where(x => x.RoomId == room.Id && x.StartAt > current).Select(x => x.StartAt)
                .Concat(leases.Where(x => x.RoomId == room.Id && x.OccupancyStartAt > current).Select(x => x.OccupancyStartAt))
                .Concat(occurrences.Where(x => x.RoomId == room.Id && x.StartAt > current).Select(x => x.StartAt))
                .OrderBy(x => x).Cast<DateTimeOffset?>().FirstOrDefault();
            var status = blocks.Contains(room.Id) ? "BLOCKED" : occupied ? "OCCUPIED" : next is not null ? "RESERVED" : closed ? "CLOSED" : "AVAILABLE";
            return new RoomOperationalStatus(room.Id, room.Name, status, next);
        }).OrderBy(x => x.RoomName).ThenBy(x => x.RoomId).ToArray();
    }

    private async Task<bool> IsClosedNowAsync(DateTimeOffset local, CancellationToken cancellationToken)
    {
        if (!await db.OperatingHoursSchedules.AsNoTracking().AnyAsync(cancellationToken)) return false;
        var time = TimeOnly.FromDateTime(local.DateTime);
        return !await db.OperatingHourIntervals.AsNoTracking().AnyAsync(x => x.DayOfWeek == local.DayOfWeek &&
            x.OpensAt <= time && x.ClosesAt > time, cancellationToken);
    }

    private sealed record RoomOperationalStatusRow(Guid Id, string Name);
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

    public async Task<RoomAvailabilityConflict> CheckLeaseRoomAsync(Guid roomId,
        DateTimeOffset startAt, DateTimeOffset? endAt, LeaseMode mode,
        CancellationToken cancellationToken)
    {
        var operational = await CheckScheduleAndBlocksAsync(roomId, startAt, endAt,
            null, enforceOperatingHours: mode == LeaseMode.Hourly, cancellationToken);
        if (operational != RoomAvailabilityConflict.None) return operational;
        if (mode == LeaseMode.Daily && await db.OperatingHoursSchedules.AsNoTracking().AnyAsync(cancellationToken))
        {
            var intervals = await db.OperatingHourIntervals.AsNoTracking().ToListAsync(cancellationToken);
            if (!operatingHours.IsCivilDayOpen(intervals, startAt))
                return RoomAvailabilityConflict.OutsideOperatingHours;
        }
        var reservation = await db.Reservations.AsNoTracking().AnyAsync(value =>
            value.RoomId == roomId && value.Status == ReservationStatus.Approved &&
            value.Kind != ReservationKind.Cancellation && startAt < value.EndAt &&
            (endAt == null || value.StartAt < endAt), cancellationToken);
        return reservation ? RoomAvailabilityConflict.Reservation : RoomAvailabilityConflict.None;
    }

    public async Task<OperatingHoursConflict?> CanApplyScheduleAsync(
        IReadOnlyCollection<OperatingHourInterval> proposedIntervals,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        EnsureTransaction();
        // The room name travels with each period so the rejection can name what is in the way: the operator sees the
        // booking to move, instead of a schedule that refuses to save for no stated reason.
        await foreach (var period in (
                           from value in db.Reservations.AsNoTracking()
                           join room in db.Rooms.AsNoTracking() on value.RoomId equals room.Id
                           where value.Status == ReservationStatus.Approved &&
                                 value.Kind != ReservationKind.Cancellation && value.EndAt > now
                           select new { value.StartAt, value.EndAt, RoomName = room.Name })
                           .AsAsyncEnumerable().WithCancellation(cancellationToken))
            if (!operatingHours.CoversPeriod(proposedIntervals, period.StartAt, period.EndAt))
                return new OperatingHoursConflict(OperatingHoursConflictKind.Reservation, period.RoomName, null,
                    period.StartAt, period.EndAt);

        await foreach (var period in (
                           from value in db.Leases.AsNoTracking()
                           join room in db.Rooms.AsNoTracking() on value.RoomId equals room.Id
                           join tenant in db.Tenants.AsNoTracking() on value.TenantId equals tenant.Id
                           where (value.Mode == LeaseMode.Hourly || value.Mode == LeaseMode.Daily) &&
                                 (value.LifecycleState == LeaseLifecycleState.Open ||
                                  value.LifecycleState == LeaseLifecycleState.EndingPending) &&
                                 value.OccupancyEndAt != null && value.OccupancyEndAt > now
                           select new
                           {
                               value.Mode, value.OccupancyStartAt, EndAt = value.OccupancyEndAt!.Value,
                               RoomName = room.Name, TenantName = tenant.Name
                           })
                           .AsAsyncEnumerable().WithCancellation(cancellationToken))
            if (period.Mode == LeaseMode.Hourly
                    ? !operatingHours.CoversPeriod(proposedIntervals, period.OccupancyStartAt, period.EndAt)
                    : !operatingHours.IsCivilDayOpen(proposedIntervals, period.OccupancyStartAt))
                return new OperatingHoursConflict(OperatingHoursConflictKind.Lease, period.RoomName, period.TenantName,
                    period.OccupancyStartAt, period.EndAt);

        return null;
    }

    private void EnsureTransaction()
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Availability checks for mutations require an active transaction.");
    }
}

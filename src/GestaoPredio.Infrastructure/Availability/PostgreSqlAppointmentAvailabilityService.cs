using GestaoPredio.Application.Availability;
using GestaoPredio.Application.Reservations;
using GestaoPredio.Domain.Availability;
using GestaoPredio.Domain.Leases;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GestaoPredio.Infrastructure.Availability;

public sealed class PostgreSqlAppointmentAvailabilityService(
    ApplicationDbContext db,
    IRoomAvailabilityService roomAvailability,
    IReservationConflictDetector reservationConflicts,
    OperatingHoursEvaluator operatingHoursEvaluator,
    TimeZoneInfo operationalTimeZone) : IAppointmentAvailabilityService
{
    public async Task<IReadOnlyList<AppointmentAvailabilitySlot>> FindSlotsAsync(
        Guid professionalId,
        DateOnly date,
        int durationMinutes,
        CancellationToken cancellationToken)
    {
        if (professionalId == Guid.Empty || durationMinutes is < 15 or > 480 || durationMinutes % 15 != 0)
            return [];

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var context = await LoadContextAsync(professionalId, date, cancellationToken);
        if (context is null || !context.ScheduleConfigured) return [];

        var effective = ProfessionalAvailabilityEvaluator.GetEffectiveRanges(
            context.Mode, date, context.OperatingHours, context.CustomIntervals, context.Exceptions);
        if (effective.Count == 0) return [];

        var roomIds = await db.Rooms.AsNoTracking().Where(room => room.IsActive)
            .OrderBy(room => room.Id).Select(room => room.Id).ToArrayAsync(cancellationToken);
        if (roomIds.Length == 0) return [];

        // Everything that can stop a slot is loaded for the whole civil day in four queries, then every candidate is
        // judged in memory. This used to run the per-period check (CheckScheduleAndBlocksAsync + FindConflictAsync,
        // ~9 queries) for every 15-minute candidate and every room: 702 round trips for a three-room day, which on a
        // database a few hundred milliseconds away took over a minute and ended in a 504. Anything that overlaps a
        // slot of this day also overlaps the day, so the day's rows are a superset; the in-memory rules below are
        // the same predicates as those two services, and AvailabilitySlotBatchingTests holds the two in agreement.
        // Booking does not rely on this list: it re-checks its one period exactly, under the resource lock.
        var day = await LoadDayAsync(roomIds, professionalId, date, cancellationToken);

        var slots = new List<AppointmentAvailabilitySlot>();
        var midnight = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        for (var minute = 0; minute < 24 * 60; minute += 15)
        {
            var localStart = midnight.AddMinutes(minute);
            var localEnd = localStart.AddMinutes(durationMinutes);
            if (DateOnly.FromDateTime(localEnd) != date) continue;
            if (!ProfessionalAvailabilityEvaluator.Contains(effective,
                    TimeOnly.FromDateTime(localStart), TimeOnly.FromDateTime(localEnd))) continue;

            var startAt = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart, operationalTimeZone));
            var endAt = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localEnd, operationalTimeZone));
            // The room check enforces the global operating hours (CheckScheduleAndBlocksAsync). The context holds
            // exactly this weekday's intervals, which is all OperatingHoursEvaluator.Contains ever reads.
            if (!operatingHoursEvaluator.Contains(context.OperatingHours, startAt, endAt)) continue;
            if (day.HasFreeRoom(roomIds, professionalId, startAt, endAt))
                slots.Add(new AppointmentAvailabilitySlot(startAt, endAt));
        }

        return slots;
    }

    private async Task<DayOccupancy> LoadDayAsync(Guid[] roomIds, Guid professionalId, DateOnly date,
        CancellationToken cancellationToken)
    {
        var dayStart = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(
            date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), operationalTimeZone));
        var dayEnd = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(
            date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), operationalTimeZone));

        // RoomAvailabilityService.CheckScheduleAndBlocksAsync: an active block on the room that overlaps.
        var blocks = await db.RoomBlocks.AsNoTracking()
            .Where(block => roomIds.Contains(block.RoomId) && block.Status == RoomBlockStatus.Active &&
                            dayStart < block.EndAt && block.StartAt < dayEnd)
            .Select(block => new Held(block.RoomId, Guid.Empty, block.StartAt, block.EndAt))
            .ToListAsync(cancellationToken);

        // PostgreSqlReservationConflictDetector, the three probes, for the room OR the professional.
        var reservations = await db.Reservations.AsNoTracking()
            .Where(reservation => reservation.Status == ReservationStatus.Approved &&
                                  reservation.Kind != ReservationKind.Cancellation &&
                                  (roomIds.Contains(reservation.RoomId) || reservation.ProfessionalId == professionalId) &&
                                  dayStart < reservation.EndAt && reservation.StartAt < dayEnd)
            .Select(reservation => new Held(reservation.RoomId, reservation.ProfessionalId,
                reservation.StartAt, reservation.EndAt))
            .ToListAsync(cancellationToken);

        // An ENDING_PENDING lease blocks whatever its dates say; an OPEN one blocks its occupancy period.
        var leases = await db.Leases.AsNoTracking()
            .Where(lease => (lease.LifecycleState == LeaseLifecycleState.Open ||
                             lease.LifecycleState == LeaseLifecycleState.EndingPending) &&
                            (roomIds.Contains(lease.RoomId) || lease.ProfessionalId == professionalId) &&
                            (lease.LifecycleState == LeaseLifecycleState.EndingPending ||
                             ((lease.OccupancyEndAt == null || dayStart < lease.OccupancyEndAt) &&
                              lease.OccupancyStartAt < dayEnd)))
            .Select(lease => new HeldLease(lease.RoomId, lease.ProfessionalId,
                lease.LifecycleState == LeaseLifecycleState.EndingPending, lease.OccupancyStartAt, lease.OccupancyEndAt))
            .ToListAsync(cancellationToken);

        var occurrences = await (
                from occurrence in db.LeaseOccurrences.AsNoTracking()
                join lease in db.Leases.AsNoTracking() on occurrence.LeaseId equals lease.Id
                where occurrence.State == LeaseOccurrenceState.Planned &&
                      (lease.LifecycleState == LeaseLifecycleState.Open ||
                       lease.LifecycleState == LeaseLifecycleState.EndingPending) &&
                      (roomIds.Contains(lease.RoomId) || lease.ProfessionalId == professionalId) &&
                      dayStart < occurrence.EndAt && occurrence.StartAt < dayEnd
                select new Held(lease.RoomId, lease.ProfessionalId, occurrence.StartAt, occurrence.EndAt))
            .ToListAsync(cancellationToken);

        return new DayOccupancy(blocks, reservations, leases, occurrences);
    }

    private sealed record Held(Guid RoomId, Guid ProfessionalId, DateTimeOffset StartAt, DateTimeOffset EndAt)
    {
        public bool Overlaps(DateTimeOffset startAt, DateTimeOffset endAt) => startAt < EndAt && StartAt < endAt;
    }

    private sealed record HeldLease(Guid RoomId, Guid ProfessionalId, bool EndingPending,
        DateTimeOffset OccupancyStartAt, DateTimeOffset? OccupancyEndAt)
    {
        public bool Blocks(DateTimeOffset startAt, DateTimeOffset endAt) =>
            EndingPending || ((OccupancyEndAt == null || startAt < OccupancyEndAt) && OccupancyStartAt < endAt);
    }

    private sealed record DayOccupancy(
        IReadOnlyList<Held> Blocks,
        IReadOnlyList<Held> Reservations,
        IReadOnlyList<HeldLease> Leases,
        IReadOnlyList<Held> Occurrences)
    {
        // Same outcome as FindAvailableRoomAsync's room loop: the first room that is neither blocked nor in
        // conflict wins, and a professional already engaged elsewhere makes every room fail.
        public bool HasFreeRoom(Guid[] roomIds, Guid professionalId, DateTimeOffset startAt, DateTimeOffset endAt)
        {
            var professionalBusy =
                Reservations.Any(value => value.ProfessionalId == professionalId && value.Overlaps(startAt, endAt)) ||
                Leases.Any(value => value.ProfessionalId == professionalId && value.Blocks(startAt, endAt)) ||
                Occurrences.Any(value => value.ProfessionalId == professionalId && value.Overlaps(startAt, endAt));
            if (professionalBusy) return false;

            foreach (var roomId in roomIds)
            {
                if (Blocks.Any(value => value.RoomId == roomId && value.Overlaps(startAt, endAt))) continue;
                var roomBusy =
                    Reservations.Any(value => value.RoomId == roomId && value.Overlaps(startAt, endAt)) ||
                    Leases.Any(value => value.RoomId == roomId && value.Blocks(startAt, endAt)) ||
                    Occurrences.Any(value => value.RoomId == roomId && value.Overlaps(startAt, endAt));
                if (!roomBusy) return true;
            }

            return false;
        }
    }

    public async Task<AppointmentAvailabilityResult> FindAvailableRoomAsync(
        Guid professionalId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        Guid? requiredRoomId,
        Guid? excludedReservationId,
        CancellationToken cancellationToken)
    {
        EnsureTransaction();
        if (professionalId == Guid.Empty || endAt <= startAt)
            return new(null, AppointmentAvailabilityFailure.ProfessionalUnavailable);

        var localStart = TimeZoneInfo.ConvertTime(startAt, operationalTimeZone);
        var localEnd = TimeZoneInfo.ConvertTime(endAt, operationalTimeZone);
        if (localStart.Date != localEnd.Date)
            return new(null, AppointmentAvailabilityFailure.ProfessionalUnavailable);
        var date = DateOnly.FromDateTime(localStart.DateTime);
        var context = await LoadContextAsync(professionalId, date, cancellationToken);
        if (context is null)
            return new(null, AppointmentAvailabilityFailure.ProfessionalNotFound);
        if (!context.ScheduleConfigured)
            return new(null, AppointmentAvailabilityFailure.OperatingHoursNotConfigured);

        var globalRanges = context.OperatingHours
            .Where(value => value.DayOfWeek == date.DayOfWeek)
            .Select(value => new ProfessionalLocalTimeRange(value.OpensAt, value.ClosesAt)).ToArray();
        if (!ProfessionalAvailabilityEvaluator.Contains(globalRanges,
                TimeOnly.FromDateTime(localStart.DateTime), TimeOnly.FromDateTime(localEnd.DateTime)))
            return new(null, AppointmentAvailabilityFailure.OutsideOperatingHours);

        var effective = ProfessionalAvailabilityEvaluator.GetEffectiveRanges(
            context.Mode, date, context.OperatingHours, context.CustomIntervals, context.Exceptions);
        if (!ProfessionalAvailabilityEvaluator.Contains(effective,
                TimeOnly.FromDateTime(localStart.DateTime), TimeOnly.FromDateTime(localEnd.DateTime)))
            return new(null, AppointmentAvailabilityFailure.ProfessionalUnavailable);

        var roomIds = await db.Rooms.AsNoTracking()
            .Where(room => room.IsActive && (requiredRoomId == null || room.Id == requiredRoomId))
            .OrderBy(room => room.Id).Select(room => room.Id).ToArrayAsync(cancellationToken);
        if (roomIds.Length == 0)
            return new(null, AppointmentAvailabilityFailure.RoomNotFound);

        var sawBlock = false;
        foreach (var roomId in roomIds)
        {
            var roomConflict = await roomAvailability.CheckScheduleAndBlocksAsync(
                roomId, startAt, endAt, null, true, cancellationToken);
            if (roomConflict != RoomAvailabilityConflict.None)
            {
                sawBlock |= roomConflict == RoomAvailabilityConflict.RoomBlock;
                continue;
            }

            var conflict = await reservationConflicts.FindConflictAsync(
                roomId, professionalId, startAt, endAt, excludedReservationId, cancellationToken);
            if (!conflict.Any) return new(roomId, AppointmentAvailabilityFailure.None);
        }

        return new(null, sawBlock && requiredRoomId is not null
            ? AppointmentAvailabilityFailure.RoomBlocked
            : AppointmentAvailabilityFailure.ResourceConflict);
    }

    private async Task<ProfessionalContext?> LoadContextAsync(
        Guid professionalId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var professional = await db.Professionals.AsNoTracking()
            .Where(value => value.Id == professionalId && value.IsActive)
            .Select(value => new { value.AvailabilityMode })
            .SingleOrDefaultAsync(cancellationToken);
        if (professional is null) return null;

        var configured = await db.OperatingHoursSchedules.AsNoTracking().AnyAsync(cancellationToken);
        var operatingHours = configured
            ? await db.OperatingHourIntervals.AsNoTracking()
                .Where(value => value.DayOfWeek == date.DayOfWeek).ToArrayAsync(cancellationToken)
            : [];
        var custom = await db.ProfessionalAvailabilityIntervals.AsNoTracking()
            .Where(value => value.ProfessionalId == professionalId && value.DayOfWeek == date.DayOfWeek)
            .ToArrayAsync(cancellationToken);
        var exceptions = await db.ProfessionalAvailabilityExceptions.AsNoTracking()
            .Where(value => value.ProfessionalId == professionalId && value.Date == date)
            .ToArrayAsync(cancellationToken);
        return new ProfessionalContext(professional.AvailabilityMode, configured, operatingHours, custom, exceptions);
    }

    private void EnsureTransaction()
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Appointment availability checks for mutations require an active transaction.");
    }

    private sealed record ProfessionalContext(
        ProfessionalAvailabilityMode Mode,
        bool ScheduleConfigured,
        IReadOnlyCollection<GestaoPredio.Domain.Availability.OperatingHourInterval> OperatingHours,
        IReadOnlyCollection<ProfessionalAvailabilityInterval> CustomIntervals,
        IReadOnlyCollection<ProfessionalAvailabilityException> Exceptions);
}

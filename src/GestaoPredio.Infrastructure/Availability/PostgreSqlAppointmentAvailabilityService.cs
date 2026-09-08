using GestaoPredio.Application.Availability;
using GestaoPredio.Application.Reservations;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GestaoPredio.Infrastructure.Availability;

public sealed class PostgreSqlAppointmentAvailabilityService(
    ApplicationDbContext db,
    IRoomAvailabilityService roomAvailability,
    IReservationConflictDetector reservationConflicts,
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
            if (await HasAvailableRoomAsync(roomIds, professionalId, startAt, endAt, null, cancellationToken))
                slots.Add(new AppointmentAvailabilitySlot(startAt, endAt));
        }

        return slots;
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

    private async Task<bool> HasAvailableRoomAsync(
        IReadOnlyCollection<Guid> roomIds,
        Guid professionalId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        Guid? excludedReservationId,
        CancellationToken cancellationToken)
    {
        foreach (var roomId in roomIds)
        {
            if (await roomAvailability.CheckScheduleAndBlocksAsync(
                    roomId, startAt, endAt, null, true, cancellationToken) != RoomAvailabilityConflict.None) continue;
            if (!(await reservationConflicts.FindConflictAsync(
                    roomId, professionalId, startAt, endAt, excludedReservationId, cancellationToken)).Any) return true;
        }

        return false;
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

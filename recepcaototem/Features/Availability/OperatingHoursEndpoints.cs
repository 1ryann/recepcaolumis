using System.Globalization;
using System.Security.Claims;
using GestaoPredio.Application.Availability;
using GestaoPredio.Application.Leases;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Availability;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using recepcaototem.Features.Auth;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Availability;

public static class OperatingHoursEndpoints
{
    public static IEndpointRouteBuilder MapOperatingHoursEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/operating-hours").RequireAuthorization("Operations");
        group.MapGet("", Get);
        group.MapPut("", Put).AddEndpointFilter<AntiforgeryFilter>();
        return endpoints;
    }

    private static async Task<IResult> Get(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var schedule = await db.OperatingHoursSchedules.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var intervals = schedule is null
            ? []
            : await db.OperatingHourIntervals.AsNoTracking()
                .OrderBy(value => value.DayOfWeek).ThenBy(value => value.OpensAt)
                .ToListAsync(cancellationToken);
        return Results.Ok(ToResponse(schedule, intervals));
    }

    private static async Task<IResult> Put(UpdateOperatingHoursRequest request, HttpContext context,
        ApplicationDbContext db, IRoomAvailabilityService availability, ILeaseResourceLock resourceLock,
        TimeProvider timeProvider, TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        if (!TryBuildIntervals(request.Days, out var proposed)) return InvalidSchedule();
        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var schedule = await db.OperatingHoursSchedules.SingleOrDefaultAsync(cancellationToken);
        var existing = schedule is null ? [] : await db.OperatingHourIntervals
            .Where(value => value.ScheduleId == schedule.Id).OrderBy(value => value.DayOfWeek)
            .ThenBy(value => value.OpensAt).ToListAsync(cancellationToken);

        if (schedule is null)
        {
            if (!string.IsNullOrWhiteSpace(request.ConcurrencyToken)) return InvalidToken();
            schedule = OperatingHoursSchedule.Create(now);
            db.OperatingHoursSchedules.Add(schedule);
        }
        else
        {
            if (!ConcurrencyToken.TryDecode(request.ConcurrencyToken, out var version)) return InvalidToken();
            if (schedule.Version != version) return Modified();
            db.Entry(schedule).Property(value => value.Version).OriginalValue = version;
            if (Equivalent(existing, proposed))
            {
                await transaction.RollbackAsync(cancellationToken);
                return Results.Ok(ToResponse(schedule, existing));
            }
            schedule.MarkUpdated(now);
        }

        var roomIds = await db.Rooms.AsNoTracking().OrderBy(value => value.Id)
            .Select(value => value.Id).ToArrayAsync(cancellationToken);
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest([], roomIds, []), cancellationToken);
        if (await availability.CanApplyScheduleAsync(proposed, now, cancellationToken) is { } conflict)
            return Conflict(conflict, timeZone);

        db.OperatingHourIntervals.RemoveRange(existing);
        db.OperatingHourIntervals.AddRange(proposed);
        db.AuditEntries.Add(AvailabilityAudit.CreateSucceeded(schedule.Id,
            AuditTargetTypes.OperatingHours, AuditActions.OperatingHoursUpdated, now,
            context.TraceIdentifier, Actor(context), context.Connection.RemoteIpAddress?.ToString()));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Modified();
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Modified();
        }
        return Results.Ok(ToResponse(schedule, proposed));
    }

    private static bool TryBuildIntervals(IReadOnlyList<OperatingHoursDayRequest>? days,
        out IReadOnlyList<OperatingHourInterval> intervals)
    {
        intervals = [];
        if (days is null || days.Count != 7) return false;
        var parsedDays = new HashSet<DayOfWeek>();
        var result = new List<OperatingHourInterval>();
        try
        {
            foreach (var day in days)
            {
                if (!Enum.TryParse<DayOfWeek>(day.DayOfWeek, true, out var parsed) ||
                    !Enum.IsDefined(parsed) || !parsedDays.Add(parsed) || day.Intervals is null)
                    return false;
                var ranges = new List<LocalTimeRange>();
                foreach (var interval in day.Intervals)
                {
                    if (!TryParseTime(interval.OpensAt, out var opensAt) ||
                        !TryParseTime(interval.ClosesAt, out var closesAt)) return false;
                    ranges.Add(new LocalTimeRange(opensAt, closesAt));
                }
                result.AddRange(OperatingHourInterval.CreateDay(
                    OperatingHoursSchedule.SingletonId, parsed, ranges));
            }
        }
        catch (ArgumentException)
        {
            return false;
        }
        intervals = result;
        return parsedDays.SetEquals(Enum.GetValues<DayOfWeek>());
    }

    private static bool TryParseTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, ["HH:mm", "HH:mm:ss"], CultureInfo.InvariantCulture,
            DateTimeStyles.None, out time);

    private static bool Equivalent(IReadOnlyCollection<OperatingHourInterval> current,
        IReadOnlyCollection<OperatingHourInterval> proposed) =>
        current.Select(Key).SequenceEqual(proposed.OrderBy(value => value.DayOfWeek).ThenBy(value => value.OpensAt).Select(Key));

    private static string Key(OperatingHourInterval value) =>
        $"{(int)value.DayOfWeek}:{value.OpensAt:HH:mm:ss}:{value.ClosesAt:HH:mm:ss}";

    private static OperatingHoursResponse ToResponse(OperatingHoursSchedule? schedule,
        IReadOnlyCollection<OperatingHourInterval> intervals) => new(
        schedule is not null,
        Enum.GetValues<DayOfWeek>().Select(day => new OperatingHoursDayResponse(
            day.ToString().ToUpperInvariant(), intervals.Where(value => value.DayOfWeek == day)
                .OrderBy(value => value.OpensAt)
                .Select(value => new OperatingHourIntervalResponse(
                    value.OpensAt.ToString("HH:mm", CultureInfo.InvariantCulture),
                    value.ClosesAt.ToString("HH:mm", CultureInfo.InvariantCulture))).ToArray())).ToArray(),
        schedule is null ? null : ConcurrencyToken.Encode(schedule.Version));

    /// <summary>
    /// Names the booking in the way. The old text said only that "a reservation or occupancy would be left outside"
    /// and asked the operator to adjust the periods — with nothing to adjust towards, since the screen never said
    /// which room, which day or which hours were the problem.
    /// </summary>
    private static IResult Conflict(OperatingHoursConflict conflict, TimeZoneInfo timeZone)
    {
        var start = TimeZoneInfo.ConvertTime(conflict.StartAt, timeZone);
        var end = TimeZoneInfo.ConvertTime(conflict.EndAt, timeZone);
        var when = start.Date == end.Date
            ? $"em {Local(start, "dd/MM/yyyy")}, das {Local(start, "HH:mm")} às {Local(end, "HH:mm")}"
            : $"de {Local(start, "dd/MM/yyyy")} {Local(start, "HH:mm")} até {Local(end, "dd/MM/yyyy")} {Local(end, "HH:mm")}";
        var what = conflict.Kind == OperatingHoursConflictKind.Lease
            ? $"A locação da {conflict.RoomName} para {conflict.TenantName}"
            : $"A reserva da {conflict.RoomName}";
        return Results.Json(new ApiError("OPERATING_HOURS_CONFLICT",
            $"{what} ocupa {when} e ficaria fora do funcionamento. Ajuste os períodos ou altere esse compromisso."),
            statusCode: StatusCodes.Status409Conflict);
    }

    private static string Local(DateTimeOffset value, string format) =>
        value.ToString(format, CultureInfo.InvariantCulture);

    private static string? Actor(HttpContext context) => context.User.FindFirstValue(ClaimTypes.NameIdentifier);
    private static IResult InvalidSchedule() => Results.BadRequest(new ApiError(
        "INVALID_OPERATING_HOURS", "A configuração de horário de funcionamento é inválida."));
    private static IResult InvalidToken() => Results.BadRequest(new ApiError(
        "INVALID_CONCURRENCY_TOKEN", "O token de concorrência informado é inválido."));
    private static IResult Modified() => Results.Json(new ApiError(
        "RESOURCE_MODIFIED", "O registro foi alterado por outra operação. Recarregue os dados e tente novamente."),
        statusCode: StatusCodes.Status409Conflict);
}

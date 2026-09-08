using System.Globalization;
using System.Security.Claims;
using GestaoPredio.Application.Availability;
using GestaoPredio.Application.Leases;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Availability;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using recepcaototem.Features.Auth;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Availability;

public static class ProfessionalAvailabilityEndpoints
{
    public static IEndpointRouteBuilder MapProfessionalAvailabilityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var own = endpoints.MapGroup("/api/professional/availability").RequireAuthorization("Professional");
        own.MapGet("", GetOwn);
        own.MapPut("", PutOwn).AddEndpointFilter<AntiforgeryFilter>();
        own.MapGet("/exceptions", ListOwnExceptions);
        own.MapPost("/exceptions", CreateOwnException).AddEndpointFilter<AntiforgeryFilter>();
        own.MapPut("/exceptions/{id:guid}", UpdateOwnException).AddEndpointFilter<AntiforgeryFilter>();
        own.MapDelete("/exceptions/{id:guid}", DeleteOwnException).AddEndpointFilter<AntiforgeryFilter>();

        var operations = endpoints.MapGroup("/api/admin/professionals/{professionalId:guid}/availability")
            .RequireAuthorization("Operations");
        operations.MapGet("", GetOperations);
        operations.MapPut("", PutOperations).AddEndpointFilter<AntiforgeryFilter>();
        operations.MapGet("/exceptions", ListOperationsExceptions);
        operations.MapPost("/exceptions", CreateOperationsException).AddEndpointFilter<AntiforgeryFilter>();
        operations.MapPut("/exceptions/{id:guid}", UpdateOperationsException).AddEndpointFilter<AntiforgeryFilter>();
        operations.MapDelete("/exceptions/{id:guid}", DeleteOperationsException).AddEndpointFilter<AntiforgeryFilter>();
        return endpoints;
    }

    private static async Task<IResult> GetOwn(HttpContext context, ApplicationDbContext db,
        CancellationToken ct)
    {
        var id = await ResolveOwnProfessionalId(context, db, ct);
        return id is null ? ProfileNotLinked() : await GetCore(id.Value, db, ct);
    }

    private static Task<IResult> GetOperations(Guid professionalId, ApplicationDbContext db,
        CancellationToken ct) => GetCore(professionalId, db, ct);

    private static async Task<IResult> GetCore(Guid professionalId, ApplicationDbContext db,
        CancellationToken ct)
    {
        var professional = await db.Professionals.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == professionalId, ct);
        if (professional is null) return Results.NotFound();
        var stored = await db.ProfessionalAvailabilityIntervals.AsNoTracking()
            .Where(value => value.ProfessionalId == professionalId)
            .OrderBy(value => value.DayOfWeek).ThenBy(value => value.StartTime).ToArrayAsync(ct);
        var global = await db.OperatingHourIntervals.AsNoTracking()
            .OrderBy(value => value.DayOfWeek).ThenBy(value => value.OpensAt).ToArrayAsync(ct);
        return Results.Ok(ToAvailabilityResponse(professional, stored, global, 0));
    }

    private static async Task<IResult> PutOwn(UpdateProfessionalAvailabilityRequest request,
        HttpContext context, ApplicationDbContext db, ILeaseResourceLock resourceLock,
        TimeProvider time, CancellationToken ct)
    {
        var id = await ResolveOwnProfessionalId(context, db, ct);
        return id is null ? ProfileNotLinked() : await PutCore(
            id.Value, request, false, context, db, resourceLock, time, ct);
    }

    private static Task<IResult> PutOperations(Guid professionalId,
        UpdateProfessionalAvailabilityRequest request, HttpContext context, ApplicationDbContext db,
        ILeaseResourceLock resourceLock, TimeProvider time, CancellationToken ct) =>
        PutCore(professionalId, request, true, context, db, resourceLock, time, ct);

    private static async Task<IResult> PutCore(Guid professionalId,
        UpdateProfessionalAvailabilityRequest request, bool byOperations, HttpContext context,
        ApplicationDbContext db, ILeaseResourceLock resourceLock, TimeProvider time, CancellationToken ct)
    {
        if (!TryMode(request.Mode, out var mode)) return InvalidAvailability();
        if (!ConcurrencyToken.TryDecode(request.ConcurrencyToken, out var version)) return InvalidToken();
        if (mode == ProfessionalAvailabilityMode.InheritGlobal && request.Days is not null)
            return InvalidAvailability();

        IReadOnlyList<ProfessionalAvailabilityInterval>? replacement = null;
        if (request.Days is not null && !TryBuildDays(professionalId, request.Days, out replacement))
            return InvalidAvailability();

        var now = time.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest([], [], [professionalId]), ct);
        var professional = await db.Professionals.SingleOrDefaultAsync(value => value.Id == professionalId, ct);
        if (professional is null || !professional.IsActive) return Results.NotFound();
        if (professional.Version != version) return Modified();
        db.Entry(professional).Property(value => value.Version).OriginalValue = version;
        var current = await db.ProfessionalAvailabilityIntervals
            .Where(value => value.ProfessionalId == professionalId)
            .OrderBy(value => value.DayOfWeek).ThenBy(value => value.StartTime).ToArrayAsync(ct);
        var stored = replacement ?? current;
        if (mode == ProfessionalAvailabilityMode.Custom && request.Days is null && current.Length == 0)
            return InvalidAvailability();

        var scheduleConfigured = await db.OperatingHoursSchedules.AsNoTracking().AnyAsync(ct);
        var global = await db.OperatingHourIntervals.AsNoTracking().ToArrayAsync(ct);
        if (replacement is not null && (!scheduleConfigured || !FitsCurrentOperatingHours(replacement, global)))
            return InvalidAvailability();

        professional.SetAvailabilityMode(mode, now);
        if (replacement is not null)
        {
            db.ProfessionalAvailabilityIntervals.RemoveRange(current);
            db.ProfessionalAvailabilityIntervals.AddRange(replacement);
        }
        var exceptions = await db.ProfessionalAvailabilityExceptions.AsNoTracking()
            .Where(value => value.ProfessionalId == professionalId).ToArrayAsync(ct);
        var warning = await CountOutsideAsync(db, professionalId, mode, stored, global,
            scheduleConfigured, exceptions, now, ct);
        db.AuditEntries.Add(ProfessionalAvailabilityAudit.CreateSucceeded(professionalId,
            AuditTargetTypes.Professional, byOperations
                ? AuditActions.ProfessionalAvailabilityUpdatedByOperations
                : AuditActions.ProfessionalAvailabilityUpdated,
            now, context.TraceIdentifier, Actor(context), context.Connection.RemoteIpAddress?.ToString()));
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(ct);
            return Modified();
        }
        return Results.Ok(ToAvailabilityResponse(professional, stored, global, warning));
    }

    private static async Task<IResult> ListOwnExceptions(DateOnly? from, DateOnly? to,
        HttpContext context, ApplicationDbContext db, CancellationToken ct)
    {
        var id = await ResolveOwnProfessionalId(context, db, ct);
        return id is null ? ProfileNotLinked() : await ListExceptionsCore(id.Value, from, to, db, ct);
    }

    private static Task<IResult> ListOperationsExceptions(Guid professionalId, DateOnly? from,
        DateOnly? to, ApplicationDbContext db, CancellationToken ct) =>
        ListExceptionsCore(professionalId, from, to, db, ct);

    private static async Task<IResult> ListExceptionsCore(Guid professionalId, DateOnly? from,
        DateOnly? to, ApplicationDbContext db, CancellationToken ct)
    {
        if (!ValidRange(from, to)) return InvalidRange();
        if (!await db.Professionals.AsNoTracking().AnyAsync(value => value.Id == professionalId, ct))
            return Results.NotFound();
        var rows = await db.ProfessionalAvailabilityExceptions.AsNoTracking()
            .Where(value => value.ProfessionalId == professionalId && value.Date >= from && value.Date <= to)
            .OrderBy(value => value.Date).ThenBy(value => value.StartTime).ThenBy(value => value.Id)
            .ToArrayAsync(ct);
        return Results.Ok(rows.Select(value => ToExceptionResponse(value, 0)).ToArray());
    }

    private static async Task<IResult> CreateOwnException(CreateProfessionalAvailabilityExceptionRequest request,
        HttpContext context, ApplicationDbContext db, ILeaseResourceLock resourceLock,
        TimeProvider time, CancellationToken ct)
    {
        var id = await ResolveOwnProfessionalId(context, db, ct);
        return id is null ? ProfileNotLinked() : await CreateExceptionCore(
            id.Value, request, false, context, db, resourceLock, time, ct);
    }

    private static Task<IResult> CreateOperationsException(Guid professionalId,
        CreateProfessionalAvailabilityExceptionRequest request, HttpContext context,
        ApplicationDbContext db, ILeaseResourceLock resourceLock, TimeProvider time, CancellationToken ct) =>
        CreateExceptionCore(professionalId, request, true, context, db, resourceLock, time, ct);

    private static async Task<IResult> CreateExceptionCore(Guid professionalId,
        CreateProfessionalAvailabilityExceptionRequest request, bool byOperations, HttpContext context,
        ApplicationDbContext db, ILeaseResourceLock resourceLock, TimeProvider time, CancellationToken ct)
    {
        if (!TryExceptionTimes(request.AllDay, request.StartTime, request.EndTime,
                out var start, out var end)) return InvalidException();
        ProfessionalAvailabilityException exception;
        try
        {
            exception = ProfessionalAvailabilityException.Create(professionalId, request.Date,
                request.AllDay, start, end, request.Reason, time.GetUtcNow());
        }
        catch (ArgumentException) { return InvalidException(); }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest([], [], [professionalId]), ct);
        if (!await db.Professionals.AnyAsync(value => value.Id == professionalId && value.IsActive, ct))
            return Results.NotFound();
        if (await OverlapsAsync(db, professionalId, request.Date, request.AllDay, start, end, null, ct))
            return ExceptionOverlap();
        db.ProfessionalAvailabilityExceptions.Add(exception);
        var warning = await WarningForCurrentAsync(db, professionalId, [exception], [], time.GetUtcNow(), ct);
        db.AuditEntries.Add(ProfessionalAvailabilityAudit.CreateSucceeded(exception.Id,
            AuditTargetTypes.ProfessionalAvailabilityException, byOperations
                ? AuditActions.ProfessionalAvailabilityExceptionCreatedByOperations
                : AuditActions.ProfessionalAvailabilityExceptionCreated,
            time.GetUtcNow(), context.TraceIdentifier, Actor(context), context.Connection.RemoteIpAddress?.ToString()));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Created(ExceptionLocation(byOperations, professionalId, exception.Id),
            ToExceptionResponse(exception, warning));
    }

    private static async Task<IResult> UpdateOwnException(Guid id,
        UpdateProfessionalAvailabilityExceptionRequest request, HttpContext context,
        ApplicationDbContext db, ILeaseResourceLock resourceLock, TimeProvider time, CancellationToken ct)
    {
        var professionalId = await ResolveOwnProfessionalId(context, db, ct);
        return professionalId is null ? ProfileNotLinked() : await UpdateExceptionCore(
            professionalId.Value, id, request, false, context, db, resourceLock, time, ct);
    }

    private static Task<IResult> UpdateOperationsException(Guid professionalId, Guid id,
        UpdateProfessionalAvailabilityExceptionRequest request, HttpContext context,
        ApplicationDbContext db, ILeaseResourceLock resourceLock, TimeProvider time, CancellationToken ct) =>
        UpdateExceptionCore(professionalId, id, request, true, context, db, resourceLock, time, ct);

    private static async Task<IResult> UpdateExceptionCore(Guid professionalId, Guid id,
        UpdateProfessionalAvailabilityExceptionRequest request, bool byOperations, HttpContext context,
        ApplicationDbContext db, ILeaseResourceLock resourceLock, TimeProvider time, CancellationToken ct)
    {
        if (!ConcurrencyToken.TryDecode(request.ConcurrencyToken, out var version)) return InvalidToken();
        if (!TryExceptionTimes(request.AllDay, request.StartTime, request.EndTime,
                out var start, out var end)) return InvalidException();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest([], [], [professionalId]), ct);
        var exception = await db.ProfessionalAvailabilityExceptions.SingleOrDefaultAsync(
            value => value.Id == id && value.ProfessionalId == professionalId, ct);
        if (exception is null) return Results.NotFound();
        if (exception.Version != version) return Modified();
        if (await OverlapsAsync(db, professionalId, request.Date, request.AllDay, start, end, id, ct))
            return ExceptionOverlap();
        db.Entry(exception).Property(value => value.Version).OriginalValue = version;
        try { exception.Update(request.Date, request.AllDay, start, end, request.Reason, time.GetUtcNow()); }
        catch (ArgumentException) { return InvalidException(); }
        var warning = await WarningForCurrentAsync(db, professionalId, [exception], [id], time.GetUtcNow(), ct);
        db.AuditEntries.Add(ProfessionalAvailabilityAudit.CreateSucceeded(exception.Id,
            AuditTargetTypes.ProfessionalAvailabilityException, byOperations
                ? AuditActions.ProfessionalAvailabilityExceptionUpdatedByOperations
                : AuditActions.ProfessionalAvailabilityExceptionUpdated,
            time.GetUtcNow(), context.TraceIdentifier, Actor(context), context.Connection.RemoteIpAddress?.ToString()));
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(ct);
            return Modified();
        }
        return Results.Ok(ToExceptionResponse(exception, warning));
    }

    private static async Task<IResult> DeleteOwnException(Guid id,
        [FromBody] DeleteProfessionalAvailabilityExceptionRequest request, HttpContext context,
        ApplicationDbContext db, ILeaseResourceLock resourceLock, TimeProvider time, CancellationToken ct)
    {
        var professionalId = await ResolveOwnProfessionalId(context, db, ct);
        return professionalId is null ? ProfileNotLinked() : await DeleteExceptionCore(
            professionalId.Value, id, request, false, context, db, resourceLock, time, ct);
    }

    private static Task<IResult> DeleteOperationsException(Guid professionalId, Guid id,
        [FromBody] DeleteProfessionalAvailabilityExceptionRequest request, HttpContext context,
        ApplicationDbContext db, ILeaseResourceLock resourceLock, TimeProvider time, CancellationToken ct) =>
        DeleteExceptionCore(professionalId, id, request, true, context, db, resourceLock, time, ct);

    private static async Task<IResult> DeleteExceptionCore(Guid professionalId, Guid id,
        DeleteProfessionalAvailabilityExceptionRequest request, bool byOperations, HttpContext context,
        ApplicationDbContext db, ILeaseResourceLock resourceLock, TimeProvider time, CancellationToken ct)
    {
        if (!ConcurrencyToken.TryDecode(request.ConcurrencyToken, out var version)) return InvalidToken();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest([], [], [professionalId]), ct);
        var exception = await db.ProfessionalAvailabilityExceptions.SingleOrDefaultAsync(
            value => value.Id == id && value.ProfessionalId == professionalId, ct);
        if (exception is null) return Results.NotFound();
        if (exception.Version != version) return Modified();
        db.Entry(exception).Property(value => value.Version).OriginalValue = version;
        db.ProfessionalAvailabilityExceptions.Remove(exception);
        var warning = await WarningForCurrentAsync(db, professionalId, [], [id], time.GetUtcNow(), ct);
        db.AuditEntries.Add(ProfessionalAvailabilityAudit.CreateSucceeded(exception.Id,
            AuditTargetTypes.ProfessionalAvailabilityException, byOperations
                ? AuditActions.ProfessionalAvailabilityExceptionRemovedByOperations
                : AuditActions.ProfessionalAvailabilityExceptionRemoved,
            time.GetUtcNow(), context.TraceIdentifier, Actor(context), context.Connection.RemoteIpAddress?.ToString()));
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(ct);
            return Modified();
        }
        return Results.Ok(new { existingReservationsOutsideAvailabilityCount = warning });
    }

    private static async Task<int> WarningForCurrentAsync(ApplicationDbContext db, Guid professionalId,
        IReadOnlyCollection<ProfessionalAvailabilityException> additions, IReadOnlyCollection<Guid> exclusions,
        DateTimeOffset now, CancellationToken ct)
    {
        var professional = await db.Professionals.AsNoTracking().SingleAsync(value => value.Id == professionalId, ct);
        var scheduleConfigured = await db.OperatingHoursSchedules.AsNoTracking().AnyAsync(ct);
        var global = await db.OperatingHourIntervals.AsNoTracking().ToArrayAsync(ct);
        var stored = await db.ProfessionalAvailabilityIntervals.AsNoTracking()
            .Where(value => value.ProfessionalId == professionalId).ToArrayAsync(ct);
        var exceptions = await db.ProfessionalAvailabilityExceptions.AsNoTracking()
            .Where(value => value.ProfessionalId == professionalId && !exclusions.Contains(value.Id)).ToListAsync(ct);
        exceptions.AddRange(additions);
        return await CountOutsideAsync(db, professionalId, professional.AvailabilityMode, stored,
            global, scheduleConfigured, exceptions, now, ct);
    }

    private static async Task<int> CountOutsideAsync(ApplicationDbContext db, Guid professionalId,
        ProfessionalAvailabilityMode mode, IReadOnlyCollection<ProfessionalAvailabilityInterval> stored,
        IReadOnlyCollection<OperatingHourInterval> global, bool scheduleConfigured,
        IReadOnlyCollection<ProfessionalAvailabilityException> exceptions, DateTimeOffset now, CancellationToken ct)
    {
        var reservations = await db.Reservations.AsNoTracking().Where(value =>
                value.ProfessionalId == professionalId && value.Status == ReservationStatus.Approved &&
                value.Kind != ReservationKind.Cancellation && value.EndAt > now)
            .Select(value => new { value.StartAt, value.EndAt }).ToArrayAsync(ct);
        if (!scheduleConfigured) return reservations.Length;
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Porto_Velho");
        return reservations.Count(value =>
        {
            var start = TimeZoneInfo.ConvertTime(value.StartAt, zone);
            var end = TimeZoneInfo.ConvertTime(value.EndAt, zone);
            if (start.Date != end.Date) return true;
            var date = DateOnly.FromDateTime(start.DateTime);
            var effective = ProfessionalAvailabilityEvaluator.GetEffectiveRanges(mode, date, global, stored, exceptions);
            return !ProfessionalAvailabilityEvaluator.Contains(effective,
                TimeOnly.FromDateTime(start.DateTime), TimeOnly.FromDateTime(end.DateTime));
        });
    }

    private static bool FitsCurrentOperatingHours(
        IReadOnlyCollection<ProfessionalAvailabilityInterval> custom,
        IReadOnlyCollection<OperatingHourInterval> global) => custom.All(value =>
        global.Any(interval => interval.DayOfWeek == value.DayOfWeek &&
            interval.OpensAt <= value.StartTime && value.EndTime <= interval.ClosesAt));

    private static bool TryBuildDays(Guid professionalId,
        IReadOnlyList<ProfessionalAvailabilityDayRequest> days,
        out IReadOnlyList<ProfessionalAvailabilityInterval> intervals)
    {
        intervals = [];
        if (days.Count != 7) return false;
        var seen = new HashSet<DayOfWeek>();
        var result = new List<ProfessionalAvailabilityInterval>();
        try
        {
            foreach (var day in days)
            {
                if (!Enum.TryParse<DayOfWeek>(day.DayOfWeek, true, out var parsed) ||
                    !Enum.IsDefined(parsed) || !seen.Add(parsed) || day.Intervals is null) return false;
                var ranges = new List<ProfessionalLocalTimeRange>();
                foreach (var interval in day.Intervals)
                {
                    if (!TryTime(interval.StartTime, out var start) || !TryTime(interval.EndTime, out var end))
                        return false;
                    ranges.Add(new(start, end));
                }
                result.AddRange(ProfessionalAvailabilityInterval.CreateDay(professionalId, parsed, ranges));
            }
        }
        catch (ArgumentException) { return false; }
        intervals = result;
        return seen.SetEquals(Enum.GetValues<DayOfWeek>());
    }

    private static bool TryExceptionTimes(bool allDay, string? startValue, string? endValue,
        out TimeOnly? start, out TimeOnly? end)
    {
        start = null;
        end = null;
        if (allDay) return startValue is null && endValue is null;
        if (!TryTime(startValue, out var parsedStart) || !TryTime(endValue, out var parsedEnd) ||
            parsedEnd <= parsedStart) return false;
        start = parsedStart;
        end = parsedEnd;
        return true;
    }

    private static Task<bool> OverlapsAsync(ApplicationDbContext db, Guid professionalId,
        DateOnly date, bool allDay, TimeOnly? start, TimeOnly? end, Guid? excludedId, CancellationToken ct) =>
        db.ProfessionalAvailabilityExceptions.AsNoTracking().AnyAsync(value =>
            value.ProfessionalId == professionalId && value.Date == date &&
            (excludedId == null || value.Id != excludedId) &&
            (allDay || value.AllDay || start < value.EndTime && value.StartTime < end), ct);

    private static ProfessionalAvailabilityResponse ToAvailabilityResponse(Professional professional,
        IReadOnlyCollection<ProfessionalAvailabilityInterval> stored,
        IReadOnlyCollection<OperatingHourInterval> global, int warning)
    {
        var days = Enum.GetValues<DayOfWeek>().Select(day => Day(day,
            stored.Where(value => value.DayOfWeek == day)
                .Select(value => new ProfessionalLocalTimeRange(value.StartTime, value.EndTime)))).ToArray();
        var effective = Enum.GetValues<DayOfWeek>().Select(day =>
        {
            var date = Sunday.AddDays((int)day);
            return Day(day, ProfessionalAvailabilityEvaluator.GetEffectiveRanges(
                professional.AvailabilityMode, date, global, stored, []));
        }).ToArray();
        return new(professional.Id, Mode(professional.AvailabilityMode), days, effective,
            ConcurrencyToken.Encode(professional.Version), warning);
    }

    private static ProfessionalAvailabilityDayResponse Day(DayOfWeek day,
        IEnumerable<ProfessionalLocalTimeRange> intervals) => new(day.ToString().ToUpperInvariant(),
        intervals.OrderBy(value => value.StartTime).Select(value => new ProfessionalAvailabilityIntervalResponse(
            value.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            value.EndTime.ToString("HH:mm", CultureInfo.InvariantCulture))).ToArray());

    private static ProfessionalAvailabilityExceptionResponse ToExceptionResponse(
        ProfessionalAvailabilityException value, int warning) => new(value.Id, value.ProfessionalId,
        value.Date, value.AllDay, value.StartTime?.ToString("HH:mm", CultureInfo.InvariantCulture),
        value.EndTime?.ToString("HH:mm", CultureInfo.InvariantCulture), value.Reason,
        value.CreatedAt, value.UpdatedAt, ConcurrencyToken.Encode(value.Version), warning);

    private static async Task<Guid?> ResolveOwnProfessionalId(HttpContext context,
        ApplicationDbContext db, CancellationToken ct)
    {
        var userId = Actor(context);
        return await db.Professionals.AsNoTracking().Where(value =>
                value.ApplicationUserId == userId && value.IsActive)
            .Select(value => (Guid?)value.Id).SingleOrDefaultAsync(ct);
    }

    private static bool TryMode(string? value, out ProfessionalAvailabilityMode mode)
    {
        mode = value?.Trim().ToUpperInvariant() switch
        {
            "INHERIT_GLOBAL" => ProfessionalAvailabilityMode.InheritGlobal,
            "CUSTOM" => ProfessionalAvailabilityMode.Custom,
            _ => (ProfessionalAvailabilityMode)(-1)
        };
        return Enum.IsDefined(mode);
    }

    private static string Mode(ProfessionalAvailabilityMode value) => value switch
    {
        ProfessionalAvailabilityMode.InheritGlobal => "INHERIT_GLOBAL",
        ProfessionalAvailabilityMode.Custom => "CUSTOM",
        _ => throw new InvalidOperationException("Modo de disponibilidade desconhecido.")
    };

    private static bool TryTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, ["HH:mm", "HH:mm:ss"], CultureInfo.InvariantCulture,
            DateTimeStyles.None, out time);
    private static bool ValidRange(DateOnly? from, DateOnly? to) =>
        from is not null && to is not null && to >= from && to.Value.DayNumber - from.Value.DayNumber <= 365;
    private static string ExceptionLocation(bool byOperations, Guid professionalId, Guid id) => byOperations
        ? $"/api/admin/professionals/{professionalId}/availability/exceptions/{id}"
        : $"/api/professional/availability/exceptions/{id}";
    private static string? Actor(HttpContext context) => context.User.FindFirstValue(ClaimTypes.NameIdentifier);
    private static readonly DateOnly Sunday = new(2027, 1, 3);
    private static IResult ProfileNotLinked() => Results.NotFound(new ApiError(
        "PROFESSIONAL_PROFILE_NOT_LINKED", "O perfil profissional não está vinculado corretamente."));
    private static IResult InvalidAvailability() => Results.BadRequest(new ApiError(
        "INVALID_PROFESSIONAL_AVAILABILITY", "A disponibilidade profissional é inválida."));
    private static IResult InvalidException() => Results.BadRequest(new ApiError(
        "INVALID_PROFESSIONAL_AVAILABILITY_EXCEPTION", "A exceção de disponibilidade é inválida."));
    private static IResult InvalidRange() => Results.BadRequest(new ApiError(
        "INVALID_DATE_RANGE", "O período deve conter no máximo 366 dias."));
    private static IResult ExceptionOverlap() => Results.Json(new ApiError(
        "PROFESSIONAL_AVAILABILITY_EXCEPTION_CONFLICT", "Já existe indisponibilidade sobreposta nessa data."),
        statusCode: StatusCodes.Status409Conflict);
    private static IResult InvalidToken() => Results.BadRequest(new ApiError(
        "INVALID_CONCURRENCY_TOKEN", "O token de concorrência informado é inválido."));
    private static IResult Modified() => Results.Json(new ApiError(
        "RESOURCE_MODIFIED", "O registro foi alterado por outra operação. Recarregue os dados e tente novamente."),
        statusCode: StatusCodes.Status409Conflict);
}

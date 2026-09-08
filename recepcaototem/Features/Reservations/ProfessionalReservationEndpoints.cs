using System.Security.Claims;
using GestaoPredio.Application.Leases;
using GestaoPredio.Application.Availability;
using GestaoPredio.Application.Reservations;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using recepcaototem.Features.Auth;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Reservations;

public static class ProfessionalReservationEndpoints
{
    private static readonly string[] Statuses = ["all", "PENDING", "APPROVED", "REJECTED", "CANCELLED"];

    public static IEndpointRouteBuilder MapProfessionalReservationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/professional/reservations").RequireAuthorization("Professional");
        group.MapGet("", List);
        group.MapGet("/{id:guid}", Detail);
        group.MapPost("", RequestNew).AddEndpointFilter<AntiforgeryFilter>();
        group.MapPost("/{id:guid}/reschedule-request", RequestReschedule).AddEndpointFilter<AntiforgeryFilter>();
        group.MapPost("/{id:guid}/cancel-request", RequestCancellation).AddEndpointFilter<AntiforgeryFilter>();
        return endpoints;
    }

    private static async Task<IResult> List(
        int? page,
        int? pageSize,
        string? status,
        HttpContext context,
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var actualPage = page ?? 1;
        if (actualPage < 1)
            return Results.BadRequest(new ApiError("INVALID_PAGE", "A página deve ser maior ou igual a 1."));
        var actualSize = pageSize ?? 20;
        if (actualSize is < 1 or > 100)
            return Results.BadRequest(new ApiError("INVALID_PAGE_SIZE", "O tamanho da página deve estar entre 1 e 100."));
        var actualStatus = string.IsNullOrWhiteSpace(status) ? "all" : status.Trim().ToUpperInvariant();
        if (actualStatus == "ALL") actualStatus = "all";
        if (!Statuses.Contains(actualStatus, StringComparer.Ordinal))
            return Results.BadRequest(new ApiError("INVALID_STATUS", "O status informado é inválido."));
        var professionalId = await ResolveProfessionalId(db, context, cancellationToken);
        if (professionalId is null)
            return Results.Ok(new PagedResponse<ReservationResponse>([], actualPage, actualSize, 0));

        var reservations = db.Reservations.AsNoTracking()
            .Where(value => value.ProfessionalId == professionalId.Value);
        if (actualStatus != "all")
        {
            var parsedStatus = ParseStatus(actualStatus);
            reservations = reservations.Where(value => value.Status == parsedStatus);
        }
        var query =
            from reservation in reservations
            join room in db.Rooms.AsNoTracking() on reservation.RoomId equals room.Id
            join professional in db.Professionals.AsNoTracking() on reservation.ProfessionalId equals professional.Id
            select new { Reservation = reservation, RoomName = room.Name, ProfessionalName = professional.Name };
        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(row => row.Reservation.StartAt).ThenBy(row => row.Reservation.Id)
            .Skip((actualPage - 1) * actualSize).Take(actualSize).ToListAsync(cancellationToken);
        return Results.Ok(new PagedResponse<ReservationResponse>(
            rows.Select(row => row.Reservation.ToResponse(row.RoomName, row.ProfessionalName)).ToArray(),
            actualPage, actualSize, totalCount));
    }

    private static async Task<IResult> Detail(
        Guid id,
        HttpContext context,
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var professionalId = await ResolveProfessionalId(db, context, cancellationToken);
        if (professionalId is null) return Results.NotFound();
        var row = await (
            from reservation in db.Reservations.AsNoTracking()
                .Where(value => value.ProfessionalId == professionalId.Value && value.Id == id)
            join room in db.Rooms.AsNoTracking() on reservation.RoomId equals room.Id
            join professional in db.Professionals.AsNoTracking() on reservation.ProfessionalId equals professional.Id
            select new { Reservation = reservation, RoomName = room.Name, ProfessionalName = professional.Name })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null
            ? Results.NotFound()
            : Results.Ok(row.Reservation.ToResponse(row.RoomName, row.ProfessionalName));
    }

    private static async Task<IResult> RequestNew(
        RequestReservationRequest request,
        HttpContext context,
        ApplicationDbContext db,
        ILeaseResourceLock resourceLock,
        IAppointmentAvailabilityService availability,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (request.RoomId == Guid.Empty || request.EndAt <= request.StartAt) return Invalid();
        var professionalId = await ResolveProfessionalId(db, context, cancellationToken);
        if (professionalId is null) return Results.NotFound();
        var now = timeProvider.GetUtcNow();
        if (request.StartAt < now.Add(Reservation.ProfessionalMinimumNotice)) return Invalid();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest(
            [], [request.RoomId], [professionalId.Value]), cancellationToken);
        if (!await HasCurrentProfessionalLink(
                db, professionalId.Value, Actor(context)!, cancellationToken))
            return Results.NotFound();
        if (!await db.Rooms.AnyAsync(
                room => room.Id == request.RoomId && room.IsActive, cancellationToken))
            return Results.BadRequest(new ApiError(
                "INVALID_RESERVATION_RESOURCE", "A sala ou o profissional informado é inválido."));
        var available = await availability.FindAvailableRoomAsync(professionalId.Value,
            request.StartAt, request.EndAt, request.RoomId, null, cancellationToken);
        if (!available.IsAvailable)
            return recepcaototem.Features.Availability.AppointmentAvailabilityResults.Conflict(available.Failure);

        Reservation reservation;
        try
        {
            reservation = Reservation.RequestNew(
                request.RoomId, professionalId.Value, request.StartAt, request.EndAt, Actor(context)!, now);
        }
        catch (ArgumentException)
        {
            return Invalid();
        }
        db.Reservations.Add(reservation);
        db.AuditEntries.Add(ReservationAudit.CreateSucceeded(
            reservation.Id, AuditActions.ReservationRequested, now, context.TraceIdentifier,
            Actor(context), context.Connection.RemoteIpAddress?.ToString()));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var roomName = await db.Rooms.AsNoTracking().Where(room => room.Id == reservation.RoomId)
            .Select(room => room.Name).SingleAsync(cancellationToken);
        var professionalName = await db.Professionals.AsNoTracking()
            .Where(professional => professional.Id == reservation.ProfessionalId)
            .Select(professional => professional.Name).SingleAsync(cancellationToken);
        return Results.Created($"/api/professional/reservations/{reservation.Id}",
            reservation.ToResponse(roomName, professionalName));
    }

    private static async Task<Guid?> ResolveProfessionalId(
        ApplicationDbContext db, HttpContext context, CancellationToken cancellationToken)
    {
        var userId = Actor(context);
        return await db.Professionals.AsNoTracking()
            .Where(professional => professional.ApplicationUserId == userId && professional.IsActive)
            .Select(professional => (Guid?)professional.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static async Task<IResult> RequestReschedule(
        Guid id,
        RescheduleReservationRequest request,
        HttpContext context,
        ApplicationDbContext db,
        ILeaseResourceLock resourceLock,
        IAppointmentAvailabilityService availability,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
        await RequestChange(id, request.ConcurrencyToken, request.StartAt, request.EndAt,
            ReservationKind.Reschedule, context, db, resourceLock, availability,
            timeProvider, cancellationToken);

    private static async Task<IResult> RequestCancellation(
        Guid id,
        ReservationConcurrencyRequest request,
        HttpContext context,
        ApplicationDbContext db,
        ILeaseResourceLock resourceLock,
        IAppointmentAvailabilityService availability,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
        await RequestChange(id, request.ConcurrencyToken, null, null,
            ReservationKind.Cancellation, context, db, resourceLock, availability,
            timeProvider, cancellationToken);

    private static async Task<IResult> RequestChange(
        Guid id,
        string? concurrencyToken,
        DateTimeOffset? startAt,
        DateTimeOffset? endAt,
        ReservationKind kind,
        HttpContext context,
        ApplicationDbContext db,
        ILeaseResourceLock resourceLock,
        IAppointmentAvailabilityService availability,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryDecode(concurrencyToken, out var version)) return InvalidToken();
        var professionalId = await ResolveProfessionalId(db, context, cancellationToken);
        if (professionalId is null) return Results.NotFound();
        var locator = await db.Reservations.AsNoTracking().SingleOrDefaultAsync(
            value => value.Id == id && value.ProfessionalId == professionalId, cancellationToken);
        if (locator is null) return Results.NotFound();

        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest(
            [], [locator.RoomId], [professionalId.Value]), cancellationToken);
        if (!await HasCurrentProfessionalLink(
                db, professionalId.Value, Actor(context)!, cancellationToken))
            return Results.NotFound();
        var original = await db.Reservations.SingleOrDefaultAsync(
            value => value.Id == id && value.ProfessionalId == professionalId, cancellationToken);
        if (original is null) return Results.NotFound();
        if (original.Version != version) return Modified();
        if (await db.Reservations.AnyAsync(value =>
                value.OriginalReservationId == original.Id && value.Status == ReservationStatus.Pending,
                cancellationToken))
            return InvalidTransition();

        Reservation change;
        try
        {
            if (kind == ReservationKind.Reschedule)
            {
                if (startAt is null || endAt is null || endAt <= startAt) return Invalid();
                if (original.StartAt < now.Add(Reservation.ProfessionalMinimumNotice) ||
                    startAt < now.Add(Reservation.ProfessionalMinimumNotice)) return Invalid();
                var available = await availability.FindAvailableRoomAsync(original.ProfessionalId,
                    startAt.Value, endAt.Value, original.RoomId, original.Id, cancellationToken);
                if (!available.IsAvailable)
                    return recepcaototem.Features.Availability.AppointmentAvailabilityResults.Conflict(available.Failure);
                change = Reservation.RequestReschedule(
                    original, startAt.Value, endAt.Value, Actor(context)!, now);
            }
            else
            {
                change = Reservation.RequestCancellation(original, Actor(context)!, now);
            }
        }
        catch (InvalidOperationException)
        {
            return InvalidTransition();
        }
        catch (ArgumentException)
        {
            return Invalid();
        }

        db.Reservations.Add(change);
        var action = kind == ReservationKind.Reschedule
            ? AuditActions.ReservationRescheduleRequested
            : AuditActions.ReservationCancellationRequested;
        db.AuditEntries.Add(ReservationAudit.CreateSucceeded(
            change.Id, action, now, context.TraceIdentifier,
            Actor(context), context.Connection.RemoteIpAddress?.ToString()));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var roomName = await db.Rooms.AsNoTracking().Where(room => room.Id == change.RoomId)
            .Select(room => room.Name).SingleAsync(cancellationToken);
        var professionalName = await db.Professionals.AsNoTracking()
            .Where(professional => professional.Id == change.ProfessionalId)
            .Select(professional => professional.Name).SingleAsync(cancellationToken);
        return Results.Created($"/api/professional/reservations/{change.Id}",
            change.ToResponse(roomName, professionalName));
    }

    private static string? Actor(HttpContext context) => context.User.FindFirstValue(ClaimTypes.NameIdentifier);
    private static Task<bool> HasCurrentProfessionalLink(
        ApplicationDbContext db,
        Guid professionalId,
        string applicationUserId,
        CancellationToken cancellationToken) =>
        db.Professionals.AsNoTracking().AnyAsync(professional =>
            professional.Id == professionalId &&
            professional.ApplicationUserId == applicationUserId &&
            professional.IsActive,
            cancellationToken);

    private static ReservationStatus ParseStatus(string status) => status switch
    {
        "PENDING" => ReservationStatus.Pending,
        "APPROVED" => ReservationStatus.Approved,
        "REJECTED" => ReservationStatus.Rejected,
        "CANCELLED" => ReservationStatus.Cancelled,
        _ => throw new InvalidOperationException("Status de reserva desconhecido.")
    };
    private static IResult Invalid() => Results.BadRequest(new ApiError(
        "INVALID_RESERVATION", "Os dados da reserva são inválidos."));
    private static IResult Conflict() => Results.Json(new ApiError(
        "RESERVATION_RESOURCE_CONFLICT", "A sala ou o profissional já possui ocupação conflitante."),
        statusCode: StatusCodes.Status409Conflict);
    private static IResult AvailabilityConflict(RoomAvailabilityConflict conflict) => conflict switch
    {
        RoomAvailabilityConflict.OutsideOperatingHours => Results.Json(new ApiError(
            "ROOM_OUTSIDE_OPERATING_HOURS", "O período está fora do horário de funcionamento."),
            statusCode: StatusCodes.Status409Conflict),
        RoomAvailabilityConflict.RoomBlock => Results.Json(new ApiError(
            "ROOM_BLOCKED", "A sala está bloqueada no período informado."),
            statusCode: StatusCodes.Status409Conflict),
        _ => throw new InvalidOperationException("Conflito de disponibilidade inesperado.")
    };
    private static IResult InvalidToken() => Results.BadRequest(new ApiError(
        "INVALID_CONCURRENCY_TOKEN", "O token de concorrência informado é inválido."));
    private static IResult Modified() => Results.Json(new ApiError(
        "RESOURCE_MODIFIED", "O registro foi alterado por outra operação. Recarregue os dados e tente novamente."),
        statusCode: StatusCodes.Status409Conflict);
    private static IResult InvalidTransition() => Results.Json(new ApiError(
        "INVALID_RESERVATION_TRANSITION", "A reserva não permite esta operação no estado atual."),
        statusCode: StatusCodes.Status409Conflict);
}

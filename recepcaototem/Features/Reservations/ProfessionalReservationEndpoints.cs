using System.Security.Claims;
using GestaoPredio.Application.Leases;
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
    public static IEndpointRouteBuilder MapProfessionalReservationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/professional/reservations").RequireAuthorization("Professional");
        group.MapGet("", List);
        group.MapGet("/{id:guid}", Detail);
        group.MapPost("", RequestNew).AddEndpointFilter<AntiforgeryFilter>();
        return endpoints;
    }

    private static async Task<IResult> List(
        int? page,
        int? pageSize,
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
        var professionalId = await ResolveProfessionalId(db, context, cancellationToken);
        if (professionalId is null)
            return Results.Ok(new PagedResponse<ReservationResponse>([], actualPage, actualSize, 0));

        var query =
            from reservation in db.Reservations.AsNoTracking()
                .Where(value => value.ProfessionalId == professionalId.Value)
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
        IReservationConflictDetector conflictDetector,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (request.RoomId == Guid.Empty || request.EndAt <= request.StartAt) return Invalid();
        var professionalId = await ResolveProfessionalId(db, context, cancellationToken);
        if (professionalId is null) return Results.NotFound();
        var now = timeProvider.GetUtcNow();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest(
            [], [request.RoomId], [professionalId.Value]), cancellationToken);
        var resourcesActive = await db.Rooms.AnyAsync(
                                  room => room.Id == request.RoomId && room.IsActive, cancellationToken) &&
                              await db.Professionals.AnyAsync(
                                  professional => professional.Id == professionalId && professional.IsActive,
                                  cancellationToken);
        if (!resourcesActive)
            return Results.BadRequest(new ApiError(
                "INVALID_RESERVATION_RESOURCE", "A sala ou o profissional informado é inválido."));
        var conflict = await conflictDetector.FindConflictAsync(
            request.RoomId, professionalId.Value, request.StartAt, request.EndAt, null, cancellationToken);
        if (conflict.Any) return Conflict();

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

    private static string? Actor(HttpContext context) => context.User.FindFirstValue(ClaimTypes.NameIdentifier);
    private static IResult Invalid() => Results.BadRequest(new ApiError(
        "INVALID_RESERVATION", "Os dados da reserva são inválidos."));
    private static IResult Conflict() => Results.Json(new ApiError(
        "RESERVATION_RESOURCE_CONFLICT", "A sala ou o profissional já possui ocupação conflitante."),
        statusCode: StatusCodes.Status409Conflict);
}

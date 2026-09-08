using System.Security.Claims;
using GestaoPredio.Application.Leases;
using GestaoPredio.Application.Reservations;
using GestaoPredio.Application.Availability;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using recepcaototem.Features.Auth;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Reservations;

public static partial class ReservationEndpoints
{
    private static readonly string[] Statuses = ["all", "PENDING", "APPROVED", "REJECTED", "CANCELLED"];

    public static IEndpointRouteBuilder MapReservationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/reservations").RequireAuthorization("Operations");
        group.MapGet("", List);
        group.MapGet("/{id:guid}", Detail);
        group.MapPost("", Create).AddEndpointFilter<AntiforgeryFilter>();
        group.MapPost("/{id:guid}/approve", Approve).AddEndpointFilter<AntiforgeryFilter>();
        group.MapPost("/{id:guid}/reject", Reject).AddEndpointFilter<AntiforgeryFilter>();
        group.MapPost("/{id:guid}/cancel", Cancel).AddEndpointFilter<AntiforgeryFilter>();
        group.MapPost("/{id:guid}/reschedule", Reschedule).AddEndpointFilter<AntiforgeryFilter>();
        return endpoints;
    }

    private static async Task<IResult> List(
        int? page,
        int? pageSize,
        string? status,
        Guid? roomId,
        Guid? professionalId,
        DateTimeOffset? from,
        DateTimeOffset? to,
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
        if (roomId == Guid.Empty || professionalId == Guid.Empty || from >= to)
            return Invalid();

        var reservations = db.Reservations.AsNoTracking();
        if (actualStatus != "all")
        {
            var parsed = ParseStatus(actualStatus);
            reservations = reservations.Where(reservation => reservation.Status == parsed);
        }
        if (roomId is not null) reservations = reservations.Where(reservation => reservation.RoomId == roomId);
        if (professionalId is not null)
            reservations = reservations.Where(reservation => reservation.ProfessionalId == professionalId);
        if (from is not null) reservations = reservations.Where(reservation => reservation.EndAt > from);
        if (to is not null) reservations = reservations.Where(reservation => reservation.StartAt < to);

        var joined =
            from reservation in reservations
            join room in db.Rooms.AsNoTracking() on reservation.RoomId equals room.Id
            join professional in db.Professionals.AsNoTracking() on reservation.ProfessionalId equals professional.Id
            select new { Reservation = reservation, RoomName = room.Name, ProfessionalName = professional.Name };
        var totalCount = await joined.CountAsync(cancellationToken);
        var rows = await joined.OrderByDescending(row => row.Reservation.StartAt).ThenBy(row => row.Reservation.Id)
            .Skip((actualPage - 1) * actualSize).Take(actualSize).ToListAsync(cancellationToken);
        return Results.Ok(new PagedResponse<ReservationResponse>(
            rows.Select(row => row.Reservation.ToResponse(row.RoomName, row.ProfessionalName)).ToArray(),
            actualPage, actualSize, totalCount));
    }

    private static async Task<IResult> Detail(
        Guid id,
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var row = await (
            from reservation in db.Reservations.AsNoTracking().Where(reservation => reservation.Id == id)
            join room in db.Rooms.AsNoTracking() on reservation.RoomId equals room.Id
            join professional in db.Professionals.AsNoTracking() on reservation.ProfessionalId equals professional.Id
            select new { Reservation = reservation, RoomName = room.Name, ProfessionalName = professional.Name })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null
            ? Results.NotFound()
            : Results.Ok(row.Reservation.ToResponse(row.RoomName, row.ProfessionalName));
    }

    private static async Task<IResult> Create(
        CreateReservationRequest request,
        HttpContext context,
        ApplicationDbContext db,
        ILeaseResourceLock resourceLock,
        IAppointmentAvailabilityService availability,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!ValidPeriod(request.RoomId, request.ProfessionalId, request.StartAt, request.EndAt)) return Invalid();
        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest(
            [], [request.RoomId], [request.ProfessionalId]), cancellationToken);
        if (!await ResourcesAreActive(db, request.RoomId, request.ProfessionalId, cancellationToken))
            return Results.BadRequest(new ApiError(
                "INVALID_RESERVATION_RESOURCE", "A sala ou o profissional informado é inválido."));
        var available = await availability.FindAvailableRoomAsync(request.ProfessionalId,
            request.StartAt, request.EndAt, request.RoomId, null, cancellationToken);
        if (!available.IsAvailable)
            return Features.Availability.AppointmentAvailabilityResults.Conflict(available.Failure);

        Reservation reservation;
        try
        {
            reservation = Reservation.CreateApproved(
                request.RoomId, request.ProfessionalId, request.StartAt, request.EndAt, Actor(context)!, now);
        }
        catch (ArgumentException)
        {
            return Invalid();
        }
        db.Reservations.Add(reservation);
        db.AuditEntries.Add(ReservationAudit.CreateSucceeded(
            reservation.Id, AuditActions.ReservationCreated, now, context.TraceIdentifier,
            Actor(context), context.Connection.RemoteIpAddress?.ToString()));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var names = await LoadNames(db, reservation, cancellationToken);
        return Results.Created($"/api/admin/reservations/{reservation.Id}",
            reservation.ToResponse(names.Room, names.Professional));
    }

    private static bool ValidPeriod(Guid roomId, Guid professionalId, DateTimeOffset startAt, DateTimeOffset endAt) =>
        roomId != Guid.Empty && professionalId != Guid.Empty && endAt > startAt;

    internal static IResult OutsideOperatingHours() => Results.Json(new ApiError(
        "ROOM_OUTSIDE_OPERATING_HOURS", "O período está fora do horário de funcionamento."),
        statusCode: StatusCodes.Status409Conflict);
    internal static IResult RoomBlocked() => Results.Json(new ApiError(
        "ROOM_BLOCKED", "A sala está bloqueada no período informado."),
        statusCode: StatusCodes.Status409Conflict);

    private static async Task<bool> ResourcesAreActive(
        ApplicationDbContext db, Guid roomId, Guid professionalId, CancellationToken cancellationToken) =>
        await db.Rooms.AnyAsync(room => room.Id == roomId && room.IsActive, cancellationToken) &&
        await db.Professionals.AnyAsync(
            professional => professional.Id == professionalId && professional.IsActive, cancellationToken);

    private static async Task<(string Room, string Professional)> LoadNames(
        ApplicationDbContext db, Reservation reservation, CancellationToken cancellationToken)
    {
        var room = await db.Rooms.AsNoTracking().Where(value => value.Id == reservation.RoomId)
            .Select(value => value.Name).SingleAsync(cancellationToken);
        var professional = await db.Professionals.AsNoTracking().Where(value => value.Id == reservation.ProfessionalId)
            .Select(value => value.Name).SingleAsync(cancellationToken);
        return (room, professional);
    }

    private static ReservationStatus ParseStatus(string status) => status switch
    {
        "PENDING" => ReservationStatus.Pending,
        "APPROVED" => ReservationStatus.Approved,
        "REJECTED" => ReservationStatus.Rejected,
        "CANCELLED" => ReservationStatus.Cancelled,
        _ => throw new InvalidOperationException("Status de reserva desconhecido.")
    };

    private static string? Actor(HttpContext context) => context.User.FindFirstValue(ClaimTypes.NameIdentifier);
    private static IResult Invalid() => Results.BadRequest(new ApiError(
        "INVALID_RESERVATION", "Os dados da reserva são inválidos."));
    private static IResult Conflict() => Results.Json(new ApiError(
        "RESERVATION_RESOURCE_CONFLICT", "A sala ou o profissional já possui ocupação conflitante."),
        statusCode: StatusCodes.Status409Conflict);
}

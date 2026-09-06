using System.Security.Claims;
using GestaoPredio.Application.Leases;
using GestaoPredio.Application.Visits;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Visits;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using recepcaototem.Features.Auth;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Visits;

public static class VisitEndpoints
{
    private static readonly string[] Statuses = ["all", "WAITING", "IN_SERVICE", "ENDED", "CANCELLED"];

    public static IEndpointRouteBuilder MapVisitEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/admin/visits").RequireAuthorization("Operations");
        admin.MapGet("", ListAdmin);
        admin.MapGet("/{id:guid}", DetailAdmin);
        admin.MapPost("", Create).AddEndpointFilter<AntiforgeryFilter>();
        admin.MapPost("/{id:guid}/start", StartAdmin).AddEndpointFilter<AntiforgeryFilter>();
        admin.MapPost("/{id:guid}/end", EndAdmin).AddEndpointFilter<AntiforgeryFilter>();
        admin.MapPost("/{id:guid}/cancel", CancelAdmin).AddEndpointFilter<AntiforgeryFilter>();
        admin.MapPost("/{id:guid}/correct", CorrectAdmin).AddEndpointFilter<AntiforgeryFilter>();

        var professional = endpoints.MapGroup("/api/professional/visits").RequireAuthorization("Professional");
        professional.MapGet("", ListProfessional);
        professional.MapGet("/{id:guid}", DetailProfessional);
        professional.MapPost("/{id:guid}/start", StartProfessional).AddEndpointFilter<AntiforgeryFilter>();
        professional.MapPost("/{id:guid}/end", EndProfessional).AddEndpointFilter<AntiforgeryFilter>();
        professional.MapPost("/{id:guid}/cancel", CancelProfessional).AddEndpointFilter<AntiforgeryFilter>();
        return endpoints;
    }

    private static Task<IResult> ListAdmin(int? page, int? pageSize, string? status,
        Guid? professionalId, Guid? roomId, DateTimeOffset? from, DateTimeOffset? to,
        ApplicationDbContext db, CancellationToken cancellationToken) =>
        List(db, page, pageSize, status, professionalId, roomId, from, to, cancellationToken);

    private static async Task<IResult> ListProfessional(int? page, int? pageSize, string? status,
        DateTimeOffset? from, DateTimeOffset? to, HttpContext context,
        ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var professionalId = await ResolveProfessionalId(db, context, cancellationToken);
        if (professionalId is null)
        {
            var actualPage = page ?? 1;
            var actualSize = pageSize ?? 20;
            if (actualPage < 1) return InvalidPage();
            if (actualSize is < 1 or > 100) return InvalidPageSize();
            if (!TryStatus(status, out _, out _)) return InvalidStatus();
            if (from >= to) return Invalid();
            return Results.Ok(new PagedResponse<VisitResponse>([], actualPage, actualSize, 0));
        }
        return await List(db, page, pageSize, status, professionalId, null, from, to, cancellationToken);
    }

    private static async Task<IResult> List(ApplicationDbContext db, int? page, int? pageSize,
        string? status, Guid? professionalId, Guid? roomId, DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var actualPage = page ?? 1;
        if (actualPage < 1) return InvalidPage();
        var actualSize = pageSize ?? 20;
        if (actualSize is < 1 or > 100) return InvalidPageSize();
        if (!TryStatus(status, out var parsedStatus, out var all)) return InvalidStatus();
        if (professionalId == Guid.Empty || roomId == Guid.Empty || from >= to) return Invalid();

        var visits = db.Visits.AsNoTracking();
        if (!all) visits = visits.Where(visit => visit.Status == parsedStatus);
        if (professionalId is not null) visits = visits.Where(visit => visit.ProfessionalId == professionalId);
        if (roomId is not null) visits = visits.Where(visit => visit.RoomId == roomId);
        if (from is not null) visits = visits.Where(visit => visit.ArrivedAt >= from);
        if (to is not null) visits = visits.Where(visit => visit.ArrivedAt < to);

        var rows =
            from visit in visits
            join professional in db.Professionals.AsNoTracking() on visit.ProfessionalId equals professional.Id
            join room in db.Rooms.AsNoTracking() on visit.RoomId equals room.Id into rooms
            from room in rooms.DefaultIfEmpty()
            select new { Visit = visit, ProfessionalName = professional.Name, RoomName = room == null ? null : room.Name };
        var total = await rows.CountAsync(cancellationToken);
        var pageRows = await rows
            .OrderBy(row => row.Visit.Status == VisitStatus.InService ? 0 : row.Visit.Status == VisitStatus.Waiting ? 1 : 2)
            .ThenByDescending(row => row.Visit.ArrivedAt).ThenBy(row => row.Visit.Id)
            .Skip((actualPage - 1) * actualSize).Take(actualSize).ToListAsync(cancellationToken);
        return Results.Ok(new PagedResponse<VisitResponse>(pageRows.Select(row =>
            Map(row.Visit, row.ProfessionalName, row.RoomName, [])).ToArray(), actualPage, actualSize, total));
    }

    private static Task<IResult> DetailAdmin(Guid id, ApplicationDbContext db,
        CancellationToken cancellationToken) => Detail(id, null, db, cancellationToken);

    private static async Task<IResult> DetailProfessional(Guid id, HttpContext context,
        ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var professionalId = await ResolveProfessionalId(db, context, cancellationToken);
        return professionalId is null
            ? Results.NotFound()
            : await Detail(id, professionalId, db, cancellationToken);
    }

    private static async Task<IResult> Detail(Guid id, Guid? professionalId, ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var response = await LoadResponse(db, id, professionalId, cancellationToken);
        return response is null ? Results.NotFound() : Results.Ok(response);
    }

    private static async Task<IResult> Create(CreateVisitRequest request, HttpContext context,
        ApplicationDbContext db, ILeaseResourceLock resourceLock, TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (request.ProfessionalId == Guid.Empty || string.IsNullOrWhiteSpace(request.VisitorName)) return Invalid();
        Guid? effectiveRoomId = request.RoomId;
        if (request.ReservationId is not null)
        {
            var reservationLocator = await db.Reservations.AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == request.ReservationId, cancellationToken);
            if (reservationLocator is null || reservationLocator.ProfessionalId != request.ProfessionalId ||
                request.RoomId is not null && request.RoomId != reservationLocator.RoomId)
                return InvalidResource();
            effectiveRoomId = reservationLocator.RoomId;
        }

        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest([], effectiveRoomId is null ? [] : [effectiveRoomId.Value],
            [request.ProfessionalId]), cancellationToken);
        if (!await db.Professionals.AnyAsync(value => value.Id == request.ProfessionalId && value.IsActive,
                cancellationToken) ||
            effectiveRoomId is not null && !await db.Rooms.AnyAsync(
                value => value.Id == effectiveRoomId && value.IsActive, cancellationToken))
            return InvalidResource();
        if (request.ReservationId is not null && !await db.Reservations.AnyAsync(value =>
                value.Id == request.ReservationId && value.ProfessionalId == request.ProfessionalId &&
                value.RoomId == effectiveRoomId && value.Status == ReservationStatus.Approved &&
                value.Kind != ReservationKind.Cancellation, cancellationToken))
            return InvalidResource();

        Visit visit;
        try
        {
            visit = Visit.Arrive(request.ProfessionalId, effectiveRoomId, request.ReservationId,
                request.VisitorName!, Actor(context)!, now);
        }
        catch (ArgumentException) { return Invalid(); }
        db.Visits.Add(visit);
        db.VisitTransitions.Add(VisitTransition.Record(visit.Id, null, VisitStatus.Waiting,
            Actor(context)!, now));
        db.AuditEntries.Add(VisitAudit.CreateSucceeded(visit.Id, AuditActions.VisitArrived, now,
            context.TraceIdentifier, Actor(context), context.Connection.RemoteIpAddress?.ToString()));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Created($"/api/admin/visits/{visit.Id}",
            await LoadResponse(db, visit.Id, null, cancellationToken));
    }

    private static Task<IResult> StartAdmin(Guid id, VisitConcurrencyRequest request, HttpContext context,
        ApplicationDbContext db, ILeaseResourceLock resourceLock, TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
        Mutate(id, request.ConcurrencyToken, VisitMutation.Start, null, null, context, db, resourceLock,
            timeProvider, cancellationToken);

    private static Task<IResult> EndAdmin(Guid id, VisitConcurrencyRequest request, HttpContext context,
        ApplicationDbContext db, ILeaseResourceLock resourceLock, TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
        Mutate(id, request.ConcurrencyToken, VisitMutation.End, null, null, context, db, resourceLock,
            timeProvider, cancellationToken);

    private static Task<IResult> CancelAdmin(Guid id, VisitConcurrencyRequest request, HttpContext context,
        ApplicationDbContext db, ILeaseResourceLock resourceLock, TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
        Mutate(id, request.ConcurrencyToken, VisitMutation.Cancel, null, null, context, db, resourceLock,
            timeProvider, cancellationToken);

    private static Task<IResult> CorrectAdmin(Guid id, CorrectVisitRequest request, HttpContext context,
        ApplicationDbContext db, ILeaseResourceLock resourceLock, TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryStatus(request.Status, out var target, out var all) || all)
            return Task.FromResult(Invalid());
        return Mutate(id, request.ConcurrencyToken, VisitMutation.Correct, target, request.Reason, context,
            db, resourceLock, timeProvider, cancellationToken);
    }

    private static Task<IResult> StartProfessional(Guid id, VisitConcurrencyRequest request, HttpContext context,
        ApplicationDbContext db, ILeaseResourceLock resourceLock, TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
        MutateOwned(id, request.ConcurrencyToken, VisitMutation.Start, context, db, resourceLock,
            timeProvider, cancellationToken);

    private static Task<IResult> EndProfessional(Guid id, VisitConcurrencyRequest request, HttpContext context,
        ApplicationDbContext db, ILeaseResourceLock resourceLock, TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
        MutateOwned(id, request.ConcurrencyToken, VisitMutation.End, context, db, resourceLock,
            timeProvider, cancellationToken);

    private static Task<IResult> CancelProfessional(Guid id, VisitConcurrencyRequest request, HttpContext context,
        ApplicationDbContext db, ILeaseResourceLock resourceLock, TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
        MutateOwned(id, request.ConcurrencyToken, VisitMutation.Cancel, context, db, resourceLock,
            timeProvider, cancellationToken);

    private static async Task<IResult> MutateOwned(Guid id, string? token, VisitMutation mutation,
        HttpContext context, ApplicationDbContext db, ILeaseResourceLock resourceLock,
        TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryDecode(token, out var version)) return InvalidToken();
        var professionalId = await ResolveProfessionalId(db, context, cancellationToken);
        if (professionalId is null) return Results.NotFound();
        var locator = await db.Visits.AsNoTracking().SingleOrDefaultAsync(
            value => value.Id == id && value.ProfessionalId == professionalId, cancellationToken);
        if (locator is null) return Results.NotFound();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest([], locator.RoomId is null ? [] : [locator.RoomId.Value],
            [professionalId.Value]), cancellationToken);
        if (!await db.Professionals.AsNoTracking().AnyAsync(value => value.Id == professionalId &&
                value.IsActive && value.ApplicationUserId == Actor(context), cancellationToken))
            return Results.NotFound();
        return await MutateLocked(id, version, mutation, null, null, professionalId, context, db,
            transaction, timeProvider.GetUtcNow(), cancellationToken);
    }

    private static async Task<IResult> Mutate(Guid id, string? token, VisitMutation mutation,
        VisitStatus? target, string? reason, HttpContext context, ApplicationDbContext db,
        ILeaseResourceLock resourceLock, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryDecode(token, out var version)) return InvalidToken();
        var locator = await db.Visits.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (locator is null) return Results.NotFound();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest([], locator.RoomId is null ? [] : [locator.RoomId.Value],
            [locator.ProfessionalId]), cancellationToken);
        return await MutateLocked(id, version, mutation, target, reason, null, context, db,
            transaction, timeProvider.GetUtcNow(), cancellationToken);
    }

    private static async Task<IResult> MutateLocked(Guid id, uint version, VisitMutation mutation,
        VisitStatus? target, string? reason, Guid? professionalId, HttpContext context,
        ApplicationDbContext db, IDbContextTransaction transaction, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var visit = await db.Visits.SingleOrDefaultAsync(value => value.Id == id &&
            (professionalId == null || value.ProfessionalId == professionalId), cancellationToken);
        if (visit is null) return Results.NotFound();
        if (visit.Version != version) return Modified();
        var previous = visit.Status;
        string auditAction;
        try
        {
            switch (mutation)
            {
                case VisitMutation.Start:
                    visit.StartService(Actor(context)!, now); auditAction = AuditActions.VisitServiceStarted; break;
                case VisitMutation.End:
                    visit.End(Actor(context)!, now); auditAction = AuditActions.VisitEnded; break;
                case VisitMutation.Cancel:
                    visit.Cancel(Actor(context)!, now); auditAction = AuditActions.VisitCancelled; break;
                case VisitMutation.Correct:
                    visit.Correct(target!.Value, reason ?? string.Empty, Actor(context)!, now);
                    auditAction = AuditActions.VisitCorrected; break;
                default: throw new InvalidOperationException();
            }
        }
        catch (InvalidOperationException) { return InvalidTransition(); }
        catch (ArgumentException) { return Invalid(); }

        db.Entry(visit).Property(value => value.Version).OriginalValue = version;
        db.VisitTransitions.Add(VisitTransition.Record(visit.Id, previous, visit.Status,
            Actor(context)!, now, mutation == VisitMutation.Correct ? reason : null,
            mutation == VisitMutation.Correct));
        db.AuditEntries.Add(VisitAudit.CreateSucceeded(visit.Id, auditAction, now,
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
        return Results.Ok(await LoadResponse(db, visit.Id, professionalId, cancellationToken));
    }

    private static async Task<VisitResponse?> LoadResponse(ApplicationDbContext db, Guid id,
        Guid? professionalId, CancellationToken cancellationToken)
    {
        var row = await (
            from visit in db.Visits.AsNoTracking().Where(value => value.Id == id &&
                (professionalId == null || value.ProfessionalId == professionalId))
            join professional in db.Professionals.AsNoTracking() on visit.ProfessionalId equals professional.Id
            join room in db.Rooms.AsNoTracking() on visit.RoomId equals room.Id into rooms
            from room in rooms.DefaultIfEmpty()
            select new { Visit = visit, ProfessionalName = professional.Name, RoomName = room == null ? null : room.Name })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;
        var history = await db.VisitTransitions.AsNoTracking().Where(value => value.VisitId == id)
            .OrderBy(value => value.OccurredAt).ThenBy(value => value.Id).ToListAsync(cancellationToken);
        return Map(row.Visit, row.ProfessionalName, row.RoomName, history.Select(Map).ToArray());
    }

    private static VisitResponse Map(Visit visit, string professionalName, string? roomName,
        IReadOnlyList<VisitTransitionResponse> history) => new(visit.Id, visit.ProfessionalId,
        professionalName, visit.RoomId, roomName, visit.ReservationId, visit.VisitorName,
        ToContract(visit.Status), visit.ArrivedAt, visit.ServiceStartedAt, visit.EndedAt,
        visit.CancelledAt, visit.CreatedAt, visit.UpdatedAt, ConcurrencyToken.Encode(visit.Version), history);

    private static VisitTransitionResponse Map(VisitTransition transition) => new(transition.Id,
        transition.PreviousStatus is null ? null : ToContract(transition.PreviousStatus.Value),
        ToContract(transition.NewStatus), transition.OccurredAt, transition.Reason, transition.IsCorrection);

    private static string ToContract(VisitStatus status) => status switch
    {
        VisitStatus.Waiting => "WAITING", VisitStatus.InService => "IN_SERVICE",
        VisitStatus.Ended => "ENDED", VisitStatus.Cancelled => "CANCELLED",
        _ => throw new InvalidOperationException("Estado de visita desconhecido.")
    };

    private static bool TryStatus(string? value, out VisitStatus status, out bool all)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "all" : value.Trim().ToUpperInvariant();
        all = normalized is "ALL" or "all";
        status = normalized switch
        {
            "WAITING" => VisitStatus.Waiting, "IN_SERVICE" => VisitStatus.InService,
            "ENDED" => VisitStatus.Ended, "CANCELLED" => VisitStatus.Cancelled, _ => default
        };
        return all || Statuses.Contains(normalized, StringComparer.Ordinal);
    }

    private static async Task<Guid?> ResolveProfessionalId(ApplicationDbContext db, HttpContext context,
        CancellationToken cancellationToken) => await db.Professionals.AsNoTracking()
        .Where(value => value.ApplicationUserId == Actor(context) && value.IsActive)
        .Select(value => (Guid?)value.Id).SingleOrDefaultAsync(cancellationToken);

    private static string? Actor(HttpContext context) => context.User.FindFirstValue(ClaimTypes.NameIdentifier);
    private static IResult Invalid() => Results.BadRequest(new ApiError("INVALID_VISIT", "Os dados da visita são inválidos."));
    private static IResult InvalidResource() => Results.BadRequest(new ApiError("INVALID_VISIT_RESOURCE", "A reserva, sala ou profissional informado é inválido."));
    private static IResult InvalidPage() => Results.BadRequest(new ApiError("INVALID_PAGE", "A página deve ser maior ou igual a 1."));
    private static IResult InvalidPageSize() => Results.BadRequest(new ApiError("INVALID_PAGE_SIZE", "O tamanho da página deve estar entre 1 e 100."));
    private static IResult InvalidStatus() => Results.BadRequest(new ApiError("INVALID_STATUS", "O status informado é inválido."));
    private static IResult InvalidToken() => Results.BadRequest(new ApiError("INVALID_CONCURRENCY_TOKEN", "O token de concorrência informado é inválido."));
    private static IResult Modified() => Results.Json(new ApiError("RESOURCE_MODIFIED", "O registro foi alterado por outra operação. Recarregue os dados e tente novamente."), statusCode: StatusCodes.Status409Conflict);
    private static IResult InvalidTransition() => Results.Json(new ApiError("INVALID_VISIT_TRANSITION", "A visita não permite esta operação no estado atual."), statusCode: StatusCodes.Status409Conflict);

    private enum VisitMutation { Start, End, Cancel, Correct }
}

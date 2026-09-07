using System.Security.Claims;
using GestaoPredio.Application.Leases;
using GestaoPredio.Application.Availability;
using GestaoPredio.Application.Scheduling;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Common;
using GestaoPredio.Domain.Leases;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using recepcaototem.Features.Auth;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Leases;

public static partial class LeaseEndpoints
{
    private static readonly string[] Statuses =
        ["all", "AGENDADA", "ATIVA", "ENCERRAMENTO_PENDENTE", "ENCERRADA", "CANCELADA"];

    public static IEndpointRouteBuilder MapLeaseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/leases").RequireAuthorization("Operations");
        group.MapGet("", List);
        group.MapGet("/{id:guid}", Detail);
        group.MapPost("", Create).AddEndpointFilter<AntiforgeryFilter>();
        group.MapPut("/{id:guid}", Update).AddEndpointFilter<AntiforgeryFilter>();
        group.MapPost("/{id:guid}/postpone-occupancy", Postpone).AddEndpointFilter<AntiforgeryFilter>();
        group.MapPost("/{id:guid}/cancel", Cancel).AddEndpointFilter<AntiforgeryFilter>();
        group.MapPost("/{id:guid}/end", End).AddEndpointFilter<AntiforgeryFilter>();
        return endpoints;
    }

    private static async Task<IResult> List(
        int? page,
        int? pageSize,
        string? status,
        string? search,
        Guid? tenantId,
        Guid? professionalId,
        Guid? roomId,
        ApplicationDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryCreateQuery(page, pageSize, status, search, out var query, out var error))
            return Results.BadRequest(error);
        if (tenantId == Guid.Empty || professionalId == Guid.Empty || roomId == Guid.Empty)
            return Results.BadRequest(new ApiError(
                "INVALID_RESOURCE_FILTER", "O filtro de recurso informado é inválido."));

        var now = timeProvider.GetUtcNow();
        var leases = db.Leases.AsNoTracking();
        if (tenantId is not null) leases = leases.Where(x => x.TenantId == tenantId);
        if (professionalId is not null) leases = leases.Where(x => x.ProfessionalId == professionalId);
        if (roomId is not null) leases = leases.Where(x => x.RoomId == roomId);
        leases = ApplyStatus(leases, query!.Status, now);

        var joined =
            from lease in leases
            join tenant in db.Tenants.AsNoTracking() on lease.TenantId equals tenant.Id
            join professional in db.Professionals.AsNoTracking() on lease.ProfessionalId equals professional.Id
            join room in db.Rooms.AsNoTracking() on lease.RoomId equals room.Id
            select new { Lease = lease, Tenant = tenant, Professional = professional, Room = room };

        if (query.Search is not null)
            joined = joined.Where(x => x.Tenant.NormalizedName.Contains(query.Search) ||
                                       x.Professional.NormalizedName.Contains(query.Search) ||
                                       x.Professional.NormalizedProfession.Contains(query.Search) ||
                                       x.Room.NormalizedName.Contains(query.Search));

        var totalCount = await joined.CountAsync(cancellationToken);
        var rows = await joined.OrderByDescending(x => x.Lease.OccupancyStartAt).ThenBy(x => x.Lease.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .ToListAsync(cancellationToken);
        var items = rows.Select(x => x.Lease.ToResponse(
            x.Tenant.Name, x.Professional.Name, x.Room.Name, now)).ToArray();
        return Results.Ok(new PagedResponse<LeaseResponse>(items, query.Page, query.PageSize, totalCount));
    }

    private static async Task<IResult> Detail(
        Guid id,
        ApplicationDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var row = await (
            from lease in db.Leases.AsNoTracking().Where(x => x.Id == id)
            join tenant in db.Tenants.AsNoTracking() on lease.TenantId equals tenant.Id
            join professional in db.Professionals.AsNoTracking() on lease.ProfessionalId equals professional.Id
            join room in db.Rooms.AsNoTracking() on lease.RoomId equals room.Id
            select new { Lease = lease, TenantName = tenant.Name, ProfessionalName = professional.Name, RoomName = room.Name })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null
            ? Results.NotFound()
            : Results.Ok(row.Lease.ToResponse(row.TenantName, row.ProfessionalName, row.RoomName, timeProvider.GetUtcNow()));
    }

    private static async Task<IResult> Create(
        CreateLeaseRequest request,
        HttpContext context,
        ApplicationDbContext db,
        ILeaseResourceLock resourceLock,
        ILeaseConflictDetector conflictDetector,
        IRoomAvailabilityService roomAvailability,
        ILeaseLifecycleCoordinator lifecycle,
        ILeaseOccurrencePlanner occurrencePlanner,
        TimeZoneInfo operationalTimeZone,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryBuildContract(request, operationalTimeZone, out var contract)) return Invalid();
        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest(
            [request.TenantId], [request.RoomId], [request.ProfessionalId]), cancellationToken);

        var tenantActive = await db.Tenants.AnyAsync(x => x.Id == request.TenantId && x.IsActive, cancellationToken);
        var professionalActive = await db.Professionals.AnyAsync(x => x.Id == request.ProfessionalId && x.IsActive, cancellationToken);
        var roomActive = await db.Rooms.AnyAsync(x => x.Id == request.RoomId && x.IsActive, cancellationToken);
        if (!tenantActive || !professionalActive || !roomActive)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Results.BadRequest(new ApiError("INVALID_LEASE_RESOURCE", "Os recursos informados para a locação são inválidos."));
        }

        var overdue = await db.Leases
            .Where(x => (x.RoomId == request.RoomId || x.ProfessionalId == request.ProfessionalId) &&
                        (x.LifecycleState == LeaseLifecycleState.EndingPending ||
                         (x.LifecycleState == LeaseLifecycleState.Open && x.OccupancyEndAt != null && x.OccupancyEndAt <= now)))
            .ToListAsync(cancellationToken);
        foreach (var previous in overdue)
        {
            var previousState = previous.LifecycleState;
            var previousOccurrences = await db.LeaseOccurrences.Where(x => x.LeaseId == previous.Id).ToListAsync(cancellationToken);
            var reconciliation = await lifecycle.ReconcileAsync(previous, previousOccurrences, now, cancellationToken);
            if (previous.LifecycleState != previousState)
            {
                var action = reconciliation == LeaseLifecycleReconciliation.EndingPending
                    ? AuditActions.LeaseEndingPending
                    : AuditActions.LeaseEnded;
                db.AuditEntries.Add(LeaseAudit.CreateSucceeded(previous.Id, action, now, context.TraceIdentifier,
                    context.User.FindFirstValue(ClaimTypes.NameIdentifier), context.Connection.RemoteIpAddress?.ToString()));
            }
        }
        if (overdue.Count > 0) await db.SaveChangesAsync(cancellationToken);

        var roomConflict = await roomAvailability.CheckLeaseRoomAsync(request.RoomId,
            contract!.OccupancyStartAt, contract.OccupancyEndAt,
            enforceOperatingHours: contract.Mode == LeaseMode.Hourly, cancellationToken);
        if (roomConflict != RoomAvailabilityConflict.None)
            return AvailabilityConflict(roomConflict);

        var conflict = await conflictDetector.FindConflictAsync(
            request.RoomId, request.ProfessionalId, contract!.OccupancyStartAt,
            contract.OccupancyEndAt, null, cancellationToken);
        if (conflict.Any)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Results.Json(new ApiError("LEASE_RESOURCE_CONFLICT", "A sala ou o profissional já possui ocupação conflitante."),
                statusCode: StatusCodes.Status409Conflict);
        }

        var lease = Lease.Create(
            request.TenantId, request.ProfessionalId, request.RoomId, contract.Mode,
            request.ContractedRate, request.BillingStartAt, request.BillingDueDay,
            contract.OccupancyStartAt, contract.OccupancyEndAt, contract.MonthlyAnchorDay, now);
        var plan = occurrencePlanner.Plan(lease, now, []);
        lease.SetMaterializedThrough(plan.MaterializedThroughAt);
        db.Leases.Add(lease);
        foreach (var period in plan.ToCreate)
            db.LeaseOccurrences.Add(LeaseOccurrence.Create(lease.Id, period.StartAt, period.EndAt, now));
        db.AuditEntries.Add(LeaseAudit.CreateSucceeded(
            lease.Id, AuditActions.LeaseCreated, now, context.TraceIdentifier,
            context.User.FindFirstValue(ClaimTypes.NameIdentifier), context.Connection.RemoteIpAddress?.ToString()));

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var names = await LoadNames(db, lease, cancellationToken);
        return Results.Created($"/api/admin/leases/{lease.Id}",
            lease.ToResponse(names.Tenant, names.Professional, names.Room, now));
    }

    private static IResult AvailabilityConflict(RoomAvailabilityConflict conflict) => conflict switch
    {
        RoomAvailabilityConflict.OutsideOperatingHours => Results.Json(new ApiError(
            "ROOM_OUTSIDE_OPERATING_HOURS", "O período está fora do horário de funcionamento."),
            statusCode: StatusCodes.Status409Conflict),
        RoomAvailabilityConflict.RoomBlock => Results.Json(new ApiError(
            "ROOM_BLOCKED", "A sala está bloqueada no período informado."),
            statusCode: StatusCodes.Status409Conflict),
        RoomAvailabilityConflict.Reservation => Results.Json(new ApiError(
            "LEASE_RESOURCE_CONFLICT", "A sala ou o profissional já possui ocupação conflitante."),
            statusCode: StatusCodes.Status409Conflict),
        _ => throw new InvalidOperationException("Conflito de disponibilidade inesperado.")
    };

    private static IQueryable<Lease> ApplyStatus(IQueryable<Lease> query, string status, DateTimeOffset now) => status switch
    {
        "AGENDADA" => query.Where(x => x.LifecycleState == LeaseLifecycleState.Open && x.OccupancyStartAt > now),
        "ATIVA" => query.Where(x => x.LifecycleState == LeaseLifecycleState.Open && x.OccupancyStartAt <= now &&
                                     (x.OccupancyEndAt == null || x.OccupancyEndAt > now)),
        "ENCERRAMENTO_PENDENTE" => query.Where(x => x.LifecycleState == LeaseLifecycleState.EndingPending ||
            (x.LifecycleState == LeaseLifecycleState.Open && x.OccupancyEndAt != null && x.OccupancyEndAt <= now)),
        "ENCERRADA" => query.Where(x => x.LifecycleState == LeaseLifecycleState.Ended),
        "CANCELADA" => query.Where(x => x.LifecycleState == LeaseLifecycleState.Cancelled),
        _ => query
    };

    private static bool TryCreateQuery(int? page, int? pageSize, string? status, string? search,
        out LeaseQuery? query, out ApiError? error)
    {
        var actualPage = page ?? 1;
        if (actualPage < 1) return Fail("INVALID_PAGE", "A página deve ser maior ou igual a 1.", out query, out error);
        var actualSize = pageSize ?? 20;
        if (actualSize is < 1 or > 100) return Fail("INVALID_PAGE_SIZE", "O tamanho da página deve estar entre 1 e 100.", out query, out error);
        var actualStatus = string.IsNullOrWhiteSpace(status) ? "all" : status.Trim().ToUpperInvariant();
        if (actualStatus == "ALL") actualStatus = "all";
        if (!Statuses.Contains(actualStatus, StringComparer.Ordinal))
            return Fail("INVALID_STATUS", "O status informado é inválido.", out query, out error);
        var actualSearch = string.IsNullOrWhiteSpace(search) ? null : TextNormalizer.Normalize(search);
        if (actualSearch?.Length > 100)
            return Fail("INVALID_SEARCH", "A busca deve possuir no máximo 100 caracteres.", out query, out error);
        query = new LeaseQuery(actualPage, actualSize, actualStatus, actualSearch);
        error = null;
        return true;
    }

    private static bool TryBuildContract(CreateLeaseRequest request, TimeZoneInfo timeZone, out LeaseContract? contract)
    {
        contract = null;
        if (request.TenantId == Guid.Empty || request.ProfessionalId == Guid.Empty || request.RoomId == Guid.Empty ||
            !RoomRate.IsValid(request.ContractedRate) || request.BillingDueDay is < 1 or > 31)
            return false;
        var mode = request.Mode?.Trim().ToUpperInvariant() switch
        {
            "MONTHLY" => LeaseMode.Monthly,
            "DAILY" => LeaseMode.Daily,
            "HOURLY" => LeaseMode.Hourly,
            _ => (LeaseMode?)null
        };
        if (mode is null) return false;
        var start = request.OccupancyStartAt;
        var end = request.OccupancyEndAt;
        int? anchor = null;
        if (mode == LeaseMode.Daily)
        {
            var local = TimeZoneInfo.ConvertTime(start, timeZone);
            var day = OperationalTimeZone.GetCivilDayInterval(DateOnly.FromDateTime(local.DateTime), timeZone);
            start = day.StartAt;
            end = day.EndAt;
        }
        else if (mode == LeaseMode.Monthly)
        {
            anchor = TimeZoneInfo.ConvertTime(start, timeZone).Day;
        }
        try
        {
            _ = Lease.Create(request.TenantId, request.ProfessionalId, request.RoomId, mode.Value,
                request.ContractedRate, request.BillingStartAt, request.BillingDueDay, start, end, anchor,
                DateTimeOffset.UtcNow);
        }
        catch (ArgumentException)
        {
            return false;
        }
        contract = new LeaseContract(mode.Value, start, end, anchor);
        return true;
    }

    private static async Task<(string Tenant, string Professional, string Room)> LoadNames(
        ApplicationDbContext db, Lease lease, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.AsNoTracking().Where(x => x.Id == lease.TenantId).Select(x => x.Name).SingleAsync(cancellationToken);
        var professional = await db.Professionals.AsNoTracking().Where(x => x.Id == lease.ProfessionalId).Select(x => x.Name).SingleAsync(cancellationToken);
        var room = await db.Rooms.AsNoTracking().Where(x => x.Id == lease.RoomId).Select(x => x.Name).SingleAsync(cancellationToken);
        return (tenant, professional, room);
    }

    private static bool Fail(string code, string message, out LeaseQuery? query, out ApiError? error)
    {
        query = null;
        error = new ApiError(code, message);
        return false;
    }

    private static IResult Invalid() => Results.BadRequest(new ApiError(
        "INVALID_LEASE", "Os dados da locação são inválidos."));

    private sealed record LeaseQuery(int Page, int PageSize, string Status, string? Search);
    private sealed record LeaseContract(LeaseMode Mode, DateTimeOffset OccupancyStartAt, DateTimeOffset? OccupancyEndAt, int? MonthlyAnchorDay);
}

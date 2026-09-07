using System.Security.Claims;
using GestaoPredio.Application.Leases;
using GestaoPredio.Application.OperationalAlerts;
using GestaoPredio.Application.Reservations;
using GestaoPredio.Application.Scheduling;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Common;
using GestaoPredio.Domain.Customers;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Visits;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using recepcaototem.Features.Common;
using recepcaototem.Features.Auth;
using recepcaototem.Features.Reservations;
using recepcaototem.Features.Totem;
using recepcaototem.Features.Visits;

namespace recepcaototem.Features.Reception;

public static class ReceptionEndpoints
{
    public static IEndpointRouteBuilder MapReceptionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/reception").RequireAuthorization("Operations");
        group.MapGet("/overview", Overview);
        group.MapGet("/professionals", Professionals);
        group.MapGet("/rooms", Rooms);
        group.MapGet("/agenda", Agenda);
        group.MapGet("/reservations", Reservations);
        group.MapGet("/visits", Visits);
        group.MapGet("/visits/{id:guid}", VisitDetail);
        group.MapPost("/reservations", AssistedReservation).AddEndpointFilter<AntiforgeryFilter>();
        group.MapPost("/reservations/{id:guid}/check-in", ManualCheckIn).AddEndpointFilter<AntiforgeryFilter>();
        group.MapPost("/visits/{id:guid}/start", VisitEndpoints.StartForReception).AddEndpointFilter<AntiforgeryFilter>();
        group.MapPost("/visits/{id:guid}/end", VisitEndpoints.EndForReception).AddEndpointFilter<AntiforgeryFilter>();
        group.MapPost("/visits/{id:guid}/cancel", VisitEndpoints.CancelForReception).AddEndpointFilter<AntiforgeryFilter>();
        return endpoints;
    }

    private static async Task<IResult> Overview(ApplicationDbContext db, IOperationalAlertReader alerts,
        GestaoPredio.Application.Availability.IRoomAvailabilityService roomAvailability, TimeProvider time,
        TimeZoneInfo zone, CancellationToken ct)
    {
        var now = time.GetUtcNow().ToUniversalTime();
        var day = OperationalTimeZone.GetCivilDayInterval(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime), zone);
        var rooms = await roomAvailability.ReadRoomOperationalStatusAsync(now, ct);
        var openVisits = await LoadVisits(db, null, null, null, false, null, ct);
        var currentReservations = await LoadAgenda(db, day.StartAt, day.EndAt, null, null, null, now, 10, ct);
        var alertRows = await alerts.ReadAsync(new OperationalAlertFilter(null, null, null, null), now, ct);
        var activeProfessionals = await db.Professionals.AsNoTracking().CountAsync(x => x.IsActive, ct);
        var inServiceProfessionals = openVisits.Where(x => x.Status == "IN_SERVICE").Select(x => x.ProfessionalId).Distinct().Count();
        var reservationsToday = await db.Reservations.AsNoTracking().CountAsync(x => x.Status == ReservationStatus.Approved &&
            x.Kind != ReservationKind.Cancellation && x.StartAt < day.EndAt && x.EndAt > day.StartAt, ct);
        return Results.Ok(new ReceptionOverviewResponse(
            openVisits.Count(x => x.Status == "WAITING"), openVisits.Count(x => x.Status == "IN_SERVICE"),
            Math.Max(0, activeProfessionals - inServiceProfessionals - openVisits.Where(x => x.Status == "WAITING").Select(x => x.ProfessionalId).Distinct().Count()), inServiceProfessionals,
            rooms.Count(x => x.Status == "AVAILABLE"), rooms.Count(x => x.Status == "OCCUPIED"), reservationsToday,
            currentReservations, openVisits.Where(x => x.Status == "WAITING").Take(20).ToArray(),
            openVisits.Where(x => x.Status == "IN_SERVICE").Take(20).ToArray(),
            alertRows.Count(x => x.Severity == OperationalAlertSeverity.Warning),
            alertRows.Count(x => x.Severity == OperationalAlertSeverity.Critical)));
    }

    private static async Task<IResult> Professionals(string? search, string? status, Guid? roomId,
        ApplicationDbContext db, GestaoPredio.Application.Availability.IRoomAvailabilityService roomAvailability,
        TimeProvider time, CancellationToken ct)
    {
        if (roomId == Guid.Empty) return Bad("INVALID_RECEPTION_FILTER");
        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : TextNormalizer.Normalize(search);
        var query = db.Professionals.AsNoTracking();
        if (normalizedSearch is not null) query = query.Where(x => x.NormalizedName.Contains(normalizedSearch) || x.NormalizedProfession.Contains(normalizedSearch));
        var professionals = await query.OrderBy(x => x.NormalizedName).ThenBy(x => x.Id).Select(x => new { x.Id, x.Name, x.Profession, x.Description, x.PhotoFileId, x.IsActive }).ToListAsync(ct);
        var roomStatuses = await roomAvailability.ReadRoomOperationalStatusAsync(time.GetUtcNow(), ct);
        var now = time.GetUtcNow();
        var visits = await db.Visits.AsNoTracking().Where(x => x.Status == VisitStatus.Waiting || x.Status == VisitStatus.InService)
            .Select(x => new { x.Id, x.ProfessionalId, x.RoomId, x.Status, x.ArrivedAt }).ToListAsync(ct);
        var reservations = await db.Reservations.AsNoTracking().Where(x => x.Status == ReservationStatus.Approved && x.Kind != ReservationKind.Cancellation && x.EndAt > now)
            .Select(x => new { x.ProfessionalId, x.RoomId, x.StartAt }).ToListAsync(ct);
        var rows = professionals.Select(p =>
        {
            var current = visits.Where(x => x.ProfessionalId == p.Id).OrderBy(x => x.Status == VisitStatus.InService ? 0 : 1).ThenBy(x => x.ArrivedAt).FirstOrDefault();
            var next = reservations.Where(x => x.ProfessionalId == p.Id && x.StartAt > now).OrderBy(x => x.StartAt).FirstOrDefault();
            var hasUsableRoom = roomStatuses.Any(x => x.Status is "AVAILABLE" or "RESERVED");
            var operational = current?.Status == VisitStatus.InService ? "IN_SERVICE" : current?.Status == VisitStatus.Waiting ? "WAITING_VISITOR" : !p.IsActive || !hasUsableRoom ? "UNAVAILABLE" : "AVAILABLE";
            return new ReceptionProfessionalResponse(p.Id, p.Name, p.Profession, p.Description, p.PhotoFileId is not null,
                p.PhotoFileId is null ? null : $"/api/admin/professionals/{p.Id}/photo", operational,
                current?.RoomId, current?.Id, visits.Count(x => x.ProfessionalId == p.Id && x.Status == VisitStatus.Waiting),
                next?.StartAt, p.IsActive && operational is not "IN_SERVICE" and not "UNAVAILABLE");
        }).Where(x => roomId is null || x.CurrentRoomId == roomId || reservations.Any(r => r.ProfessionalId == x.ProfessionalId && r.RoomId == roomId)).ToArray();
        if (!string.IsNullOrWhiteSpace(status) && !new[] { "ALL", "AVAILABLE", "WAITING_VISITOR", "IN_SERVICE", "UNAVAILABLE" }.Contains(status.Trim().ToUpperInvariant())) return Bad("INVALID_STATUS");
        var selectedStatus = string.IsNullOrWhiteSpace(status) || status.Trim().Equals("all", StringComparison.OrdinalIgnoreCase) ? null : status.Trim().ToUpperInvariant();
        return Results.Ok(selectedStatus is null ? rows : rows.Where(x => x.OperationalStatus == selectedStatus).ToArray());
    }

    private static async Task<IResult> Rooms(ApplicationDbContext db,
        GestaoPredio.Application.Availability.IRoomAvailabilityService availability, TimeProvider time, CancellationToken ct)
    {
        var statuses = await availability.ReadRoomOperationalStatusAsync(time.GetUtcNow(), ct);
        var ids = statuses.Select(x => x.RoomId).ToArray();
        var visits = await (from visit in db.Visits.AsNoTracking().Where(x => x.RoomId != null && ids.Contains(x.RoomId.Value) && (x.Status == VisitStatus.Waiting || x.Status == VisitStatus.InService))
                            join professional in db.Professionals.AsNoTracking() on visit.ProfessionalId equals professional.Id
                            select new { visit.RoomId, visit.Id, visit.ProfessionalId, ProfessionalName = professional.Name }).ToListAsync(ct);
        return Results.Ok(statuses.Select(room =>
        {
            var visit = visits.FirstOrDefault(x => x.RoomId == room.RoomId);
            return new ReceptionRoomResponse(room.RoomId, room.RoomName, room.Status, visit?.ProfessionalId,
                visit?.ProfessionalName, visit?.Id, room.Status == "BLOCKED", room.NextCommitmentAt,
                room.Status == "AVAILABLE");
        }).ToArray());
    }

    private static async Task<IResult> Agenda(DateOnly? date, Guid? professionalId, Guid? roomId,
        ApplicationDbContext db, TimeZoneInfo zone, TimeProvider time, CancellationToken ct)
    {
        if (professionalId == Guid.Empty || roomId == Guid.Empty) return Bad("INVALID_RECEPTION_FILTER");
        var localDate = date ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).DateTime);
        var interval = OperationalTimeZone.GetCivilDayInterval(localDate, zone);
        return Results.Ok(await LoadAgenda(db, interval.StartAt, interval.EndAt, professionalId, roomId, null, time.GetUtcNow(), 200, ct));
    }

    private static async Task<IResult> Reservations(string? search, string? phone, DateTimeOffset? from,
        DateTimeOffset? to, Guid? professionalId, Guid? roomId, ApplicationDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (professionalId == Guid.Empty || roomId == Guid.Empty || from >= to) return Bad("INVALID_RECEPTION_FILTER");
        var normalizedPhone = string.IsNullOrWhiteSpace(phone) ? null : WhatsAppNormalizer.TryNormalize(phone, out var p) ? p : "!invalid";
        if (normalizedPhone == "!invalid") return Bad("INVALID_PHONE");
        var rows = await LoadReservations(db, from, to, professionalId, roomId, normalizedPhone, search, time.GetUtcNow(), 100, ct);
        return Results.Ok(new PagedResponse<ReceptionReservationItem>(rows, 1, rows.Count, rows.Count));
    }

    private static async Task<IResult> Visits(int? page, int? pageSize, string? status, Guid? professionalId,
        Guid? roomId, string? search, DateTimeOffset? from, DateTimeOffset? to, ApplicationDbContext db, CancellationToken ct)
    {
        var p = page ?? 1; var size = pageSize ?? 20;
        if (p < 1) return Bad("INVALID_PAGE");
        if (size is < 1 or > 100) return Bad("INVALID_PAGE_SIZE");
        if (professionalId == Guid.Empty || roomId == Guid.Empty || from >= to) return Bad("INVALID_RECEPTION_FILTER");
        if (!TryStatus(status, out var parsed, out var all)) return Bad("INVALID_STATUS");
        var rows = await LoadVisits(db, professionalId, roomId, parsed, all, search, ct, from, to);
        var total = rows.Count; return Results.Ok(new PagedResponse<ReceptionVisitItem>(rows.Skip((p - 1) * size).Take(size).ToArray(), p, size, total));
    }

    private static async Task<IResult> VisitDetail(Guid id, ApplicationDbContext db, CancellationToken ct)
    {
        var rows = await LoadVisits(db, null, null, null, true, null, ct, null, null, id);
        return rows.Count == 0 ? Results.NotFound() : Results.Ok(rows[0]);
    }

    private static Task<IResult> AssistedReservation(TotemReservationRequest request, HttpContext context,
        ApplicationDbContext db, ILeaseResourceLock resourceLock, IReservationConflictDetector conflicts,
        GestaoPredio.Application.Availability.IRoomAvailabilityService availability, TimeProvider time, CancellationToken ct) =>
        TotemEndpoints.CreateAssistedReservation(request, context, db, resourceLock, conflicts, availability, time, ct);

    private static async Task<IResult> ManualCheckIn(Guid id, ReceptionCheckInRequest request, HttpContext context,
        ApplicationDbContext db, ILeaseResourceLock resourceLock, TimeProvider time, CancellationToken ct)
    {
        if (!ConcurrencyToken.TryDecode(request.ConcurrencyToken, out var version)) return Bad("INVALID_CONCURRENCY_TOKEN");
        var locator = await db.Reservations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (locator is null) return Results.NotFound();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest([], [locator.RoomId], [locator.ProfessionalId]), ct);
        var reservation = await db.Reservations.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (reservation is null) return Results.NotFound();
        if (reservation.Version != version) return Modified();
        if (reservation.Status != ReservationStatus.Approved || reservation.Kind == ReservationKind.Cancellation) return InvalidTransition();
        var customer = reservation.CustomerId is null ? null : await db.Customers.SingleOrDefaultAsync(x => x.Id == reservation.CustomerId, ct);
        if (reservation.CustomerId is not null && (customer is null || !customer.IsActive)) return Bad("INVALID_VISIT");
        var customerName = customer?.Name;
        var existing = await db.Visits.SingleOrDefaultAsync(x => x.ReservationId == id && (x.Status == VisitStatus.Waiting || x.Status == VisitStatus.InService), ct);
        if (existing is not null) return Results.Ok(new ReceptionVisitResponse(existing.Id, existing.ProfessionalId, existing.RoomId, existing.ReservationId, existing.CustomerId, existing.VisitorName, Contract(existing.Status), existing.ArrivedAt, existing.ServiceStartedAt, existing.EndedAt, ConcurrencyToken.Encode(existing.Version)));
        var visitorName = customerName ?? request.VisitorName?.Trim();
        if (string.IsNullOrWhiteSpace(visitorName)) return Bad("INVALID_VISIT");
        var now = time.GetUtcNow();
        var visit = Visit.Arrive(reservation.ProfessionalId, reservation.RoomId, reservation.Id, visitorName, Actor(context)!, now, reservation.CustomerId);
        db.Visits.Add(visit);
        db.VisitTransitions.Add(VisitTransition.Record(visit.Id, null, VisitStatus.Waiting, Actor(context)!, now));
        db.AuditEntries.Add(new AuditEntry { Id = Guid.NewGuid(), Action = "VISIT_CHECKED_IN_MANUAL", Result = "SUCCEEDED", TargetEntityType = "VISIT", TargetEntityId = visit.Id, TargetUserId = Actor(context), OccurredAt = now, CorrelationId = context.TraceIdentifier });
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        catch (DbUpdateConcurrencyException) { await transaction.RollbackAsync(ct); return Modified(); }
        return Results.Created($"/api/reception/visits/{visit.Id}", new ReceptionVisitResponse(visit.Id, visit.ProfessionalId, visit.RoomId, visit.ReservationId, visit.CustomerId, visit.VisitorName, "WAITING", visit.ArrivedAt, null, null, ConcurrencyToken.Encode(visit.Version)));
    }

    private static async Task<List<ReceptionAgendaItem>> LoadAgenda(ApplicationDbContext db, DateTimeOffset fromAt, DateTimeOffset toAt,
        Guid? professionalId, Guid? roomId, string? search, DateTimeOffset now, int take, CancellationToken ct)
    {
        var query = from r in db.Reservations.AsNoTracking()
                    join p in db.Professionals.AsNoTracking() on r.ProfessionalId equals p.Id
                    join room in db.Rooms.AsNoTracking() on r.RoomId equals room.Id
                    join c in db.Customers.AsNoTracking() on r.CustomerId equals c.Id into customers
                    from c in customers.DefaultIfEmpty()
                    where r.Status == ReservationStatus.Approved && r.Kind != ReservationKind.Cancellation && r.StartAt < toAt && r.EndAt > fromAt
                    select new { r, p.Name, ProfessionalId = p.Id, RoomName = room.Name, CustomerName = c == null ? null : c.Name, CustomerPhone = c == null ? null : c.Phone, CustomerId = r.CustomerId };
        if (professionalId is not null) query = query.Where(x => x.ProfessionalId == professionalId);
        if (roomId is not null) query = query.Where(x => x.r.RoomId == roomId);
        var rows = await query.OrderBy(x => x.r.StartAt).ThenBy(x => x.r.Id).Take(take).ToListAsync(ct);
        var ids = rows.Select(x => x.r.Id).ToArray();
        var visits = await db.Visits.AsNoTracking().Where(x => x.ReservationId != null && ids.Contains(x.ReservationId.Value)).Select(x => new { x.Id, x.ReservationId, x.Status }).ToListAsync(ct);
        return rows.Select(x => { var v = visits.FirstOrDefault(y => y.ReservationId == x.r.Id && (y.Status == VisitStatus.Waiting || y.Status == VisitStatus.InService)); return new ReceptionAgendaItem(x.r.Id, x.CustomerId, x.CustomerName, x.CustomerPhone, x.ProfessionalId, x.Name, x.r.RoomId, x.RoomName, x.r.StartAt, x.r.EndAt, x.r.Status.ToString().ToUpperInvariant(), v?.Id, v is null ? null : Contract(v.Status), v is null && now >= x.r.StartAt.Subtract(TimeSpan.FromHours(1)) && now < x.r.EndAt); }).ToList();
    }

    private static async Task<List<ReceptionReservationItem>> LoadReservations(ApplicationDbContext db, DateTimeOffset? fromAt, DateTimeOffset? toAt, Guid? professionalId, Guid? roomId, string? phone, string? search, DateTimeOffset now, int take, CancellationToken ct)
    {
        var query = from r in db.Reservations.AsNoTracking()
                    join p in db.Professionals.AsNoTracking() on r.ProfessionalId equals p.Id
                    join room in db.Rooms.AsNoTracking() on r.RoomId equals room.Id
                    join c in db.Customers.AsNoTracking() on r.CustomerId equals c.Id into customers
                    from c in customers.DefaultIfEmpty()
                    where r.Kind != ReservationKind.Cancellation
                    select new { r, p.Name, roomName = room.Name, p.Id, CustomerName = c == null ? null : c.Name, CustomerPhone = c == null ? null : c.Phone };
        if (fromAt is not null) query = query.Where(x => x.r.EndAt > fromAt);
        if (toAt is not null) query = query.Where(x => x.r.StartAt < toAt);
        if (professionalId is not null) query = query.Where(x => x.r.ProfessionalId == professionalId);
        if (roomId is not null) query = query.Where(x => x.r.RoomId == roomId);
        if (phone is not null) query = query.Where(x => x.CustomerPhone == phone);
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim(); query = query.Where(x => (x.CustomerName != null && x.CustomerName.Contains(term)) || x.Name.Contains(term)); }
        var rows = await query.OrderByDescending(x => x.r.StartAt).ThenBy(x => x.r.Id).Take(take).ToListAsync(ct);
        var ids = rows.Select(x => x.r.Id).ToArray(); var visits = await db.Visits.AsNoTracking().Where(x => x.ReservationId != null && ids.Contains(x.ReservationId.Value)).Select(x => new { x.Id, x.ReservationId, x.Status }).ToListAsync(ct);
        return rows.Select(x => { var v = visits.FirstOrDefault(y => y.ReservationId == x.r.Id && (y.Status == VisitStatus.Waiting || y.Status == VisitStatus.InService)); return new ReceptionReservationItem(x.r.Id, x.r.CustomerId, x.CustomerName, x.CustomerPhone, x.r.ProfessionalId, x.Name, x.r.RoomId, x.roomName, x.r.StartAt, x.r.EndAt, x.r.Status.ToString().ToUpperInvariant(), v?.Id, v is null ? null : Contract(v.Status), ConcurrencyToken.Encode(x.r.Version)); }).ToList();
    }

    private static async Task<List<ReceptionVisitItem>> LoadVisits(ApplicationDbContext db, Guid? professionalId, Guid? roomId, VisitStatus? status, bool all, string? search, CancellationToken ct, DateTimeOffset? from = null, DateTimeOffset? to = null, Guid? id = null)
    {
        var query = from v in db.Visits.AsNoTracking()
                    join p in db.Professionals.AsNoTracking() on v.ProfessionalId equals p.Id
                    join r in db.Rooms.AsNoTracking() on v.RoomId equals r.Id into rooms
                    from r in rooms.DefaultIfEmpty()
                    join c in db.Customers.AsNoTracking() on v.CustomerId equals c.Id into customers
                    from c in customers.DefaultIfEmpty()
                    select new { v, p.Name, RoomName = r == null ? null : r.Name, CustomerName = c == null ? null : c.Name };
        if (id is not null) query = query.Where(x => x.v.Id == id);
        if (professionalId is not null) query = query.Where(x => x.v.ProfessionalId == professionalId);
        if (roomId is not null) query = query.Where(x => x.v.RoomId == roomId);
        if (!all && status is not null) query = query.Where(x => x.v.Status == status);
        else if (!all) query = query.Where(x => x.v.Status == VisitStatus.Waiting || x.v.Status == VisitStatus.InService);
        if (from is not null) query = query.Where(x => x.v.ArrivedAt >= from);
        if (to is not null) query = query.Where(x => x.v.ArrivedAt < to);
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim(); query = query.Where(x => x.v.VisitorName.Contains(term) || x.CustomerName != null && x.CustomerName.Contains(term)); }
        var rows = await query.OrderBy(x => x.v.Status == VisitStatus.Waiting ? 0 : x.v.Status == VisitStatus.InService ? 1 : 2).ThenByDescending(x => x.v.ArrivedAt).ThenBy(x => x.v.Id).ToListAsync(ct);
        return rows.Select(x => new ReceptionVisitItem(x.v.Id, x.v.ProfessionalId, x.Name, x.v.RoomId, x.RoomName, x.v.ReservationId, x.v.CustomerId, x.CustomerName, x.v.VisitorName, Contract(x.v.Status), x.v.ArrivedAt, x.v.ServiceStartedAt, x.v.EndedAt, ConcurrencyToken.Encode(x.v.Version))).ToList();
    }

    private static bool TryStatus(string? value, out VisitStatus status, out bool all)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "ALL" : value.Trim().ToUpperInvariant(); all = normalized == "ALL";
        status = normalized switch { "WAITING" => VisitStatus.Waiting, "IN_SERVICE" => VisitStatus.InService, "ENDED" => VisitStatus.Ended, "CANCELLED" => VisitStatus.Cancelled, _ => default }; return all || normalized is "WAITING" or "IN_SERVICE" or "ENDED" or "CANCELLED";
    }
    private static string Contract(VisitStatus status) => status switch { VisitStatus.Waiting => "WAITING", VisitStatus.InService => "IN_SERVICE", VisitStatus.Ended => "ENDED", VisitStatus.Cancelled => "CANCELLED", _ => throw new ArgumentOutOfRangeException(nameof(status)) };
    private static string? Actor(HttpContext context) => context.User.FindFirstValue(ClaimTypes.NameIdentifier);
    private static IResult Bad(string code) => Results.BadRequest(new ApiError(code, "Os dados informados são inválidos."));
    private static IResult Modified() => Results.Json(new ApiError("RESOURCE_MODIFIED", "O registro foi alterado por outra operação. Recarregue os dados e tente novamente."), statusCode: 409);
    private static IResult InvalidTransition() => Results.Json(new ApiError("INVALID_VISIT_TRANSITION", "A operação não é válida no estado atual."), statusCode: 409);
}

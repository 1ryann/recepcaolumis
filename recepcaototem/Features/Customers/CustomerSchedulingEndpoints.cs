using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using GestaoPredio.Application.Availability;
using GestaoPredio.Application.Leases;
using GestaoPredio.Application.Reservations;
using GestaoPredio.Application.Scheduling;
using GestaoPredio.Domain.Customers;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using recepcaototem.Api.Configuration;
using recepcaototem.Features.Auth;
using recepcaototem.Features.Common;
using recepcaototem.Features.Reservations;

namespace recepcaototem.Features.Customers;

public sealed record CustomerAvailabilityRequest(Guid ProfessionalId, DateOnly Date, int DurationMinutes) : IStrictModuleRequest;
public sealed record CustomerReservationRequest(Guid ProfessionalId, DateTimeOffset StartAt, DateTimeOffset EndAt) : IStrictModuleRequest;
public sealed record CustomerReservationRescheduleRequest(Guid ProfessionalId, DateTimeOffset StartAt, DateTimeOffset EndAt, string? ConcurrencyToken) : IStrictModuleRequest;
public sealed record CustomerReservationConcurrencyRequest(string? ConcurrencyToken) : IStrictModuleRequest;
public sealed record CustomerReservationPageResponse(ReservationResponse[] Items, int Page, int PageSize, int TotalCount);
public sealed record CustomerProfessionalResponse(Guid Id, string Name, string Profession);
public sealed record AvailabilitySlotResponse(DateTimeOffset StartAt, DateTimeOffset EndAt);

public static class CustomerSchedulingEndpoints
{
    public static IEndpointRouteBuilder MapCustomerSchedulingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/customer").RequireAuthorization(IdentityConfiguration.CustomerPolicy);
        group.MapGet("/professionals", ListProfessionals);
        group.MapGet("/availability", Availability);
        group.MapGet("/reservations", ListReservations);
        group.MapGet("/reservations/{id:guid}", ReservationDetail);
        group.MapPost("/reservations", CreateReservation).AddEndpointFilter<AntiforgeryFilter>();
        group.MapPost("/reservations/{id:guid}/cancel", CancelReservation).AddEndpointFilter<AntiforgeryFilter>();
        group.MapPost("/reservations/{id:guid}/reschedule", RescheduleReservation).AddEndpointFilter<AntiforgeryFilter>();
        group.MapPost("/reservations/{id:guid}/check-in-token", IssueToken).AddEndpointFilter<AntiforgeryFilter>();
        return endpoints;
    }

    private static async Task<IResult> ListProfessionals(ApplicationDbContext db, CancellationToken ct) =>
        Results.Ok(await db.Professionals.AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.NormalizedName).ThenBy(x => x.Id)
            .Select(x => new CustomerProfessionalResponse(x.Id, x.Name, x.Profession)).ToArrayAsync(ct));

    internal static async Task<IResult> Availability([AsParameters] CustomerAvailabilityRequest request, ApplicationDbContext db,
        IRoomAvailabilityService availability, IReservationConflictDetector conflicts, TimeZoneInfo timeZone,
        CancellationToken ct)
    {
        if (request.ProfessionalId == Guid.Empty || request.DurationMinutes is < 15 or > 480 || request.DurationMinutes % 15 != 0)
            return Results.BadRequest(new ApiError("INVALID_AVAILABILITY", "Os dados de disponibilidade são inválidos."));
        if (!await db.Professionals.AnyAsync(x => x.Id == request.ProfessionalId && x.IsActive, ct)) return Results.NotFound();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var intervals = await db.OperatingHourIntervals.AsNoTracking().ToListAsync(ct);
        var slots = new List<AvailabilitySlotResponse>();
        var localStart = request.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        for (var minute = 0; minute < 24 * 60; minute += 15)
        {
            var startLocal = localStart.AddMinutes(minute);
            var endLocal = startLocal.AddMinutes(request.DurationMinutes);
            if (endLocal.Date != request.Date.ToDateTime(TimeOnly.MinValue).Date) continue;
            var start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(startLocal, timeZone));
            var end = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(endLocal, timeZone));
            if (!intervals.Any() || !new OperatingHoursEvaluator(timeZone).Contains(intervals, start, end)) continue;
            var rooms = await db.Rooms.AsNoTracking().Where(x => x.IsActive).Select(x => x.Id).ToListAsync(ct);
            foreach (var roomId in rooms)
            {
                var roomConflict = await availability.CheckScheduleAndBlocksAsync(roomId, start, end, null, true, ct);
                var resourceConflict = await conflicts.FindConflictAsync(roomId, request.ProfessionalId, start, end, null, ct);
                if (roomConflict == RoomAvailabilityConflict.None && !resourceConflict.Any)
                {
                    slots.Add(new AvailabilitySlotResponse(start, end));
                    break;
                }
            }
        }
        return Results.Ok(slots.ToArray());
    }

    private static async Task<Customer?> GetCustomer(ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrWhiteSpace(userId) ? null : await db.Customers.SingleOrDefaultAsync(x => x.ApplicationUserId == userId && x.IsActive, ct);
    }

    private static async Task<IResult> ListReservations(ClaimsPrincipal principal, ApplicationDbContext db, int? page, int? pageSize, CancellationToken ct)
    {
        var customer = await GetCustomer(principal, db, ct); if (customer is null) return Results.NotFound();
        var p = page ?? 1; var size = pageSize ?? 20;
        if (p < 1) return Results.BadRequest(new ApiError("INVALID_PAGE", "A página é inválida."));
        if (size is < 1 or > 100) return Results.BadRequest(new ApiError("INVALID_PAGE_SIZE", "O tamanho da página é inválido."));
        var query = from reservation in db.Reservations.AsNoTracking().Where(x => x.CustomerId == customer.Id)
                    join room in db.Rooms.AsNoTracking() on reservation.RoomId equals room.Id
                    join professional in db.Professionals.AsNoTracking() on reservation.ProfessionalId equals professional.Id
                    select new { reservation, room.Name, ProfessionalName = professional.Name };
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.reservation.StartAt).ThenBy(x => x.reservation.Id).Skip((p - 1) * size).Take(size).ToListAsync(ct);
        return Results.Ok(new CustomerReservationPageResponse(rows.Select(x => x.reservation.ToResponse(x.Name, x.ProfessionalName)).ToArray(), p, size, total));
    }

    private static async Task<IResult> ReservationDetail(Guid id, ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken ct)
    {
        var customer = await GetCustomer(principal, db, ct); if (customer is null) return Results.NotFound();
        var row = await (from reservation in db.Reservations.AsNoTracking().Where(x => x.Id == id && x.CustomerId == customer.Id)
                         join room in db.Rooms.AsNoTracking() on reservation.RoomId equals room.Id
                         join professional in db.Professionals.AsNoTracking() on reservation.ProfessionalId equals professional.Id
                         select new { reservation, room.Name, ProfessionalName = professional.Name }).SingleOrDefaultAsync(ct);
        return row is null ? Results.NotFound() : Results.Ok(row.reservation.ToResponse(row.Name, row.ProfessionalName));
    }

    private static async Task<IResult> CreateReservation(CustomerReservationRequest request, ClaimsPrincipal principal,
        HttpContext context, ApplicationDbContext db, ILeaseResourceLock resourceLock, IReservationConflictDetector conflicts,
        IRoomAvailabilityService availability, TimeProvider time, CancellationToken ct)
    {
        var customer = await GetCustomer(principal, db, ct); if (customer is null) return Results.NotFound();
        if (request.ProfessionalId == Guid.Empty || request.EndAt <= request.StartAt) return Results.BadRequest(new ApiError("INVALID_RESERVATION", "Os dados da reserva são inválidos."));
        var professional = await db.Professionals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ProfessionalId && x.IsActive, ct);
        if (professional is null) return Results.NotFound();
        var roomIds = await db.Rooms.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Id).Select(x => x.Id).ToListAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest([], roomIds, [request.ProfessionalId]), ct);
        foreach (var roomId in roomIds)
        {
            if (await availability.CheckScheduleAndBlocksAsync(roomId, request.StartAt, request.EndAt, null, true, ct) != RoomAvailabilityConflict.None) continue;
            if ((await conflicts.FindConflictAsync(roomId, request.ProfessionalId, request.StartAt, request.EndAt, null, ct)).Any) continue;
            var reservation = Reservation.CreateApproved(roomId, request.ProfessionalId, request.StartAt, request.EndAt, principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? customer.ApplicationUserId!, time.GetUtcNow(), customer.Id);
            db.Reservations.Add(reservation);
            db.AuditEntries.Add(new GestaoPredio.Domain.Auditing.AuditEntry { Id = Guid.NewGuid(), Action = "RESERVATION_CREATED", Result = "SUCCEEDED", TargetEntityType = "RESERVATION", TargetEntityId = reservation.Id, TargetUserId = customer.ApplicationUserId, OccurredAt = time.GetUtcNow(), CorrelationId = context.TraceIdentifier });
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return Results.Created($"/api/customer/reservations/{reservation.Id}", reservation.ToResponse((await db.Rooms.FindAsync([roomId], ct))!.Name, professional.Name));
        }
        await transaction.RollbackAsync(ct);
        return Results.Json(new ApiError("RESERVATION_RESOURCE_CONFLICT", "O horário não está disponível."), statusCode: 409);
    }

    private static async Task<IResult> CancelReservation(Guid id, CustomerReservationConcurrencyRequest request, ClaimsPrincipal principal, ApplicationDbContext db, TimeProvider time, CancellationToken ct)
    {
        var customer = await GetCustomer(principal, db, ct); if (customer is null) return Results.NotFound();
        var reservation = await db.Reservations.SingleOrDefaultAsync(x => x.Id == id && x.CustomerId == customer.Id, ct);
        if (reservation is null) return Results.NotFound();
        if (!ConcurrencyToken.TryDecode(request.ConcurrencyToken, out var version)) return InvalidToken();
        if (reservation.Version != version) return Modified();
        db.Entry(reservation).Property(x => x.Version).OriginalValue = version;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var now = time.GetUtcNow();
            reservation.Cancel(customer.ApplicationUserId!, now);
            await ReservationCheckInTokenRevocation.RevokeAsync(db, id, now, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.NoContent();
        }
        catch (DbUpdateConcurrencyException) { await transaction.RollbackAsync(ct); return Modified(); }
        catch (InvalidOperationException) { return Results.Json(new ApiError("INVALID_RESERVATION_STATE", "A reserva não pode ser cancelada."), statusCode: 409); }
    }

    private static async Task<IResult> IssueToken(Guid id, ClaimsPrincipal principal, HttpContext context,
        ApplicationDbContext db, TimeProvider time, CancellationToken ct)
    {
        var customer = await GetCustomer(principal, db, ct); if (customer is null) return Results.NotFound();
        var reservation = await db.Reservations.SingleOrDefaultAsync(x => x.Id == id && x.CustomerId == customer.Id, ct);
        if (reservation is null) return Results.NotFound();
        var now = time.GetUtcNow();
        if (reservation.Status != ReservationStatus.Approved || reservation.EndAt <= now || now < reservation.StartAt.Subtract(TimeSpan.FromHours(1)))
            return Results.BadRequest(new ApiError("CHECK_IN_NOT_ELIGIBLE", "O check-in não está disponível para esta reserva."));
        var raw = RandomNumberGenerator.GetBytes(32);
        var hash = SHA256.HashData(raw);
        var token = await db.CheckInTokens.SingleOrDefaultAsync(x => x.ReservationId == id, ct);
        if (token is null) db.CheckInTokens.Add(token = CheckInToken.Create(id, hash, now, reservation.EndAt));
        else token.Rotate(hash, now, reservation.EndAt);
        db.AuditEntries.Add(new GestaoPredio.Domain.Auditing.AuditEntry { Id = Guid.NewGuid(), Action = "CHECK_IN_TOKEN_ISSUED", Result = "SUCCEEDED", TargetEntityType = "RESERVATION", TargetEntityId = id, TargetUserId = customer.ApplicationUserId, OccurredAt = now, CorrelationId = context.TraceIdentifier });
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { token = WebEncoders.Base64UrlEncode(raw), expiresAt = reservation.EndAt });
    }

    private static async Task<IResult> RescheduleReservation(Guid id, CustomerReservationRescheduleRequest request, ClaimsPrincipal principal,
        ApplicationDbContext db, ILeaseResourceLock resourceLock, IReservationConflictDetector conflicts,
        IRoomAvailabilityService availability, TimeProvider time, CancellationToken ct)
    {
        var customer = await GetCustomer(principal, db, ct); if (customer is null) return Results.NotFound();
        var original = await db.Reservations.SingleOrDefaultAsync(x => x.Id == id && x.CustomerId == customer.Id, ct);
        if (original is null) return Results.NotFound();
        if (!ConcurrencyToken.TryDecode(request.ConcurrencyToken, out var version)) return InvalidToken();
        if (original.Version != version) return Modified();
        if (request.ProfessionalId != original.ProfessionalId || request.EndAt <= request.StartAt) return Results.BadRequest(new ApiError("INVALID_RESERVATION", "Os dados da reserva são inválidos."));
        var now = time.GetUtcNow();
        var roomIds = await db.Rooms.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Id).Select(x => x.Id).ToListAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest([], roomIds, [original.ProfessionalId]), ct);
        var roomId = original.RoomId;
        if (await availability.CheckScheduleAndBlocksAsync(roomId, request.StartAt, request.EndAt, null, true, ct) != RoomAvailabilityConflict.None ||
            (await conflicts.FindConflictAsync(roomId, original.ProfessionalId, request.StartAt, request.EndAt, id, ct)).Any)
            return Results.Json(new ApiError("RESERVATION_RESOURCE_CONFLICT", "O horário não está disponível."), statusCode: 409);
        try
        {
            db.Entry(original).Property(x => x.Version).OriginalValue = version;
            var replacement = Reservation.CreateApprovedReschedule(original, request.StartAt, request.EndAt, customer.ApplicationUserId!, now);
            original.Cancel(customer.ApplicationUserId!, now);
            await ReservationCheckInTokenRevocation.RevokeAsync(db, id, now, ct);
            db.Reservations.Add(replacement); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            var roomName = await db.Rooms.Where(x => x.Id == roomId).Select(x => x.Name).SingleAsync(ct);
            var professionalName = await db.Professionals.Where(x => x.Id == original.ProfessionalId).Select(x => x.Name).SingleAsync(ct);
            return Results.Ok(replacement.ToResponse(roomName, professionalName));
        }
        catch (DbUpdateConcurrencyException) { await transaction.RollbackAsync(ct); return Modified(); }
        catch (InvalidOperationException) { return Results.Json(new ApiError("INVALID_RESERVATION_STATE", "A reserva não pode ser reagendada."), statusCode: 409); }
    }

    private static IResult InvalidToken() => Results.BadRequest(new ApiError("INVALID_CONCURRENCY_TOKEN", "O token de concorrência informado é inválido."));
    private static IResult Modified() => Results.Json(new ApiError("RESOURCE_MODIFIED", "O registro foi alterado por outra operação. Recarregue os dados e tente novamente."), statusCode: StatusCodes.Status409Conflict);
}

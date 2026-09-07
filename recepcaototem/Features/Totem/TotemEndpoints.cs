using System.Security.Cryptography;
using GestaoPredio.Application.Availability;
using GestaoPredio.Application.Leases;
using GestaoPredio.Application.Reservations;
using GestaoPredio.Application.Scheduling;
using GestaoPredio.Domain.Customers;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Visits;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using recepcaototem.Features.Common;
using recepcaototem.Features.Customers;

namespace recepcaototem.Features.Totem;

public sealed record TotemCustomerResolveRequest(string Name, string Phone) : IStrictModuleRequest;
public sealed record TotemReservationRequest(string Name, string Phone, Guid ProfessionalId, DateTimeOffset StartAt, DateTimeOffset EndAt) : IStrictModuleRequest;
public sealed record TotemCheckInRequest(string Token) : IStrictModuleRequest;
public sealed record TotemProfessionalResponse(Guid Id, string Name, string Profession);
public sealed record TotemCheckInPreview(string Professional, string Room, DateTimeOffset StartAt, DateTimeOffset EndAt, bool Eligible);

public static class TotemEndpoints
{
    public static IEndpointRouteBuilder MapTotemEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/totem/professionals", Professionals).AllowAnonymous();
        endpoints.MapGet("/api/totem/availability", Availability).AllowAnonymous();
        endpoints.MapPost("/api/totem/customers/resolve", ResolveCustomer).AllowAnonymous();
        endpoints.MapPost("/api/totem/reservations", CreateReservation).AllowAnonymous();
        endpoints.MapPost("/api/totem/check-in/resolve", ResolveCheckIn).AllowAnonymous();
        endpoints.MapPost("/api/totem/check-in/confirm", ConfirmCheckIn).AllowAnonymous();
        return endpoints;
    }

    private static async Task<IResult> Professionals(ApplicationDbContext db, CancellationToken ct) =>
        Results.Ok(await db.Professionals.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.NormalizedName)
            .Select(x => new TotemProfessionalResponse(x.Id, x.Name, x.Profession)).ToArrayAsync(ct));

    private static async Task<IResult> Availability(Guid professionalId, DateOnly date, int durationMinutes,
        ApplicationDbContext db, IRoomAvailabilityService availability, IReservationConflictDetector conflicts,
        TimeZoneInfo timeZone, CancellationToken ct)
    {
        var request = new CustomerAvailabilityRequest(professionalId, date, durationMinutes);
        return await CustomerSchedulingEndpointsForTotem.Availability(request, db, availability, conflicts, timeZone, ct);
    }

    private static async Task<IResult> ResolveCustomer(TotemCustomerResolveRequest request, ApplicationDbContext db, CancellationToken ct)
    {
        if (!GestaoPredio.Domain.Professionals.WhatsAppNormalizer.TryNormalize(request.Phone, out var phone)) return Invalid();
        var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.NormalizedPhone == phone && x.IsActive, ct);
        if (customer is null) return Results.Ok(new { exists = false });
        return Results.Ok(new { exists = true, maskedName = MaskName(customer.Name) });
    }

    private static async Task<IResult> CreateReservation(TotemReservationRequest request, ApplicationDbContext db,
        ILeaseResourceLock resourceLock, IReservationConflictDetector conflicts, IRoomAvailabilityService availability,
        TimeProvider time, CancellationToken ct)
    {
        if (!WhatsApp(request.Phone, out var phone) || request.ProfessionalId == Guid.Empty || request.EndAt <= request.StartAt) return Invalid();
        var professional = await db.Professionals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ProfessionalId && x.IsActive, ct);
        if (professional is null) return Results.NotFound();
        var customer = await db.Customers.SingleOrDefaultAsync(x => x.NormalizedPhone == phone, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (customer is null)
        {
            try { customer = Customer.Create(request.Name, phone, time.GetUtcNow()); db.Customers.Add(customer); await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { await transaction.RollbackAsync(ct); return Results.Json(new ApiError("CUSTOMER_ALREADY_EXISTS", "Não foi possível concluir o agendamento."), statusCode: 409); }
        }
        if (!customer.IsActive) return Invalid();
        var rooms = await db.Rooms.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Id).Select(x => x.Id).ToListAsync(ct);
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest([], rooms, [request.ProfessionalId]), ct);
        foreach (var roomId in rooms)
        {
            if (await availability.CheckScheduleAndBlocksAsync(roomId, request.StartAt, request.EndAt, null, true, ct) != RoomAvailabilityConflict.None) continue;
            if ((await conflicts.FindConflictAsync(roomId, request.ProfessionalId, request.StartAt, request.EndAt, null, ct)).Any) continue;
            var reservation = Reservation.CreateApproved(roomId, request.ProfessionalId, request.StartAt, request.EndAt, "TOTEM", time.GetUtcNow(), customer.Id);
            db.Reservations.Add(reservation); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return Results.Ok(new { reservationId = reservation.Id, startAt = reservation.StartAt, endAt = reservation.EndAt });
        }
        await transaction.RollbackAsync(ct);
        return Results.Json(new ApiError("RESERVATION_RESOURCE_CONFLICT", "O horário não está disponível."), statusCode: 409);
    }

    private static async Task<IResult> ResolveCheckIn(TotemCheckInRequest request, ApplicationDbContext db, TimeProvider time, CancellationToken ct)
    {
        var result = await FindCheckIn(request.Token, db, time, ct);
        return result is null ? InvalidCheckIn() : Results.Ok(result.Value.Preview);
    }

    private static async Task<IResult> ConfirmCheckIn(TotemCheckInRequest request, ApplicationDbContext db, TimeProvider time, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var result = await FindCheckIn(request.Token, db, time, ct);
        if (result is null) return InvalidCheckIn();
        var (token, reservation, customer, preview) = result.Value;
        var existing = await db.Visits.SingleOrDefaultAsync(x => x.ReservationId == reservation.Id && x.IsOpen, ct);
        if (existing is not null) return Results.Ok(new { visitId = existing.Id, status = existing.Status.ToString().ToUpperInvariant() });
        var visit = Visit.Arrive(reservation.ProfessionalId, reservation.RoomId, reservation.Id, customer.Name, "TOTEM", time.GetUtcNow(), customer.Id);
        db.Visits.Add(visit); token.MarkUsed(time.GetUtcNow());
        db.AuditEntries.Add(new GestaoPredio.Domain.Auditing.AuditEntry { Id = Guid.NewGuid(), Action = "VISIT_CHECKED_IN", Result = "SUCCEEDED", TargetEntityType = "VISIT", TargetEntityId = visit.Id, OccurredAt = time.GetUtcNow(), CorrelationId = Guid.NewGuid().ToString("N") });
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return Results.Ok(new { visitId = visit.Id, status = "WAITING" });
    }

    private static async Task<(CheckInToken Token, Reservation Reservation, Customer Customer, TotemCheckInPreview Preview)?> FindCheckIn(string raw, ApplicationDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        byte[] bytes; try { bytes = WebEncoders.Base64UrlDecode(raw); } catch (FormatException) { return null; }
        if (bytes.Length != 32) return null;
        var hash = SHA256.HashData(bytes);
        var row = await (from token in db.CheckInTokens
                         join reservation in db.Reservations on token.ReservationId equals reservation.Id
                         join customer in db.Customers on reservation.CustomerId equals customer.Id
                         join professional in db.Professionals on reservation.ProfessionalId equals professional.Id
                         join room in db.Rooms on reservation.RoomId equals room.Id
                         where token.TokenHash == hash
                         select new { token, reservation, customer, Name = professional.Name, RoomName = room.Name }).SingleOrDefaultAsync(ct);
        if (row is null) return null;
        var now = time.GetUtcNow();
        if (row.token.RevokedAt is not null || row.token.ExpiresAt <= now || row.reservation.Status != ReservationStatus.Approved || !row.customer.IsActive || now < row.reservation.StartAt.Subtract(TimeSpan.FromHours(1)) || now >= row.reservation.EndAt) return null;
        return (row.token, row.reservation, row.customer, new TotemCheckInPreview(row.Name, row.RoomName, row.reservation.StartAt, row.reservation.EndAt, true));
    }

    private static string MaskName(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 1 ? parts[0] : $"{parts[0]} {parts[^1][0]}.";
    }
    private static bool WhatsApp(string input, out string phone) => GestaoPredio.Domain.Professionals.WhatsAppNormalizer.TryNormalize(input, out phone);
    private static IResult Invalid() => Results.BadRequest(new ApiError("INVALID_TOTEM_REQUEST", "Não foi possível concluir a operação."));
    private static IResult InvalidCheckIn() => Results.BadRequest(new ApiError("INVALID_CHECK_IN", "Não foi possível validar o check-in."));
}

internal static class CustomerSchedulingEndpointsForTotem
{
    internal static async Task<IResult> Availability(CustomerAvailabilityRequest request, ApplicationDbContext db, IRoomAvailabilityService availability, IReservationConflictDetector conflicts, TimeZoneInfo timeZone, CancellationToken ct)
    {
        if (request.ProfessionalId == Guid.Empty || request.DurationMinutes is < 15 or > 480 || request.DurationMinutes % 15 != 0) return Results.BadRequest(new ApiError("INVALID_AVAILABILITY", "Os dados de disponibilidade são inválidos."));
        if (!await db.Professionals.AnyAsync(x => x.Id == request.ProfessionalId && x.IsActive, ct)) return Results.NotFound();
        var intervals = await db.OperatingHourIntervals.AsNoTracking().ToListAsync(ct); var slots = new List<AvailabilitySlotResponse>();
        var localStart = request.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified); var evaluator = new OperatingHoursEvaluator(timeZone);
        var rooms = await db.Rooms.AsNoTracking().Where(x => x.IsActive).Select(x => x.Id).ToListAsync(ct);
        for (var minute = 0; minute < 1440; minute += 15)
        {
            var startLocal = localStart.AddMinutes(minute); var endLocal = startLocal.AddMinutes(request.DurationMinutes); if (endLocal.Date != localStart.Date) continue;
            var start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(startLocal, timeZone)); var end = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(endLocal, timeZone));
            if (!evaluator.Contains(intervals, start, end)) continue;
            foreach (var room in rooms)
            {
                if (await availability.CheckScheduleAndBlocksAsync(room, start, end, null, true, ct) == RoomAvailabilityConflict.None && !(await conflicts.FindConflictAsync(room, request.ProfessionalId, start, end, null, ct)).Any) { slots.Add(new AvailabilitySlotResponse(start, end)); break; }
            }
        }
        return Results.Ok(slots.ToArray());
    }
}

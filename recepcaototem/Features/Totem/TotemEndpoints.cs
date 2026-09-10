using System.Security.Claims;
using System.Security.Cryptography;
using GestaoPredio.Application.Availability;
using GestaoPredio.Application.Leases;
using GestaoPredio.Application.Notifications;
using GestaoPredio.Application.Reservations;
using GestaoPredio.Application.Scheduling;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Customers;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Visits;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using recepcaototem.Features.Common;
using recepcaototem.Features.Customers;
using recepcaototem.Features.Professionals;
using recepcaototem.Features.Availability;

namespace recepcaototem.Features.Totem;

public sealed record TotemCustomerResolveRequest(string Name, string Phone) : IStrictModuleRequest;
public sealed record TotemReservationRequest(string Name, string Phone, Guid ProfessionalId, DateTimeOffset StartAt, DateTimeOffset EndAt) : IStrictModuleRequest;
public sealed record TotemCheckInRequest(string Token) : IStrictModuleRequest;
public sealed record TotemPresenceRequest(string Token) : IStrictModuleRequest;
public sealed record TotemProfessionalResponse(Guid Id, string Name, string Profession, string? Description);
public sealed record TotemProfessionalCard(Guid Id, string Name, string Profession, string? PhotoUrl, string Status);
public sealed record TotemCheckInPreview(string Professional, string Room, DateTimeOffset StartAt, DateTimeOffset EndAt, bool Eligible);

public static class TotemEndpoints
{
    public static IEndpointRouteBuilder MapTotemEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/totem/professionals", Professionals).AllowAnonymous();
        endpoints.MapGet("/api/totem/professionals/{id:guid}/photo", ProfessionalPhoto).AllowAnonymous();
        endpoints.MapGet("/api/totem/availability", Availability).AllowAnonymous();
        endpoints.MapPost("/api/totem/customers/resolve", ResolveCustomer).AllowAnonymous();
        endpoints.MapPost("/api/totem/reservations", CreateReservation).AllowAnonymous();
        endpoints.MapPost("/api/totem/check-in/resolve", ResolveCheckIn).AllowAnonymous();
        endpoints.MapPost("/api/totem/check-in/confirm", ConfirmCheckIn).AllowAnonymous();
        endpoints.MapPost("/api/totem/presence/confirm", ConfirmPresence).AllowAnonymous();
        endpoints.MapGet("/api/totem/immediate", Immediate).AllowAnonymous();
        return endpoints;
    }

    private static async Task<IResult> ConfirmPresence(TotemPresenceRequest request, HttpContext context,
        ProfessionalPresenceRateLimiter limiter, ApplicationDbContext db, ILeaseResourceLock resourceLock,
        TimeZoneInfo timeZone, TimeProvider time, CancellationToken ct)
    {
        var raw = request.Token ?? string.Empty;
        using var lease = await limiter.AcquireAsync(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", raw, ct);
        if (!lease.IsAcquired) return Results.Json(new ApiError("TOO_MANY_REQUESTS", "Tente novamente mais tarde."), statusCode: 429);

        byte[] bytes;
        try { bytes = WebEncoders.Base64UrlDecode(raw); } catch (FormatException) { return InvalidPresence(); }
        if (bytes.Length != 32) return InvalidPresence();
        var hash = SHA256.HashData(bytes);
        var now = time.GetUtcNow();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var token = await db.ProfessionalPresenceTokens.SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (token is null || token.RevokedAt is not null || token.UsedAt is not null || token.ExpiresAt <= now)
            return InvalidPresence();

        var professionalId = token.ProfessionalId;
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest([], [], [professionalId]), ct);

        var operatingHours = await db.OperatingHourIntervals.AsNoTracking().ToListAsync(ct);
        var openPresence = await db.ProfessionalPresences
            .Where(x => x.ProfessionalId == professionalId && x.EndedAt == null)
            .OrderByDescending(x => x.StartedAt).FirstOrDefaultAsync(ct);

        if (openPresence is not null && !PresenceEvaluator.IsEffective(openPresence, operatingHours, now, timeZone))
        {
            var civilDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(openPresence.StartedAt, timeZone).DateTime);
            openPresence.MaterialiseOperatingHoursEnd(
                PresenceEvaluator.OperatingHoursEndInstant(civilDay, operatingHours, timeZone) ?? now);
            openPresence = null;
        }

        if (openPresence is not null)
        {
            token.MarkUsed(now);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.Ok(new { status = "PRESENT" });
        }

        var presence = ProfessionalPresence.StartByQr(professionalId, now);
        db.ProfessionalPresences.Add(presence);
        token.MarkUsed(now);
        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            Action = AuditActions.ProfessionalPresenceStarted,
            Result = "SUCCEEDED",
            TargetEntityType = AuditTargetTypes.ProfessionalPresence,
            TargetEntityId = presence.Id,
            IpAddress = context.Connection.RemoteIpAddress?.ToString(),
            OccurredAt = now,
            CorrelationId = context.TraceIdentifier
        });
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
        }
        return Results.Ok(new { status = "PRESENT" });
    }

    private static async Task<IResult> Immediate(int? durationMinutes, HttpContext context,
        CustomerPublicRateLimiter limiter, ApplicationDbContext db, IAppointmentAvailabilityService availability,
        TimeZoneInfo timeZone, TimeProvider time, CancellationToken ct)
    {
        using var lease = await limiter.AcquireAsync(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", "totem-immediate", ct);
        if (!lease.IsAcquired) return Results.Json(new ApiError("TOO_MANY_REQUESTS", "Tente novamente mais tarde."), statusCode: 429);
        var duration = durationMinutes ?? 0;
        if (duration is < 15 or > 480 || duration % 15 != 0) return Invalid();

        var now = time.GetUtcNow();
        var operatingHours = await db.OperatingHourIntervals.AsNoTracking().ToListAsync(ct);
        var professionals = await db.Professionals.AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.NormalizedName)
            .Select(x => new TotemProfessionalResponse(x.Id, x.Name, x.Profession, x.Description))
            .ToArrayAsync(ct);
        if (professionals.Length == 0) return Results.Ok(Array.Empty<TotemProfessionalResponse>());

        var openPresences = await db.ProfessionalPresences.AsNoTracking()
            .Where(x => x.EndedAt == null).ToListAsync(ct);
        var presenceByProfessional = openPresences
            .GroupBy(x => x.ProfessionalId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(x => x.StartedAt).First());

        var result = new List<TotemProfessionalResponse>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        foreach (var professional in professionals)
        {
            presenceByProfessional.TryGetValue(professional.Id, out var presence);
            if (!PresenceEvaluator.IsEffective(presence, operatingHours, now, timeZone)) continue;
            var room = await availability.FindAvailableRoomAsync(
                professional.Id, now, now.AddMinutes(duration), null, null, ct);
            if (room.IsAvailable) result.Add(professional);
        }
        await transaction.RollbackAsync(ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> Professionals(
        HttpContext context, CustomerPublicRateLimiter limiter, ApplicationDbContext db,
        TimeZoneInfo timeZone, TimeProvider time, CancellationToken ct)
    {
        using var lease = await limiter.AcquireAsync(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown", "totem-professionals", ct);
        if (!lease.IsAcquired)
            return Results.Json(new ApiError("TOO_MANY_REQUESTS", "Tente novamente mais tarde."), statusCode: 429);

        var professionals = await db.Professionals.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.NormalizedName)
            .Select(x => new { x.Id, x.Name, x.Profession, x.PhotoFileId })
            .ToListAsync(ct);
        if (professionals.Count == 0) return Results.Ok(Array.Empty<TotemProfessionalCard>());

        var ids = professionals.Select(x => x.Id).ToList();
        var inService = (await db.Visits.AsNoTracking()
            .Where(v => ids.Contains(v.ProfessionalId) && v.Status == VisitStatus.InService)
            .Select(v => v.ProfessionalId).Distinct().ToListAsync(ct)).ToHashSet();
        var operatingHours = await db.OperatingHourIntervals.AsNoTracking().ToListAsync(ct);
        var presenceByProfessional = (await db.ProfessionalPresences.AsNoTracking()
                .Where(x => ids.Contains(x.ProfessionalId) && x.EndedAt == null).ToListAsync(ct))
            .GroupBy(x => x.ProfessionalId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.StartedAt).First());

        var now = time.GetUtcNow();
        var cards = professionals.Select(p =>
        {
            presenceByProfessional.TryGetValue(p.Id, out var presence);
            var status = TotemProfessionalStatus.Resolve(
                inService.Contains(p.Id),
                PresenceEvaluator.IsEffective(presence, operatingHours, now, timeZone));
            return new TotemProfessionalCard(
                p.Id, p.Name, p.Profession,
                p.PhotoFileId is null ? null : $"/api/totem/professionals/{p.Id}/photo",
                status);
        }).ToArray();

        return Results.Ok(cards);
    }

    private static async Task<IResult> ProfessionalPhoto(
        Guid id, HttpContext context, ApplicationDbContext db,
        GestaoPredio.Application.Abstractions.IPrivateFileStorage storage,
        ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var professional = await db.Professionals.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.IsActive, ct);
        if (professional is null) return Results.NotFound();
        return await recepcaototem.Features.Professionals.ProfessionalPhotoStreaming.StreamAsync(
            professional, db, storage, loggerFactory, context, "public, max-age=300",
            notFoundWhenMetadataUnusable: true, ct);
    }

    private static async Task<IResult> Availability(Guid professionalId, DateOnly date, int durationMinutes,
        ApplicationDbContext db, IAppointmentAvailabilityService availability, CancellationToken ct)
    {
        var request = new CustomerAvailabilityRequest(professionalId, date, durationMinutes);
        return await CustomerSchedulingEndpoints.Availability(request, db, availability, ct);
    }

    private static async Task<IResult> ResolveCustomer(TotemCustomerResolveRequest request, HttpContext context, CustomerPublicRateLimiter limiter, ApplicationDbContext db, CancellationToken ct)
    {
        using var lease = await limiter.AcquireAsync(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", request.Phone ?? string.Empty, ct);
        if (!lease.IsAcquired) return Results.Json(new ApiError("TOO_MANY_REQUESTS", "Tente novamente mais tarde."), statusCode: 429);
        if (!GestaoPredio.Domain.Professionals.WhatsAppNormalizer.TryNormalize(request.Phone, out var phone)) return Invalid();
        var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.NormalizedPhone == phone && x.IsActive, ct);
        if (customer is null) return Results.Ok(new { exists = false });
        return Results.Ok(new { exists = true, maskedName = MaskName(customer.Name) });
    }

    private static async Task<IResult> CreateReservation(TotemReservationRequest request, HttpContext context, CustomerPublicRateLimiter limiter, ApplicationDbContext db,
        ILeaseResourceLock resourceLock, IAppointmentAvailabilityService availability,
        TimeProvider time, CancellationToken ct)
    {
        using var lease = await limiter.AcquireAsync(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", request.Phone ?? string.Empty, ct);
        if (!lease.IsAcquired) return Results.Json(new ApiError("TOO_MANY_REQUESTS", "Tente novamente mais tarde."), statusCode: 429);
        return await CreateReservationCore(request, context, db, resourceLock, availability, time, ct, "TOTEM");
    }

    internal static Task<IResult> CreateAssistedReservation(TotemReservationRequest request, HttpContext context,
        ApplicationDbContext db, ILeaseResourceLock resourceLock,
        IAppointmentAvailabilityService availability, TimeProvider time, CancellationToken ct) =>
        CreateReservationCore(request, context, db, resourceLock, availability, time, ct,
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "RECEPTION");

    private static async Task<IResult> CreateReservationCore(TotemReservationRequest request, HttpContext context, ApplicationDbContext db,
        ILeaseResourceLock resourceLock, IAppointmentAvailabilityService availability,
        TimeProvider time, CancellationToken ct, string actor)
    {
        if (!WhatsApp(request.Phone ?? string.Empty, out var phone) || request.ProfessionalId == Guid.Empty || request.EndAt <= request.StartAt) return Invalid();
        var professional = await db.Professionals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ProfessionalId && x.IsActive, ct);
        if (professional is null) return Results.NotFound();
        var customer = await db.Customers.SingleOrDefaultAsync(x => x.NormalizedPhone == phone, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (customer is null)
        {
            try
            {
                customer = Customer.Create(request.Name, phone, time.GetUtcNow());
                db.Customers.Add(customer);
                db.AuditEntries.Add(new GestaoPredio.Domain.Auditing.AuditEntry { Id = Guid.NewGuid(), Action = "CUSTOMER_CREATED", Result = "SUCCEEDED", TargetEntityType = "CUSTOMER", TargetEntityId = customer.Id, OccurredAt = time.GetUtcNow(), CorrelationId = Guid.NewGuid().ToString("N") });
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException) { await transaction.RollbackAsync(ct); return Results.Json(new ApiError("CUSTOMER_ALREADY_EXISTS", "Não foi possível concluir o agendamento."), statusCode: 409); }
        }
        if (!customer.IsActive) return Invalid();
        var rooms = await db.Rooms.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Id).Select(x => x.Id).ToListAsync(ct);
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest([], rooms, [request.ProfessionalId]), ct);
        var available = await availability.FindAvailableRoomAsync(
            request.ProfessionalId, request.StartAt, request.EndAt, null, null, ct);
        if (!available.IsAvailable) return AppointmentAvailabilityResults.Conflict(available.Failure);
        var reservation = Reservation.CreateApproved(available.RoomId!.Value, request.ProfessionalId,
            request.StartAt, request.EndAt, actor, time.GetUtcNow(), customer.Id);
        db.Reservations.Add(reservation);
        db.AuditEntries.Add(new GestaoPredio.Domain.Auditing.AuditEntry { Id = Guid.NewGuid(), Action = "RESERVATION_CREATED", Result = "SUCCEEDED", TargetEntityType = "RESERVATION", TargetEntityId = reservation.Id, OccurredAt = time.GetUtcNow(), CorrelationId = Guid.NewGuid().ToString("N") });
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return Results.Ok(new { reservationId = reservation.Id, startAt = reservation.StartAt, endAt = reservation.EndAt });
    }

    private static async Task<IResult> ResolveCheckIn(TotemCheckInRequest request, HttpContext context, CustomerPublicRateLimiter limiter, ApplicationDbContext db, TimeProvider time, CancellationToken ct)
    {
        using var lease = await limiter.AcquireAsync(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", request.Token ?? string.Empty, ct);
        if (!lease.IsAcquired) return Results.Json(new ApiError("TOO_MANY_REQUESTS", "Tente novamente mais tarde."), statusCode: 429);
        var result = await FindCheckIn(request.Token ?? string.Empty, db, time, ct);
        return result is null ? InvalidCheckIn() : Results.Ok(result.Value.Preview);
    }

    private static async Task<IResult> ConfirmCheckIn(TotemCheckInRequest request, HttpContext context, CustomerPublicRateLimiter limiter, ApplicationDbContext db, TimeProvider time, INotificationService notifications, CancellationToken ct)
    {
        using var lease = await limiter.AcquireAsync(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", request.Token ?? string.Empty, ct);
        if (!lease.IsAcquired) return Results.Json(new ApiError("TOO_MANY_REQUESTS", "Tente novamente mais tarde."), statusCode: 429);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var result = await FindCheckIn(request.Token ?? string.Empty, db, time, ct);
        if (result is null) return InvalidCheckIn();
        var (token, reservation, customer, preview) = result.Value;
        var existing = await db.Visits.SingleOrDefaultAsync(
            x => x.ReservationId == reservation.Id &&
                 (x.Status == VisitStatus.Waiting || x.Status == VisitStatus.InService), ct);
        if (existing is not null) return Results.Ok(new { visitId = existing.Id, status = existing.Status.ToString().ToUpperInvariant() });
        if (token.UsedAt is not null) return InvalidCheckIn();
        var visit = Visit.Arrive(reservation.ProfessionalId, reservation.RoomId, reservation.Id, customer.Name, "TOTEM", time.GetUtcNow(), customer.Id);
        db.Visits.Add(visit); token.MarkUsed(time.GetUtcNow());
        db.AuditEntries.Add(new GestaoPredio.Domain.Auditing.AuditEntry { Id = Guid.NewGuid(), Action = "VISIT_CHECKED_IN", Result = "SUCCEEDED", TargetEntityType = "VISIT", TargetEntityId = visit.Id, OccurredAt = time.GetUtcNow(), CorrelationId = Guid.NewGuid().ToString("N") });
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        await notifications.NotifyProfessionalAsync(new ProfessionalNotificationEvent(
            visit.ProfessionalId, NotificationEventTypes.ProfessionalVisitWaiting,
            visit.VisitorName, visit.ArrivedAt, visit.ReservationId), CancellationToken.None);
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
    private static IResult InvalidPresence() => Results.BadRequest(new ApiError("INVALID_PRESENCE", "Não foi possível validar a presença."));
}

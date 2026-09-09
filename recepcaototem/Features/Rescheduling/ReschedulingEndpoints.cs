using System.Security.Cryptography;
using GestaoPredio.Application.Availability;
using GestaoPredio.Application.Leases;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using recepcaototem.Features.Availability;
using recepcaototem.Features.Common;
using recepcaototem.Features.Customers;

namespace recepcaototem.Features.Rescheduling;

public sealed record RescheduleResolveRequest(string Token) : IStrictModuleRequest;
public sealed record RescheduleConfirmRequest(string Token, DateTimeOffset StartAt, DateTimeOffset EndAt) : IStrictModuleRequest;

public static class ReschedulingEndpoints
{
    public static IEndpointRouteBuilder MapReschedulingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/reschedule/resolve", Resolve).AllowAnonymous();
        endpoints.MapGet("/api/reschedule/slots", Slots).AllowAnonymous();
        endpoints.MapPost("/api/reschedule/confirm", Confirm).AllowAnonymous();
        return endpoints;
    }

    private static async Task<IResult> Resolve(RescheduleResolveRequest request, HttpContext context,
        RescheduleTokenRateLimiter limiter, ApplicationDbContext db, TimeProvider time, CancellationToken ct)
    {
        var raw = request.Token ?? string.Empty;
        using var lease = await limiter.AcquireAsync(Ip(context), raw, ct);
        if (!lease.IsAcquired) return TooMany();

        var resolved = await LoadAsync(raw, db, time.GetUtcNow(), ct);
        if (resolved is null) return Invalid();
        var value = resolved.Value;
        return Results.Ok(new
        {
            professionalId = value.Original.ProfessionalId,
            professionalName = value.ProfessionalName,
            originalStartAt = value.Original.StartAt,
            originalEndAt = value.Original.EndAt,
            durationMinutes = DurationMinutes(value.Original),
            expiresAt = value.Token.ExpiresAt
        });
    }

    private static async Task<IResult> Slots(string? token, DateOnly? date, HttpContext context,
        RescheduleTokenRateLimiter limiter, ApplicationDbContext db, IAppointmentAvailabilityService availability,
        TimeProvider time, CancellationToken ct)
    {
        var raw = token ?? string.Empty;
        using var lease = await limiter.AcquireAsync(Ip(context), raw, ct);
        if (!lease.IsAcquired) return TooMany();
        if (date is null) return Invalid();

        var resolved = await LoadAsync(raw, db, time.GetUtcNow(), ct);
        if (resolved is null) return Invalid();
        var slots = await availability.FindSlotsAsync(
            resolved.Value.Original.ProfessionalId, date.Value, DurationMinutes(resolved.Value.Original), ct);
        return Results.Ok(slots.Select(slot => new AvailabilitySlotResponse(slot.StartAt, slot.EndAt)).ToArray());
    }

    private static async Task<IResult> Confirm(RescheduleConfirmRequest request, HttpContext context,
        RescheduleTokenRateLimiter limiter, ApplicationDbContext db, ILeaseResourceLock resourceLock,
        IAppointmentAvailabilityService availability, TimeZoneInfo timeZone, TimeProvider time, CancellationToken ct)
    {
        var raw = request.Token ?? string.Empty;
        using var lease = await limiter.AcquireAsync(Ip(context), raw, ct);
        if (!lease.IsAcquired) return TooMany();

        var now = time.GetUtcNow();
        if (request.EndAt <= request.StartAt) return Invalid();
        var localStart = TimeZoneInfo.ConvertTime(request.StartAt, timeZone);
        var localEnd = TimeZoneInfo.ConvertTime(request.EndAt, timeZone);
        if (localStart.Date != localEnd.Date) return Invalid();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var resolved = await LoadAsync(raw, db, now, ct);
        if (resolved is null) return Invalid();
        var (token, original, professionalName) = resolved.Value;

        var originalDuration = original.EndAt - original.StartAt;
        if (Math.Abs(((request.EndAt - request.StartAt) - originalDuration).TotalMinutes) > 1) return Invalid();

        var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == original.CustomerId, ct);
        if (customer is null || !customer.IsActive) return Invalid();

        var activeRoomIds = await db.Rooms.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Id)
            .Select(x => x.Id).ToListAsync(ct);
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest([], activeRoomIds, [original.ProfessionalId]), ct);

        var available = await availability.FindAvailableRoomAsync(
            original.ProfessionalId, request.StartAt, request.EndAt, original.RoomId, null, ct);
        if (!available.IsAvailable)
            available = await availability.FindAvailableRoomAsync(
                original.ProfessionalId, request.StartAt, request.EndAt, null, null, ct);
        if (!available.IsAvailable)
        {
            await transaction.RollbackAsync(ct);
            return AppointmentAvailabilityResults.Conflict(available.Failure);
        }

        var replacement = Reservation.CreateApprovedReplacementForIncident(
            original, available.RoomId!.Value, request.StartAt, request.EndAt, "RESCHEDULE_LINK", now);
        db.Reservations.Add(replacement);
        token.MarkUsed(now);
        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            Action = AuditActions.ReservationRescheduled,
            Result = "SUCCEEDED",
            TargetEntityType = AuditTargetTypes.Reservation,
            TargetEntityId = replacement.Id,
            OccurredAt = now,
            CorrelationId = context.TraceIdentifier
        });
        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            Action = AuditActions.RescheduleLinkConsumed,
            Result = "SUCCEEDED",
            TargetEntityType = AuditTargetTypes.RescheduleToken,
            TargetEntityId = token.Id,
            OccurredAt = now,
            CorrelationId = context.TraceIdentifier
        });
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(ct);
            return Invalid();
        }

        var roomName = await db.Rooms.AsNoTracking()
            .Where(x => x.Id == replacement.RoomId).Select(x => x.Name).SingleAsync(ct);
        return Results.Ok(new
        {
            reservationId = replacement.Id,
            startAt = replacement.StartAt,
            endAt = replacement.EndAt,
            professionalName,
            roomName
        });
    }

    private static async Task<Resolved?> LoadAsync(string raw, ApplicationDbContext db, DateTimeOffset now,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        byte[] bytes;
        try { bytes = WebEncoders.Base64UrlDecode(raw); } catch (FormatException) { return null; }
        if (bytes.Length != 32) return null;
        var hash = SHA256.HashData(bytes);

        var row = await (from token in db.RescheduleTokens
                         join reservation in db.Reservations on token.ReservationId equals reservation.Id
                         join professional in db.Professionals on reservation.ProfessionalId equals professional.Id
                         where token.TokenHash == hash
                         select new { token, reservation, ProfessionalName = professional.Name })
            .SingleOrDefaultAsync(ct);
        if (row is null) return null;
        if (row.token.RevokedAt is not null || row.token.UsedAt is not null || row.token.ExpiresAt <= now) return null;
        if (row.reservation.Status != ReservationStatus.Cancelled ||
            row.reservation.CancellationReason != ReservationCancellationReason.ProfessionalUnavailable) return null;
        return new Resolved(row.token, row.reservation, row.ProfessionalName);
    }

    private static int DurationMinutes(Reservation reservation) =>
        (int)Math.Round((reservation.EndAt - reservation.StartAt).TotalMinutes);

    private static string Ip(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    private static IResult TooMany() =>
        Results.Json(new ApiError("TOO_MANY_REQUESTS", "Tente novamente mais tarde."), statusCode: 429);
    private static IResult Invalid() =>
        Results.BadRequest(new ApiError("INVALID_RESCHEDULE_LINK", "O link de reagendamento é inválido ou expirou."));

    private readonly record struct Resolved(
        GestaoPredio.Domain.Customers.RescheduleToken Token, Reservation Original, string ProfessionalName);
}

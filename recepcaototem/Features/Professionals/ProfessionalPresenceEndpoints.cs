using System.Security.Claims;
using System.Security.Cryptography;
using GestaoPredio.Application.Availability;
using GestaoPredio.Domain.Notifications;
using GestaoPredio.Application.Leases;
using GestaoPredio.Application.Scheduling;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Customers;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using recepcaototem.Features.Auth;
using recepcaototem.Features.Common;
using recepcaototem.Features.Reservations;

namespace recepcaototem.Features.Professionals;

public sealed record ProfessionalIncidentRequest(string Type, string? UntilTime, string? Reason) : IStrictModuleRequest;

public static class ProfessionalPresenceEndpoints
{
    public static IEndpointRouteBuilder MapProfessionalPresenceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/professional/presence").RequireAuthorization("Professional");
        group.MapPost("/qr", IssueQr).AddEndpointFilter<AntiforgeryFilter>();
        group.MapGet("", GetOwnStatus);

        endpoints.MapPost("/api/professional/incidents", ReportIncident)
            .RequireAuthorization("Professional")
            .AddEndpointFilter<AntiforgeryFilter>();
        return endpoints;
    }

    private static async Task<IResult> IssueQr(HttpContext context, ApplicationDbContext db,
        IConfiguration configuration, TimeProvider time, CancellationToken ct)
    {
        var professionalId = await ResolveOwnProfessionalId(context, db, ct);
        if (professionalId is null) return NotLinked();

        var now = time.GetUtcNow();
        var ttl = TimeSpan.FromSeconds(Math.Max(1, configuration.GetValue("Presence:QrTokenTtlSeconds", 120)));
        var raw = RandomNumberGenerator.GetBytes(32);
        var hash = SHA256.HashData(raw);
        var token = await db.ProfessionalPresenceTokens
            .SingleOrDefaultAsync(x => x.ProfessionalId == professionalId.Value, ct);
        if (token is null)
            db.ProfessionalPresenceTokens.Add(ProfessionalPresenceToken.Create(professionalId.Value, hash, now, now + ttl));
        else
            token.Rotate(hash, now, now + ttl);

        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            Action = AuditActions.ProfessionalPresenceQrIssued,
            Result = "SUCCEEDED",
            TargetEntityType = AuditTargetTypes.Professional,
            TargetEntityId = professionalId.Value,
            ActorUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier),
            IpAddress = context.Connection.RemoteIpAddress?.ToString(),
            OccurredAt = now,
            CorrelationId = context.TraceIdentifier
        });
        await db.SaveChangesAsync(ct);

        context.Response.Headers.CacheControl = "private, no-store";
        return Results.Ok(new { token = WebEncoders.Base64UrlEncode(raw), expiresAt = now + ttl });
    }

    private static async Task<IResult> GetOwnStatus(HttpContext context, ApplicationDbContext db,
        TimeZoneInfo timeZone, TimeProvider time, CancellationToken ct)
    {
        var professionalId = await ResolveOwnProfessionalId(context, db, ct);
        if (professionalId is null) return NotLinked();

        var now = time.GetUtcNow();
        var openPresence = await db.ProfessionalPresences.AsNoTracking()
            .Where(p => p.ProfessionalId == professionalId.Value && p.EndedAt == null)
            .OrderByDescending(p => p.StartedAt)
            .FirstOrDefaultAsync(ct);
        var operatingHours = await db.OperatingHourIntervals.AsNoTracking().ToListAsync(ct);
        var present = PresenceEvaluator.IsEffective(openPresence, operatingHours, now, timeZone);

        var absentUntil = await ResolveAbsentUntil(db, professionalId.Value, now, timeZone, ct);

        context.Response.Headers.CacheControl = "private, no-store";
        return Results.Ok(new
        {
            status = present ? "PRESENT" : "ABSENT",
            since = present && openPresence is not null ? openPresence.StartedAt : (DateTimeOffset?)null,
            absentUntil
        });
    }

    internal static async Task<DateTimeOffset?> ResolveAbsentUntil(ApplicationDbContext db, Guid professionalId,
        DateTimeOffset now, TimeZoneInfo timeZone, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
        var incidentEnds = await db.ProfessionalAvailabilityExceptions.AsNoTracking()
            .Where(e => e.ProfessionalId == professionalId &&
                e.Origin == ProfessionalAvailabilityExceptionOrigin.Incident &&
                !e.AllDay && e.Date == today && e.EndTime != null)
            .Select(e => e.EndTime!.Value)
            .ToListAsync(ct);

        DateTimeOffset? absentUntil = null;
        foreach (var endTime in incidentEnds)
        {
            var local = DateTime.SpecifyKind(today.ToDateTime(endTime), DateTimeKind.Unspecified);
            var instant = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, timeZone), TimeSpan.Zero);
            if (instant > now && (absentUntil is null || instant > absentUntil))
                absentUntil = instant;
        }
        return absentUntil;
    }

    private static async Task<IResult> ReportIncident(ProfessionalIncidentRequest request, HttpContext context,
        ApplicationDbContext db, ILeaseResourceLock resourceLock,
        IConfiguration configuration, TimeZoneInfo timeZone, TimeProvider time,
        CancellationToken ct)
    {
        var professionalId = await ResolveOwnProfessionalId(context, db, ct);
        if (professionalId is null) return NotLinked();

        var type = (request.Type ?? string.Empty).Trim().ToUpperInvariant();
        if (type is not ("NEXT_APPOINTMENT" or "UNTIL_TIME" or "REST_OF_DAY"))
            return InvalidIncident("O tipo de imprevisto é inválido.");
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (reason is { Length: > ProfessionalAvailabilityException.MaximumReasonLength })
            return InvalidIncident("O motivo excede o limite permitido.");

        var now = time.GetUtcNow();
        var localNow = TimeZoneInfo.ConvertTime(now, timeZone).DateTime;
        var today = DateOnly.FromDateTime(localNow);
        var localNowTime = TimeOnly.FromDateTime(localNow);
        var civilDay = OperationalTimeZone.GetCivilDayInterval(today, timeZone);
        var ttl = TimeSpan.FromHours(Math.Max(1, configuration.GetValue("Rescheduling:LinkTtlHours", 48)));
        var actor = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest([], [], [professionalId.Value]), ct);

        var operatingHours = await db.OperatingHourIntervals.AsNoTracking().ToListAsync(ct);
        var openPresence = await db.ProfessionalPresences
            .Where(x => x.ProfessionalId == professionalId.Value && x.EndedAt == null)
            .OrderByDescending(x => x.StartedAt).FirstOrDefaultAsync(ct);

        var upcoming = db.Reservations.Where(x => x.ProfessionalId == professionalId.Value &&
            x.Status == ReservationStatus.Approved && x.Kind != ReservationKind.Cancellation &&
            x.EndAt > now && x.StartAt < civilDay.EndAt);

        DateTimeOffset windowStart;
        DateTimeOffset windowEnd;
        TimeOnly exceptionStart;
        TimeOnly exceptionEnd;

        if (type == "NEXT_APPOINTMENT")
        {
            var next = await upcoming.OrderBy(x => x.StartAt).FirstOrDefaultAsync(ct);
            if (next is null)
            {
                await transaction.CommitAsync(ct);
                return Results.Ok(new
                {
                    exceptionId = (Guid?)null,
                    affectedReservationIds = Array.Empty<Guid>(),
                    presence = EffectivePresence(openPresence, operatingHours, now, timeZone)
                });
            }
            windowStart = next.StartAt;
            windowEnd = next.EndAt;
            exceptionStart = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(next.StartAt, timeZone).DateTime);
            var localEnd = TimeZoneInfo.ConvertTime(next.EndAt, timeZone).DateTime;
            exceptionEnd = DateOnly.FromDateTime(localEnd) == today
                ? TimeOnly.FromDateTime(localEnd)
                : new TimeOnly(23, 59, 59);
        }
        else if (type == "UNTIL_TIME")
        {
            if (!TimeOnly.TryParse(request.UntilTime, out var until) || until <= localNowTime)
                return InvalidIncident("O horário informado é inválido.");
            windowStart = now;
            windowEnd = ToUtc(today, until, timeZone);
            exceptionStart = localNowTime;
            exceptionEnd = until;
        }
        else
        {
            var operatingEnd = PresenceEvaluator.OperatingHoursEndInstant(today, operatingHours, timeZone);
            windowStart = now;
            windowEnd = operatingEnd is { } end && end < civilDay.EndAt ? end : civilDay.EndAt;
            var localWindowEnd = TimeZoneInfo.ConvertTime(windowEnd, timeZone).DateTime;
            exceptionStart = localNowTime;
            exceptionEnd = DateOnly.FromDateTime(localWindowEnd) == today
                ? TimeOnly.FromDateTime(localWindowEnd)
                : new TimeOnly(23, 59, 59);
        }

        if (exceptionEnd <= exceptionStart)
            return InvalidIncident("O período do imprevisto é inválido.");

        var exception = ProfessionalAvailabilityException.Create(professionalId.Value, today, false,
            exceptionStart, exceptionEnd, reason, now, ProfessionalAvailabilityExceptionOrigin.Incident);
        db.ProfessionalAvailabilityExceptions.Add(exception);

        var affected = await db.Reservations.Where(x => x.ProfessionalId == professionalId.Value &&
                x.Status == ReservationStatus.Approved && x.Kind != ReservationKind.Cancellation &&
                x.EndAt > now && x.StartAt < windowEnd && x.EndAt > windowStart)
            .OrderBy(x => x.StartAt).ToListAsync(ct);
        if (type == "NEXT_APPOINTMENT") affected = affected.Take(1).ToList();

        var cancelledIds = new List<Guid>();
        foreach (var reservation in affected)
        {
            try
            {
                reservation.Cancel("PROFESSIONAL_INCIDENT", now, ReservationCancellationReason.ProfessionalUnavailable);
            }
            catch (InvalidOperationException) { continue; }

            await ReservationCheckInTokenRevocation.RevokeAsync(db, reservation.Id, now, ct);
            // Outbox: the customer's PROFESSIONAL_CANCELLED notice commits with the cancellation. The dispatcher
            // rotates the reschedule token when it sends, so the raw link only ever exists inside that message.
            if (WhatsAppNotification.ReservationCancelled(reservation, now) is { } notice)
                db.WhatsAppNotifications.Add(notice);

            var raw = RandomNumberGenerator.GetBytes(32);
            var hash = SHA256.HashData(raw);
            var tokenRow = await db.RescheduleTokens.SingleOrDefaultAsync(x => x.ReservationId == reservation.Id, ct);
            RescheduleToken token;
            if (tokenRow is null)
            {
                token = RescheduleToken.Create(reservation.Id, hash, now, now + ttl);
                db.RescheduleTokens.Add(token);
            }
            else
            {
                tokenRow.Rotate(hash, now, now + ttl);
                token = tokenRow;
            }

            db.AuditEntries.Add(new AuditEntry
            {
                Id = Guid.NewGuid(),
                Action = AuditActions.ReservationCancelledProfessionalUnavailable,
                Result = "SUCCEEDED",
                TargetEntityType = AuditTargetTypes.Reservation,
                TargetEntityId = reservation.Id,
                ActorUserId = actor,
                OccurredAt = now,
                CorrelationId = context.TraceIdentifier
            });
            db.AuditEntries.Add(new AuditEntry
            {
                Id = Guid.NewGuid(),
                Action = AuditActions.RescheduleLinkIssued,
                Result = "SUCCEEDED",
                TargetEntityType = AuditTargetTypes.RescheduleToken,
                TargetEntityId = token.Id,
                ActorUserId = actor,
                OccurredAt = now,
                CorrelationId = context.TraceIdentifier
            });

            cancelledIds.Add(reservation.Id);
        }

        if (type != "NEXT_APPOINTMENT" && openPresence is not null)
            openPresence.EndForIncident(restOfDay: type == "REST_OF_DAY", now);

        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            Action = type switch
            {
                "NEXT_APPOINTMENT" => AuditActions.ProfessionalIncidentReportedNextAppointment,
                "UNTIL_TIME" => AuditActions.ProfessionalIncidentReportedUntilTime,
                _ => AuditActions.ProfessionalIncidentReportedRestOfDay
            },
            Result = "SUCCEEDED",
            TargetEntityType = AuditTargetTypes.Professional,
            TargetEntityId = professionalId.Value,
            ActorUserId = actor,
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
            return Results.Json(new ApiError("INCIDENT_CONFLICT", "Não foi possível registrar o imprevisto."),
                statusCode: StatusCodes.Status409Conflict);
        }

        return Results.Ok(new
        {
            exceptionId = (Guid?)exception.Id,
            affectedReservationIds = cancelledIds.ToArray(),
            presence = EffectivePresence(openPresence, operatingHours, now, timeZone)
        });
    }

    private static string EffectivePresence(ProfessionalPresence? presence,
        IReadOnlyCollection<GestaoPredio.Domain.Availability.OperatingHourInterval> operatingHours,
        DateTimeOffset now, TimeZoneInfo timeZone) =>
        presence is not null && presence.EndedAt is null &&
        PresenceEvaluator.IsEffective(presence, operatingHours, now, timeZone) ? "PRESENT" : "ABSENT";

    private static DateTimeOffset ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo timeZone) =>
        new(TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified), timeZone), TimeSpan.Zero);

    private static IResult InvalidIncident(string message) =>
        Results.BadRequest(new ApiError("INVALID_INCIDENT", message));

    private static async Task<Guid?> ResolveOwnProfessionalId(HttpContext context, ApplicationDbContext db,
        CancellationToken ct)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return await db.Professionals.AsNoTracking()
            .Where(p => p.ApplicationUserId == userId && p.IsActive)
            .Select(p => (Guid?)p.Id)
            .SingleOrDefaultAsync(ct);
    }

    private static IResult NotLinked() => Results.NotFound(
        new ApiError("PROFESSIONAL_PROFILE_NOT_LINKED", "O perfil profissional não está vinculado corretamente."));
}

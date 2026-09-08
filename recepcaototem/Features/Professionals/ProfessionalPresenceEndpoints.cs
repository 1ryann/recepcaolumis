using System.Security.Claims;
using System.Security.Cryptography;
using GestaoPredio.Application.Availability;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using recepcaototem.Features.Auth;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Professionals;

public static class ProfessionalPresenceEndpoints
{
    public static IEndpointRouteBuilder MapProfessionalPresenceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/professional/presence").RequireAuthorization("Professional");
        group.MapPost("/qr", IssueQr).AddEndpointFilter<AntiforgeryFilter>();
        group.MapGet("", GetOwnStatus);
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

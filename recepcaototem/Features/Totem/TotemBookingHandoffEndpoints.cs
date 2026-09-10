using System.Security.Cryptography;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Customers;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Totem;

/// <summary>
/// Anonymous bridge between the public Totem and a visitor's phone. The kiosk creates a handoff,
/// shows a QR, and polls <c>status</c>; the phone completes the booking (Task 12+). Only the two
/// plaintext tokens ever leave this endpoint and they are never logged, audited, or put in a URL.
/// Every failure collapses to a generic <c>INVALID_HANDOFF</c> (400) so nothing acts as an
/// existence oracle.
/// </summary>
public static class TotemBookingHandoffEndpoints
{
    internal static class HandoffWindows
    {
        public static readonly TimeSpan Initial = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan Grace = TimeSpan.FromMinutes(10);
        public static readonly TimeSpan HardCeiling = TimeSpan.FromMinutes(20);
    }

    public static IEndpointRouteBuilder MapTotemBookingHandoffEndpoints(this IEndpointRouteBuilder e)
    {
        e.MapPost("/api/totem/booking-handoffs", Create).AllowAnonymous();
        e.MapPost("/api/totem/booking-handoffs/{id:guid}/status", Status).AllowAnonymous();
        e.MapPost("/api/totem/booking-handoffs/{id:guid}/cancel", Cancel).AllowAnonymous();
        return e;
    }

    internal static IResult Invalid() =>
        Results.Json(new ApiError("INVALID_HANDOFF", "Não foi possível validar este código."), statusCode: 400);

    internal static IResult Expired() =>
        Results.Json(new ApiError("HANDOFF_EXPIRED", "Este QR Code expirou."), statusCode: 410);

    internal static IResult TooMany() =>
        Results.Json(new ApiError("TOO_MANY_REQUESTS", "Tente novamente mais tarde."), statusCode: 429);

    /// <summary>base64url -> exactly 32 bytes -> SHA-256. Any deviation is an indistinguishable failure.</summary>
    internal static bool TryDecodeHash(string? token, out byte[] hash)
    {
        hash = [];
        if (string.IsNullOrWhiteSpace(token)) return false;
        byte[] bytes;
        try { bytes = WebEncoders.Base64UrlDecode(token); }
        catch (FormatException) { return false; }
        if (bytes.Length != 32) return false;
        hash = SHA256.HashData(bytes);
        return true;
    }

    private static string NewToken(out byte[] hash)
    {
        var raw = RandomNumberGenerator.GetBytes(32);
        hash = SHA256.HashData(raw);
        return WebEncoders.Base64UrlEncode(raw);
    }

    private static string ClientIp(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static async Task<IResult> Create(CreateHandoffRequest request, HttpContext ctx,
        TotemHandoffRateLimiter limiter, ApplicationDbContext db, TimeProvider time, CancellationToken ct)
    {
        using var lease = await limiter.AcquireAsync(ClientIp(ctx), "create", ct);
        if (!lease.IsAcquired) return TooMany();

        var professional = await db.Professionals.AsNoTracking()
            .Where(x => x.Id == request.ProfessionalId && x.IsActive)
            .Select(x => new { x.Name, x.Profession })
            .SingleOrDefaultAsync(ct);
        if (professional is null)
            return Results.NotFound(new ApiError("INVALID_HANDOFF", "Profissional indisponível."));

        var now = time.GetUtcNow();
        var handoffToken = NewToken(out var handoffHash);
        var statusToken = NewToken(out var statusHash);
        var handoff = TotemBookingHandoff.Create(request.ProfessionalId, handoffHash, statusHash, now, now + HandoffWindows.Initial);
        db.TotemBookingHandoffs.Add(handoff);
        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            Action = "TOTEM_HANDOFF_CREATED",
            Result = "SUCCEEDED",
            TargetEntityType = "TOTEM_HANDOFF",
            TargetEntityId = handoff.Id,
            OccurredAt = now,
            CorrelationId = ctx.TraceIdentifier
        });
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/totem/booking-handoffs/{handoff.Id}", new
        {
            id = handoff.Id,
            handoffToken,
            statusToken,
            expiresAt = handoff.ExpiresAt,
            professionalName = professional.Name,
            profession = professional.Profession
        });
    }

    private static async Task<IResult> Status(Guid id, HandoffStatusRequest request, HttpContext ctx,
        TotemHandoffRateLimiter limiter, ApplicationDbContext db, TimeProvider time, CancellationToken ct)
    {
        using var lease = await limiter.AcquireAsync($"{ClientIp(ctx)}:{id}", "status", ct);
        if (!lease.IsAcquired) return TooMany();
        if (!TryDecodeHash(request.StatusToken, out var hash)) return Invalid();

        var handoff = await db.TotemBookingHandoffs
            .SingleOrDefaultAsync(x => x.Id == id && x.StatusTokenHash == hash, ct);
        if (handoff is null) return Invalid();

        var now = time.GetUtcNow();
        if (handoff.Status == TotemBookingHandoffStatus.Pending && handoff.ExpiresAt <= now)
        {
            handoff.MarkExpired(now);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // An overlapping request (a racing status poll, or a cancel) folded this same
                // Pending row to a terminal state first. `Version` is xmin, so our UPDATE lost.
                // Re-read the committed row and answer from it — the loser must still return 200.
                db.ChangeTracker.Clear();
                handoff = await db.TotemBookingHandoffs
                    .SingleOrDefaultAsync(x => x.Id == id && x.StatusTokenHash == hash, ct);
                if (handoff is null) return Invalid();
            }
        }

        return handoff.Status switch
        {
            TotemBookingHandoffStatus.Pending => Results.Ok(new { status = "PENDING", expiresAt = handoff.ExpiresAt }),
            TotemBookingHandoffStatus.Expired => Results.Ok(new { status = "EXPIRED" }),
            TotemBookingHandoffStatus.Completed => Results.Ok(await CompletedPayload(db, handoff, ct)),
            _ => Invalid()
        };
    }

    private static async Task<object> CompletedPayload(ApplicationDbContext db, TotemBookingHandoff handoff, CancellationToken ct)
    {
        var row = await (from r in db.Reservations.AsNoTracking().Where(x => x.Id == handoff.ReservationId)
                         join p in db.Professionals.AsNoTracking() on r.ProfessionalId equals p.Id
                         join room in db.Rooms.AsNoTracking() on r.RoomId equals room.Id
                         select new { p.Name, RoomName = room.Name, r.StartAt }).SingleOrDefaultAsync(ct);
        return new { status = "COMPLETED", professionalName = row?.Name, startAt = row?.StartAt, roomName = row?.RoomName };
    }

    // AUTHORIZATION: {id} alone NEVER authorizes cancellation. The row is fetched ONLY when
    // SHA-256(base64url-decode(statusToken)) == StatusTokenHash for that same id, and no
    // mutation happens before that check passes. A bad / missing / wrong-handoff token yields
    // the same generic INVALID_HANDOFF as an unknown id — no existence oracle.
    private static async Task<IResult> Cancel(Guid id, HandoffStatusRequest request, HttpContext ctx,
        TotemHandoffRateLimiter limiter, ApplicationDbContext db, TimeProvider time, CancellationToken ct)
    {
        using var lease = await limiter.AcquireAsync(ClientIp(ctx), "cancel", ct);
        if (!lease.IsAcquired) return TooMany();
        if (!TryDecodeHash(request.StatusToken, out var hash)) return Invalid();

        var handoff = await db.TotemBookingHandoffs
            .SingleOrDefaultAsync(x => x.Id == id && x.StatusTokenHash == hash, ct);
        if (handoff is null) return Invalid();   // wrong token OR unknown id — indistinguishable

        if (handoff.Status == TotemBookingHandoffStatus.Pending)
        {
            var now = time.GetUtcNow();
            handoff.MarkExpired(now);
            db.AuditEntries.Add(new AuditEntry
            {
                Id = Guid.NewGuid(),
                Action = "TOTEM_HANDOFF_CANCELLED",
                Result = "SUCCEEDED",
                TargetEntityType = "TOTEM_HANDOFF",
                TargetEntityId = handoff.Id,
                OccurredAt = now,
                CorrelationId = ctx.TraceIdentifier
            });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // A concurrent status poll or cancel already folded this Pending row to a
                // terminal state (`Version` is xmin, so our UPDATE — audit entry included —
                // lost). Cancel is idempotent: drop the losing unit of work, re-read, and
                // still return 200. The request that won recorded its own audit.
                db.ChangeTracker.Clear();
                handoff = await db.TotemBookingHandoffs
                    .SingleOrDefaultAsync(x => x.Id == id && x.StatusTokenHash == hash, ct);
                if (handoff is null) return Invalid();
            }
        }

        return Results.Ok(new { status = "EXPIRED" });
    }
}

public sealed record CreateHandoffRequest(Guid ProfessionalId) : IStrictModuleRequest;
public sealed record HandoffStatusRequest(string StatusToken) : IStrictModuleRequest;

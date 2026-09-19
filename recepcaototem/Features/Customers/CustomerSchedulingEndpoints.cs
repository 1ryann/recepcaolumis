using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using GestaoPredio.Application.Availability;
using GestaoPredio.Application.Customers;
using GestaoPredio.Application.Leases;
using GestaoPredio.Application.Reservations;
using GestaoPredio.Application.Scheduling;
using GestaoPredio.Domain.Customers;
using GestaoPredio.Domain.Notifications;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using recepcaototem.Api.Configuration;
using recepcaototem.Features.Auth;
using recepcaototem.Features.Common;
using recepcaototem.Features.Reservations;
using recepcaototem.Features.Availability;
using recepcaototem.Features.Totem;

namespace recepcaototem.Features.Customers;

public sealed record CustomerAvailabilityRequest(Guid ProfessionalId, DateOnly Date, int DurationMinutes) : IStrictModuleRequest;
/// <summary><see cref="WhatsAppOptIn"/> true only when the customer ticked the operational WhatsApp opt-in (docs/operations/whatsapp-consent.md).</summary>
public sealed record CustomerReservationRequest(Guid ProfessionalId, DateTimeOffset StartAt, DateTimeOffset EndAt, string? HandoffToken = null,
    bool? WhatsAppOptIn = null) : IStrictModuleRequest;
public sealed record CustomerReservationRescheduleRequest(Guid ProfessionalId, DateTimeOffset StartAt, DateTimeOffset EndAt, string? ConcurrencyToken) : IStrictModuleRequest;
public sealed record CustomerReservationConcurrencyRequest(string? ConcurrencyToken) : IStrictModuleRequest;
public sealed record CustomerReservationPageResponse(ReservationResponse[] Items, int Page, int PageSize, int TotalCount);
public sealed record CustomerProfessionalResponse(Guid Id, string Name, string Profession, string? Description);
public sealed record AvailabilitySlotResponse(DateTimeOffset StartAt, DateTimeOffset EndAt);
public sealed record CustomerBookingHandoffResolveRequest(string HandoffToken) : IStrictModuleRequest;
public sealed record CustomerBookingHandoffResolveResponse(Guid HandoffId, Guid ProfessionalId, string ProfessionalName, string Profession, DateTimeOffset ExpiresAt);

/// <summary>
/// RNG seam for the 6-digit manual check-in code (spec 7A.5). Kept next to the endpoint so a
/// scripted source can be substituted per-test (Task 16); production uses <see cref="DefaultManualCodeSource"/>.
/// </summary>
internal interface IManualCodeSource
{
    ManualCheckInCode Next();
}

internal sealed class DefaultManualCodeSource : IManualCodeSource
{
    public ManualCheckInCode Next() => ManualCheckInCode.Generate();
}

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
        group.MapPost("/booking-handoffs/resolve", ResolveHandoff).AddEndpointFilter<AntiforgeryFilter>();
        return endpoints;
    }

    private static async Task<IResult> ListProfessionals(ApplicationDbContext db, CancellationToken ct) =>
        Results.Ok(await db.Professionals.AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.NormalizedName).ThenBy(x => x.Id)
            .Select(x => new CustomerProfessionalResponse(x.Id, x.Name, x.Profession, x.Description)).ToArrayAsync(ct));

    internal static async Task<IResult> Availability([AsParameters] CustomerAvailabilityRequest request,
        ApplicationDbContext db, IAppointmentAvailabilityService availability, CancellationToken ct)
    {
        if (request.ProfessionalId == Guid.Empty || request.DurationMinutes is < 15 or > 480 || request.DurationMinutes % 15 != 0)
            return Results.BadRequest(new ApiError("INVALID_AVAILABILITY", "Os dados de disponibilidade são inválidos."));
        if (!await db.Professionals.AnyAsync(x => x.Id == request.ProfessionalId && x.IsActive, ct)) return Results.NotFound();
        var slots = await availability.FindSlotsAsync(
            request.ProfessionalId, request.Date, request.DurationMinutes, ct);
        return Results.Ok(slots.Select(slot =>
            new AvailabilitySlotResponse(slot.StartAt, slot.EndAt)).ToArray());
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
        HttpContext context, ApplicationDbContext db, ILeaseResourceLock resourceLock,
        IAppointmentAvailabilityService availability, TimeProvider time, CancellationToken ct)
    {
        var customer = await GetCustomer(principal, db, ct); if (customer is null) return Results.NotFound();
        if (request.ProfessionalId == Guid.Empty || request.EndAt <= request.StartAt) return Results.BadRequest(new ApiError("INVALID_RESERVATION", "Os dados da reserva são inválidos."));
        var professional = await db.Professionals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ProfessionalId && x.IsActive, ct);
        if (professional is null) return Results.NotFound();

        // Task 14 — optional handoff completion. A null/absent HandoffToken leaves every line below
        // this block untouched (201, no rollback dance). When present, the reservation INSERT and the
        // handoff.Complete() UPDATE ride the SAME transaction (all or nothing), and any retry or
        // concurrent duplicate by the same customer replays the winner's reservation (200) — never a
        // second row.
        TotemBookingHandoff? handoff = null;
        byte[] handoffHash = [];
        if (request.HandoffToken is { } rawHandoff)
        {
            if (!TotemBookingHandoffEndpoints.TryDecodeHash(rawHandoff, out handoffHash))
                return Results.Json(new ApiError("INVALID_HANDOFF", "Não foi possível validar este convite."), statusCode: 400);
            handoff = await db.TotemBookingHandoffs.SingleOrDefaultAsync(x => x.HandoffTokenHash == handoffHash, ct);
            if (handoff is null)
                return Results.Json(new ApiError("INVALID_HANDOFF", "Não foi possível validar este convite."), statusCode: 400);

            var nowHandoff = time.GetUtcNow();
            // CASE 4 — terminal-expired, or Pending past its deadline.
            if (handoff.Status == TotemBookingHandoffStatus.Expired ||
                (handoff.Status == TotemBookingHandoffStatus.Pending && handoff.ExpiresAt <= nowHandoff))
                return Results.Json(new ApiError("HANDOFF_EXPIRED", "Este QR Code expirou."), statusCode: 410);

            // CASE 2/3 — already completed: pure-read idempotent replay, no transaction, no SaveChanges.
            if (handoff.Status == TotemBookingHandoffStatus.Completed)
                return await ReplayHandoffAsync(db, handoff, customer, request.ProfessionalId, ct);

            // CASE 1 — Pending & not expired: the invite must be for the professional being booked.
            if (handoff.ProfessionalId != request.ProfessionalId)
                return Results.Json(new ApiError("INVALID_HANDOFF", "Não foi possível validar este convite."), statusCode: 400);
            // `handoff` stays tracked from the query above so Complete() persists in the single SaveChanges.
        }

        var roomIds = await db.Rooms.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Id).Select(x => x.Id).ToListAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest([], roomIds, [request.ProfessionalId]), ct);
        var available = await availability.FindAvailableRoomAsync(
            request.ProfessionalId, request.StartAt, request.EndAt, null, null, ct);
        if (!available.IsAvailable)
        {
            if (handoff is not null)
            {
                // Controller ruling: two concurrent POSTs with the same handoffToken serialize on the
                // Professionals FOR UPDATE lock. After the winner commits, the loser acquires the lock,
                // sees the winner's committed Approved reservation here, and would 409 before ever
                // building a reservation. Intercept: roll back, re-read in a clean state, and replay
                // the winner's reservation (200). A genuine slot conflict (handoff still not Completed)
                // falls through to the normal Conflict below, leaving the handoff Pending.
                await transaction.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                var fresh = await db.TotemBookingHandoffs.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.HandoffTokenHash == handoffHash, ct);
                if (fresh is { Status: TotemBookingHandoffStatus.Completed })
                    return await ReplayHandoffAsync(db, fresh, customer, request.ProfessionalId, ct);
            }
            return AppointmentAvailabilityResults.Conflict(available.Failure);
        }
        var roomId = available.RoomId!.Value;
        var reservation = Reservation.CreateApproved(roomId, request.ProfessionalId, request.StartAt, request.EndAt,
            principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? customer.ApplicationUserId!, time.GetUtcNow(), customer.Id);
        db.Reservations.Add(reservation);
        db.AuditEntries.Add(new GestaoPredio.Domain.Auditing.AuditEntry { Id = Guid.NewGuid(), Action = "RESERVATION_CREATED", Result = "SUCCEEDED", TargetEntityType = "RESERVATION", TargetEntityId = reservation.Id, TargetUserId = customer.ApplicationUserId, OccurredAt = time.GetUtcNow(), CorrelationId = context.TraceIdentifier });
        // Opt-in ticked by the signed-in customer on their own device, committed with the booking. A booking that started
        // at the Totem (QR handoff) is recorded as TOTEM: the journey began there, the decision was made on the phone.
        if (request.WhatsAppOptIn == true &&
            customer.GrantWhatsAppOptIn(handoff is not null ? WhatsAppOptInSource.Totem : WhatsAppOptInSource.CustomerPortal, time.GetUtcNow()))
            db.AuditEntries.Add(Whatsapp.WhatsappOptInEndpoints.Audit(context, "CUSTOMER", customer.Id, customer.ApplicationUserId,
                true, time.GetUtcNow()));
        // Outbox: APPOINTMENT_CONFIRMED, committed with the booking (rolled back with it on a lost race).
        db.WhatsAppNotifications.Add(WhatsAppNotification.AppointmentConfirmed(reservation, time.GetUtcNow())!);
        if (handoff is not null)
        {
            var completedAt = time.GetUtcNow();
            handoff.Complete(reservation.Id, completedAt);
            db.AuditEntries.Add(new GestaoPredio.Domain.Auditing.AuditEntry { Id = Guid.NewGuid(), Action = "TOTEM_HANDOFF_COMPLETED", Result = "SUCCEEDED", TargetEntityType = "TOTEM_HANDOFF", TargetEntityId = handoff.Id, TargetUserId = customer.ApplicationUserId, OccurredAt = completedAt, CorrelationId = context.TraceIdentifier });
        }
        try
        {
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException) when (handoff is not null)
        {
            // Safety net for any window where both requests cleared the lock + availability: the
            // loser's handoff.Complete() UPDATE loses the xmin race. Same recovery as above.
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var fresh = await db.TotemBookingHandoffs.AsNoTracking()
                .SingleOrDefaultAsync(x => x.HandoffTokenHash == handoffHash, ct);
            if (fresh is { Status: TotemBookingHandoffStatus.Completed })
                return await ReplayHandoffAsync(db, fresh, customer, request.ProfessionalId, ct);
            return Results.Json(new ApiError("HANDOFF_ALREADY_USED", "Este convite já foi utilizado."), statusCode: 409);
        }
        return Results.Created($"/api/customer/reservations/{reservation.Id}", reservation.ToResponse((await db.Rooms.FindAsync([roomId], ct))!.Name, professional.Name));
    }

    /// <summary>
    /// CASE 2/3 idempotent replay: a handoff that is already <see cref="TotemBookingHandoffStatus.Completed"/>
    /// yields the reservation it produced — but only to the customer who owns it, and only while that
    /// reservation is still an Approved booking for the same professional. Any invariant miss collapses
    /// to a generic <c>409 HANDOFF_ALREADY_USED</c> with no reservation data in the body. Pure read:
    /// opens no transaction and issues no SaveChanges.
    /// </summary>
    private static async Task<IResult> ReplayHandoffAsync(ApplicationDbContext db, TotemBookingHandoff handoff,
        Customer customer, Guid requestProfessionalId, CancellationToken ct)
    {
        var reservation = handoff.ReservationId is { } reservationId
            ? await db.Reservations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == reservationId, ct)
            : null;
        if (reservation is null
            || reservation.CustomerId != customer.Id
            || reservation.ProfessionalId != handoff.ProfessionalId
            || reservation.ProfessionalId != requestProfessionalId
            || reservation.Status != ReservationStatus.Approved)
            return Results.Json(new ApiError("HANDOFF_ALREADY_USED", "Este convite já foi utilizado."), statusCode: 409);

        var roomName = await db.Rooms.AsNoTracking().Where(x => x.Id == reservation.RoomId).Select(x => x.Name).SingleAsync(ct);
        var professionalName = await db.Professionals.AsNoTracking().Where(x => x.Id == reservation.ProfessionalId).Select(x => x.Name).SingleAsync(ct);
        return Results.Ok(reservation.ToResponse(roomName, professionalName));
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
        ApplicationDbContext db, IManualCodeSource codes, IManualCheckInCodeHasher hasher, TimeProvider time, CancellationToken ct)
    {
        var customer = await GetCustomer(principal, db, ct); if (customer is null) return Results.NotFound();
        var reservation = await db.Reservations.SingleOrDefaultAsync(x => x.Id == id && x.CustomerId == customer.Id, ct);
        if (reservation is null) return Results.NotFound();
        var now = time.GetUtcNow();
        if (reservation.Status != ReservationStatus.Approved || reservation.EndAt <= now || now < reservation.StartAt.Subtract(TimeSpan.FromHours(1)))
            return Results.BadRequest(new ApiError("CHECK_IN_NOT_ELIGIBLE", "O check-in não está disponível para esta reserva."));

        // Strong QR token: one CSPRNG draw, hashed once. Only the 6-digit manual code is redrawn
        // per attempt via the bounded collision loop + lazy reclaim (spec 7A.5 / 7A.7).
        var raw = RandomNumberGenerator.GetBytes(32);
        var tokenHash = SHA256.HashData(raw);

        // One shared budget: each iteration draws exactly one code, so total codes.Next() calls <= maxAttempts.
        // A still-resolvable collision (continue) and a unique-violation race (caught DbUpdateException) each
        // spend one iteration of this same loop; both exhaustion routes converge on the single 503 below.
        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var code = codes.Next();
            var manualCodeHash = hasher.Hash(code);

            var clash = await db.CheckInTokens
                .SingleOrDefaultAsync(x => x.ReservationId != id && x.ManualCodeHash == manualCodeHash, ct);
            if (clash is not null)
            {
                var resolvable = clash.RevokedAt == null && clash.UsedAt == null && clash.ExpiresAt > now;
                if (resolvable)
                {
                    // Real collision with a live credential -> redraw, spending this attempt (7A.5 step 5).
                    await transaction.RollbackAsync(ct);
                    db.ChangeTracker.Clear();
                    continue;
                }
                // Stale colliding row -> lazy reclaim in this same transaction; keep the code (7A.5 step 6).
                clash.ClearManualCode();
            }

            var token = await db.CheckInTokens.SingleOrDefaultAsync(x => x.ReservationId == id, ct);
            if (token is null) db.CheckInTokens.Add(token = CheckInToken.Create(id, tokenHash, manualCodeHash, now, reservation.EndAt));
            else token.Rotate(tokenHash, manualCodeHash, now, reservation.EndAt);

            db.AuditEntries.Add(new GestaoPredio.Domain.Auditing.AuditEntry { Id = Guid.NewGuid(), Action = "CHECK_IN_TOKEN_ISSUED", Result = "SUCCEEDED", TargetEntityType = "RESERVATION", TargetEntityId = id, TargetUserId = customer.ApplicationUserId, OccurredAt = now, CorrelationId = context.TraceIdentifier });
            try
            {
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return Results.Ok(new { token = WebEncoders.Base64UrlEncode(raw), manualCode = code.Value, expiresAt = reservation.EndAt });
            }
            catch (DbUpdateException)
            {
                // A concurrent issuer won the unique-violation race on UX_CheckInTokens_ManualCodeHash
                // (7A.5 step 7). Roll back and fall through: the SAME attempt budget covers this retry.
                await transaction.RollbackAsync(ct);
                db.ChangeTracker.Clear();
            }
        }

        // Budget exhausted (7A.5 step 8): 503 + CHECK_IN_TOKEN_ISSUE_FAILED audit — no code, no hash.
        await using (var failTransaction = await db.Database.BeginTransactionAsync(ct))
        {
            db.AuditEntries.Add(new GestaoPredio.Domain.Auditing.AuditEntry { Id = Guid.NewGuid(), Action = "CHECK_IN_TOKEN_ISSUE_FAILED", Result = "FAILED", TargetEntityType = "RESERVATION", TargetEntityId = id, TargetUserId = customer.ApplicationUserId, OccurredAt = now, CorrelationId = context.TraceIdentifier });
            await db.SaveChangesAsync(ct);
            await failTransaction.CommitAsync(ct);
        }
        return Results.Json(new ApiError("CHECK_IN_CODE_UNAVAILABLE", "Não foi possível gerar o código agora. Tente novamente."), statusCode: 503);
    }

    private static async Task<IResult> RescheduleReservation(Guid id, CustomerReservationRescheduleRequest request, ClaimsPrincipal principal,
        ApplicationDbContext db, ILeaseResourceLock resourceLock,
        IAppointmentAvailabilityService availability, TimeProvider time, CancellationToken ct)
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
        var available = await availability.FindAvailableRoomAsync(
            original.ProfessionalId, request.StartAt, request.EndAt, roomId, id, ct);
        if (!available.IsAvailable) return AppointmentAvailabilityResults.Conflict(available.Failure);
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

    /// <summary>
    /// Read-only professional context for a handoff so the phone's booking screen can pre-select the
    /// professional. NO state mutation — <c>claim</c> (Task 12) already started the clock; this must not.
    /// Every failure collapses to the two generic shapes and the body carries no customer or
    /// reservation data and never the token.
    /// </summary>
    private static async Task<IResult> ResolveHandoff(CustomerBookingHandoffResolveRequest request, HttpContext context,
        TotemHandoffRateLimiter limiter, ApplicationDbContext db, TimeProvider time, CancellationToken ct)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        using var lease = await limiter.AcquireAsync(ip, "resolve", ct);
        if (!lease.IsAcquired)
            return Results.Json(new ApiError("TOO_MANY_REQUESTS", "Tente novamente mais tarde."), statusCode: 429);

        if (!TotemBookingHandoffEndpoints.TryDecodeHash(request.HandoffToken, out var hash))
            return Results.Json(new ApiError("INVALID_HANDOFF", "Não foi possível validar este código."), statusCode: 400);

        var now = time.GetUtcNow();
        var handoff = await db.TotemBookingHandoffs.AsNoTracking()
            .SingleOrDefaultAsync(x => x.HandoffTokenHash == hash, ct);
        if (handoff is null || !handoff.IsUsable(now))
            return Results.Json(new ApiError("HANDOFF_EXPIRED", "Este convite expirou."), statusCode: 410);

        var professional = await db.Professionals.AsNoTracking()
            .Where(x => x.Id == handoff.ProfessionalId)
            .Select(x => new { x.Name, x.Profession })
            .SingleOrDefaultAsync(ct);
        if (professional is null)
            return Results.Json(new ApiError("HANDOFF_EXPIRED", "Este convite expirou."), statusCode: 410);

        return Results.Ok(new CustomerBookingHandoffResolveResponse(
            handoff.Id, handoff.ProfessionalId, professional.Name, professional.Profession, handoff.ExpiresAt));
    }

    private static IResult InvalidToken() => Results.BadRequest(new ApiError("INVALID_CONCURRENCY_TOKEN", "O token de concorrência informado é inválido."));
    private static IResult Modified() => Results.Json(new ApiError("RESOURCE_MODIFIED", "O registro foi alterado por outra operação. Recarregue os dados e tente novamente."), statusCode: StatusCodes.Status409Conflict);
}

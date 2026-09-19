using System.Security.Claims;
using GestaoPredio.Application.Leases;
using GestaoPredio.Application.Availability;
using GestaoPredio.Application.Reservations;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Notifications;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Reservations;

public static partial class ReservationEndpoints
{
    private static async Task<IResult> Reschedule(
        Guid id,
        RescheduleReservationRequest request,
        HttpContext context,
        ApplicationDbContext db,
        ILeaseResourceLock resourceLock,
        IAppointmentAvailabilityService availability,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryDecode(request.ConcurrencyToken, out var version)) return InvalidToken();
        var locator = await db.Reservations.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (locator is null) return Results.NotFound();
        var now = timeProvider.GetUtcNow();
        if (request.EndAt <= request.StartAt || request.StartAt <= now) return Invalid();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest(
            [], [locator.RoomId], [locator.ProfessionalId]), cancellationToken);
        var original = await db.Reservations.SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (original is null) return Results.NotFound();
        if (original.Version != version) return Modified();
        if (original.Status != ReservationStatus.Approved || original.Kind == ReservationKind.Cancellation)
            return InvalidTransition();
        if (!await ResourcesAreActive(db, original.RoomId, original.ProfessionalId, cancellationToken))
            return Results.BadRequest(new ApiError(
                "INVALID_RESERVATION_RESOURCE", "A sala ou o profissional informado é inválido."));
        var available = await availability.FindAvailableRoomAsync(original.ProfessionalId,
            request.StartAt, request.EndAt, original.RoomId, original.Id, cancellationToken);
        if (!available.IsAvailable)
            return Features.Availability.AppointmentAvailabilityResults.Conflict(available.Failure);

        Reservation replacement;
        try
        {
            replacement = Reservation.CreateApprovedReschedule(
                original, request.StartAt, request.EndAt, Actor(context)!, now);
            db.Entry(original).Property(value => value.Version).OriginalValue = version;
            original.Cancel(Actor(context)!, now);
            await ReservationCheckInTokenRevocation.RevokeAsync(db, original.Id, now, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return InvalidTransition();
        }
        catch (ArgumentException)
        {
            return Invalid();
        }
        db.Reservations.Add(replacement);
        db.AuditEntries.Add(ReservationAudit.CreateSucceeded(
            original.Id, AuditActions.ReservationRescheduled, now, context.TraceIdentifier,
            Actor(context), context.Connection.RemoteIpAddress?.ToString()));
        db.AuditEntries.Add(ReservationAudit.CreateSucceeded(
            replacement.Id, AuditActions.ReservationCreated, now, context.TraceIdentifier,
            Actor(context), context.Connection.RemoteIpAddress?.ToString()));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Modified();
        }
        var names = await LoadNames(db, replacement, cancellationToken);
        return Results.Created($"/api/admin/reservations/{replacement.Id}",
            replacement.ToResponse(names.Room, names.Professional));
    }

    private static async Task<IResult> Approve(
        Guid id,
        ReservationConcurrencyRequest request,
        HttpContext context,
        ApplicationDbContext db,
        ILeaseResourceLock resourceLock,
        IAppointmentAvailabilityService availability,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryDecode(request.ConcurrencyToken, out var version)) return InvalidToken();
        var locator = await db.Reservations.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (locator is null) return Results.NotFound();
        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest(
            [], [locator.RoomId], [locator.ProfessionalId]), cancellationToken);
        var reservation = await db.Reservations.SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (reservation is null) return Results.NotFound();
        if (reservation.Version != version) return Modified();
        if (reservation.Status != ReservationStatus.Pending) return InvalidTransition();
        if (reservation.Kind != ReservationKind.Cancellation && reservation.StartAt <= now)
            return InvalidTransition();

        Reservation? original = null;
        if (reservation.OriginalReservationId is { } originalId)
        {
            original = await db.Reservations.SingleOrDefaultAsync(value => value.Id == originalId, cancellationToken);
            if (original is null || original.Status != ReservationStatus.Approved ||
                original.Kind == ReservationKind.Cancellation ||
                original.RoomId != reservation.RoomId || original.ProfessionalId != reservation.ProfessionalId)
                return InvalidTransition();
        }

        if (reservation.Kind != ReservationKind.Cancellation)
        {
            if (!await ResourcesAreActive(db, reservation.RoomId, reservation.ProfessionalId, cancellationToken))
                return Results.BadRequest(new ApiError(
                    "INVALID_RESERVATION_RESOURCE", "A sala ou o profissional informado é inválido."));
            var available = await availability.FindAvailableRoomAsync(reservation.ProfessionalId,
                reservation.StartAt, reservation.EndAt, reservation.RoomId, original?.Id, cancellationToken);
            if (!available.IsAvailable)
                return Features.Availability.AppointmentAvailabilityResults.Conflict(available.Failure);
        }

        db.Entry(reservation).Property(value => value.Version).OriginalValue = version;
        try
        {
            reservation.Approve(Actor(context)!, now);
            if (original is not null)
            {
                original.Cancel(Actor(context)!, now);
                await ReservationCheckInTokenRevocation.RevokeAsync(db, original.Id, now, cancellationToken);
                var originalAction = reservation.Kind == ReservationKind.Reschedule
                    ? AuditActions.ReservationRescheduled
                    : AuditActions.ReservationCancelled;
                db.AuditEntries.Add(ReservationAudit.CreateSucceeded(
                    original.Id, originalAction, now, context.TraceIdentifier,
                    Actor(context), context.Connection.RemoteIpAddress?.ToString()));
            }
        }
        catch (InvalidOperationException)
        {
            return InvalidTransition();
        }
        db.AuditEntries.Add(ReservationAudit.CreateSucceeded(
            reservation.Id, AuditActions.ReservationApproved, now, context.TraceIdentifier,
            Actor(context), context.Connection.RemoteIpAddress?.ToString()));
        // Outbox, committed with the decision: an approved cancellation request notifies the original's customer.
        if (reservation.Kind == ReservationKind.Cancellation && original is not null &&
            WhatsAppNotification.ReservationCancelled(original, now) is { } notice)
            db.WhatsAppNotifications.Add(notice);
        return await SaveDecision(db, transaction, reservation, cancellationToken);
    }

    private static async Task<IResult> Reject(
        Guid id,
        RejectReservationRequest request,
        HttpContext context,
        ApplicationDbContext db,
        ILeaseResourceLock resourceLock,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryDecode(request.ConcurrencyToken, out var version)) return InvalidToken();
        var locator = await db.Reservations.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (locator is null) return Results.NotFound();
        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest(
            [], [locator.RoomId], [locator.ProfessionalId]), cancellationToken);
        var reservation = await db.Reservations.SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (reservation is null) return Results.NotFound();
        if (reservation.Version != version) return Modified();
        db.Entry(reservation).Property(value => value.Version).OriginalValue = version;
        try
        {
            reservation.Reject(request.Reason ?? string.Empty, Actor(context)!, now);
        }
        catch (InvalidOperationException)
        {
            return InvalidTransition();
        }
        catch (ArgumentException)
        {
            return Invalid();
        }
        await ReservationCheckInTokenRevocation.RevokeAsync(db, reservation.Id, now, cancellationToken);
        db.AuditEntries.Add(ReservationAudit.CreateSucceeded(
            reservation.Id, AuditActions.ReservationRejected, now, context.TraceIdentifier,
            Actor(context), context.Connection.RemoteIpAddress?.ToString()));
        return await SaveDecision(db, transaction, reservation, cancellationToken);
    }

    private static async Task<IResult> Cancel(
        Guid id,
        ReservationConcurrencyRequest request,
        HttpContext context,
        ApplicationDbContext db,
        ILeaseResourceLock resourceLock,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryDecode(request.ConcurrencyToken, out var version)) return InvalidToken();
        var locator = await db.Reservations.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (locator is null) return Results.NotFound();
        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest(
            [], [locator.RoomId], [locator.ProfessionalId]), cancellationToken);
        var reservation = await db.Reservations.SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (reservation is null) return Results.NotFound();
        if (reservation.Version != version) return Modified();
        db.Entry(reservation).Property(value => value.Version).OriginalValue = version;
        try
        {
            reservation.Cancel(Actor(context)!, now);
        }
        catch (InvalidOperationException)
        {
            return InvalidTransition();
        }
        await ReservationCheckInTokenRevocation.RevokeAsync(db, reservation.Id, now, cancellationToken);
        db.AuditEntries.Add(ReservationAudit.CreateSucceeded(
            reservation.Id, AuditActions.ReservationCancelled, now, context.TraceIdentifier,
            Actor(context), context.Connection.RemoteIpAddress?.ToString()));
        // Outbox: APPOINTMENT_CANCELLED to the customer, committed with the cancellation.
        if (WhatsAppNotification.ReservationCancelled(reservation, now) is { } notice)
            db.WhatsAppNotifications.Add(notice);
        return await SaveDecision(db, transaction, reservation, cancellationToken);
    }

    private static async Task<IResult> SaveDecision(
        ApplicationDbContext db,
        IDbContextTransaction transaction,
        Reservation reservation,
        CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Modified();
        }
        var names = await LoadNames(db, reservation, cancellationToken);
        return Results.Ok(reservation.ToResponse(names.Room, names.Professional));
    }

    private static IResult InvalidToken() => Results.BadRequest(new ApiError(
        "INVALID_CONCURRENCY_TOKEN", "O token de concorrência informado é inválido."));
    private static IResult Modified() => Results.Json(new ApiError(
        "RESOURCE_MODIFIED", "O registro foi alterado por outra operação. Recarregue os dados e tente novamente."),
        statusCode: StatusCodes.Status409Conflict);
    private static IResult InvalidTransition() => Results.Json(new ApiError(
        "INVALID_RESERVATION_TRANSITION", "A reserva não permite esta operação no estado atual."),
        statusCode: StatusCodes.Status409Conflict);
}

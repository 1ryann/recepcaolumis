using GestaoPredio.Application.Leases;
using GestaoPredio.Application.Visits;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Customers;
using GestaoPredio.Domain.Notifications;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Visits;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GestaoPredio.Infrastructure.Visits;

/// <summary>
/// The one place an arrival is confirmed. Every route — kiosk, reception desk, back office — resolves
/// who is arriving and then hands the decision here, so the visit, its transition, the audit entry and
/// the WhatsApp outbox row are always written the same way, in one transaction.
/// </summary>
public sealed class CheckInService(
    ApplicationDbContext db,
    ILeaseResourceLock resourceLock,
    TimeProvider time) : ICheckInService
{
    /// <summary>
    /// The partial unique index that makes "one open visit per reservation" a database guarantee rather
    /// than a hopeful read-then-write.
    /// </summary>
    public const string OpenReservationIndex = "UX_Visits_OpenReservation";

    public async Task<CheckInResult> ConfirmArrivalAsync(CheckInRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ActorUserId))
            throw new ArgumentException("O ator deve ser informado.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.CorrelationId))
            throw new ArgumentException("A correlação deve ser informada.", nameof(request));

        // Resolving the resources before opening the transaction keeps the lock order identical to every
        // other writer: professional and room rows first, then the work.
        Guid professionalId;
        Guid? roomId;
        if (request.ReservationId is Guid locatorId)
        {
            var locator = await db.Reservations.AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == locatorId, cancellationToken);
            if (locator is null) return Failed(CheckInOutcome.ReservationNotFound);
            if (request.ProfessionalId is Guid declared && declared != locator.ProfessionalId)
                return Failed(CheckInOutcome.InvalidResource);
            if (request.RoomId is Guid declaredRoom && declaredRoom != locator.RoomId)
                return Failed(CheckInOutcome.InvalidResource);
            professionalId = locator.ProfessionalId;
            roomId = locator.RoomId;
        }
        else
        {
            if (request.ProfessionalId is not Guid walkIn || walkIn == Guid.Empty)
                return Failed(CheckInOutcome.InvalidResource);
            professionalId = walkIn;
            roomId = request.RoomId;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await resourceLock.AcquireAsync(new LeaseResourceLockRequest(
            [], roomId is null ? [] : [roomId.Value], [professionalId]), cancellationToken);

        var now = time.GetUtcNow();
        Customer? customer = null;

        if (request.ReservationId is Guid reservationId)
        {
            var reservation = await db.Reservations
                .SingleOrDefaultAsync(value => value.Id == reservationId, cancellationToken);
            if (reservation is null) return Failed(CheckInOutcome.ReservationNotFound);
            if (request.ExpectedReservationVersion is uint expected && reservation.Version != expected)
                return Failed(CheckInOutcome.ReservationModified);
            if (reservation.Status != ReservationStatus.Approved || reservation.Kind == ReservationKind.Cancellation)
                return Failed(CheckInOutcome.ReservationNotEligible);
            if (request.EnforceArrivalWindow && !CheckInWindow.IsOpen(reservation.StartAt, reservation.EndAt, now))
                return Failed(CheckInOutcome.OutsideArrivalWindow);
            if (reservation.CustomerId is Guid customerId)
            {
                customer = await db.Customers.SingleOrDefaultAsync(value => value.Id == customerId, cancellationToken);
                if (customer is null || !customer.IsActive) return Failed(CheckInOutcome.CustomerNotEligible);
            }

            // Held under the resource lock, so a concurrent arrival for the same reservation is either
            // still waiting for the lock or already visible here.
            var open = await db.Visits.SingleOrDefaultAsync(value => value.ReservationId == reservationId &&
                (value.Status == VisitStatus.Waiting || value.Status == VisitStatus.InService), cancellationToken);
            if (open is not null) return new CheckInResult(CheckInOutcome.AlreadyCheckedIn, open);
        }
        else
        {
            // Nothing authorises a walk-in but the resources themselves, so they must still be usable.
            if (!await db.Professionals.AnyAsync(value => value.Id == professionalId && value.IsActive, cancellationToken))
                return Failed(CheckInOutcome.InvalidResource);
            if (roomId is Guid walkInRoom &&
                !await db.Rooms.AnyAsync(value => value.Id == walkInRoom && value.IsActive, cancellationToken))
                return Failed(CheckInOutcome.InvalidResource);
        }

        // The customer on the reservation names the visit; the caller's name is the fallback for a
        // reservation without one, which is how a walk-in gets named at all.
        var visitorName = customer?.Name ?? request.VisitorName?.Trim();
        if (string.IsNullOrWhiteSpace(visitorName)) return Failed(CheckInOutcome.VisitorNameRequired);

        Visit visit;
        try
        {
            visit = Visit.Arrive(professionalId, roomId, request.ReservationId, visitorName,
                request.ActorUserId, now, customer?.Id);
        }
        catch (ArgumentException)
        {
            return Failed(CheckInOutcome.InvalidResource);
        }

        db.Visits.Add(visit);
        db.VisitTransitions.Add(VisitTransition.Record(visit.Id, null, VisitStatus.Waiting, request.ActorUserId, now));
        db.AuditEntries.Add(VisitAudit.CreateSucceeded(visit.Id, AuditActionFor(request.Origin), now,
            request.CorrelationId, request.ActorUserId, request.IpAddress));
        // Outbox: committed with the arrival, sent later by the dispatcher — a check-in never waits on Meta.
        db.WhatsAppNotifications.Add(WhatsAppNotification.ClientCheckedIn(visit, now));
        if (request.ReservationId is Guid credentialReservation)
            await ConsumeCredentialAsync(credentialReservation, now, cancellationToken);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (request.ReservationId is not null && IsOpenVisitViolation(exception))
        {
            // A concurrent arrival for this reservation committed first. The index is the authority, so
            // this caller joins that visit instead of failing.
            await DiscardAsync(transaction, cancellationToken);
            return await ReadOpenVisitAsync(request.ReservationId.Value, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await DiscardAsync(transaction, cancellationToken);
            return Failed(CheckInOutcome.ReservationModified);
        }

        return new CheckInResult(CheckInOutcome.Confirmed, visit);
    }

    /// <summary>
    /// A check-in credential authorises one arrival for its reservation, whoever confirms it. Consuming it
    /// on every route stops a QR code or manual code from opening a second visit once this one is over.
    /// </summary>
    private async Task ConsumeCredentialAsync(Guid reservationId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var token = await db.CheckInTokens.SingleOrDefaultAsync(
            value => value.ReservationId == reservationId && value.RevokedAt == null && value.UsedAt == null,
            cancellationToken);
        token?.MarkUsed(now);
    }

    private async Task<CheckInResult> ReadOpenVisitAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        var open = await db.Visits.AsNoTracking().SingleOrDefaultAsync(value => value.ReservationId == reservationId &&
            (value.Status == VisitStatus.Waiting || value.Status == VisitStatus.InService), cancellationToken);
        return open is null
            ? Failed(CheckInOutcome.ReservationModified)
            : new CheckInResult(CheckInOutcome.AlreadyCheckedIn, open);
    }

    /// <summary>
    /// Rolls the unit of work back and forgets it: the rejected visit, transition, audit entry and outbox
    /// row must not ride along on whatever the caller does with this context next.
    /// </summary>
    private async Task DiscardAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    private static bool IsOpenVisitViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres &&
        string.Equals(postgres.ConstraintName, OpenReservationIndex, StringComparison.Ordinal);

    private static string AuditActionFor(CheckInOrigin origin) => origin switch
    {
        CheckInOrigin.Totem => AuditActions.VisitCheckedIn,
        CheckInOrigin.Reception => AuditActions.VisitCheckedInManual,
        CheckInOrigin.Admin => AuditActions.VisitArrived,
        _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, "Origem de check-in desconhecida.")
    };

    private static CheckInResult Failed(CheckInOutcome outcome) => new(outcome, null);
}

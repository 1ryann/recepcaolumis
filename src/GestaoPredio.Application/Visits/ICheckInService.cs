using GestaoPredio.Domain.Visits;

namespace GestaoPredio.Application.Visits;

/// <summary>
/// Where an arrival was confirmed. The check-in core never branches on it: the origin only picks the
/// audit action, so the operational trail keeps telling the kiosk, the desk and the back office apart.
/// </summary>
public enum CheckInOrigin
{
    Totem = 1,
    Reception = 2,
    Admin = 3
}

public enum CheckInOutcome
{
    /// <summary>A new <see cref="Visit"/> was created.</summary>
    Confirmed,

    /// <summary>
    /// An open visit already existed for the reservation, so nothing was written. Confirming the same
    /// arrival twice — by the same route or by two different ones — lands here.
    /// </summary>
    AlreadyCheckedIn,

    ReservationNotFound,

    /// <summary>The reservation is not approved, or it is a cancellation.</summary>
    ReservationNotEligible,

    /// <summary>The reservation's customer is inactive.</summary>
    CustomerNotEligible,

    /// <summary>Outside <see cref="CheckInWindow"/>.</summary>
    OutsideArrivalWindow,

    /// <summary>No customer to name the visit after and no fallback name supplied.</summary>
    VisitorNameRequired,

    /// <summary>The professional or room is missing, inactive, or does not match the reservation.</summary>
    InvalidResource,

    /// <summary>The caller's expected reservation version is stale, or a concurrent write won.</summary>
    ReservationModified
}

public sealed record CheckInResult(CheckInOutcome Outcome, Visit? Visit)
{
    public bool Succeeded => Outcome is CheckInOutcome.Confirmed or CheckInOutcome.AlreadyCheckedIn;
}

/// <summary>
/// One confirmed arrival. <see cref="ReservationId"/> null means a walk-in, which no reservation
/// authorises and which therefore carries its own professional, room and visitor name.
/// </summary>
public sealed record CheckInRequest
{
    public required CheckInOrigin Origin { get; init; }

    /// <summary>The signed-in user, or a route marker such as <c>TOTEM</c> for an unattended kiosk.</summary>
    public required string ActorUserId { get; init; }

    public required string CorrelationId { get; init; }

    public Guid? ReservationId { get; init; }

    /// <summary>
    /// Required for a walk-in. Supplied alongside a reservation it is cross-checked against it, which is
    /// how the admin route rejects a professional that does not own the reservation.
    /// </summary>
    public Guid? ProfessionalId { get; init; }

    /// <summary>Cross-checked against the reservation when both are present.</summary>
    public Guid? RoomId { get; init; }

    /// <summary>Used when the reservation has no customer to name the visit after.</summary>
    public string? VisitorName { get; init; }

    /// <summary>
    /// Whether <see cref="CheckInWindow"/> applies. A credential presented by the customer is only good
    /// inside the window; a member of staff with the person in front of them may confirm a late arrival.
    /// </summary>
    public bool EnforceArrivalWindow { get; init; }

    /// <summary>Optimistic concurrency against the reservation, when the caller holds a version.</summary>
    public uint? ExpectedReservationVersion { get; init; }

    public string? IpAddress { get; init; }
}

/// <summary>
/// The single core every arrival goes through: the kiosk, the reception desk, the back office and, later,
/// a physical access-control event. It owns the unit of work — eligibility, the visit, its transition, the
/// audit entry and the outbox row all commit together, or none of them do.
/// </summary>
public interface ICheckInService
{
    Task<CheckInResult> ConfirmArrivalAsync(CheckInRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The arrival window: a customer-presented credential is good from one hour before the reservation
/// starts until it ends. Defined once because both the kiosk preview and the authoritative check-in
/// have to agree on it.
/// </summary>
public static class CheckInWindow
{
    public static readonly TimeSpan EarlyGrace = TimeSpan.FromHours(1);

    public static bool IsOpen(DateTimeOffset startAt, DateTimeOffset endAt, DateTimeOffset now) =>
        now >= startAt - EarlyGrace && now < endAt;
}

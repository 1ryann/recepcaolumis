namespace GestaoPredio.Application.Customers;

/// <summary>
/// What a valid check-in credential points at. The credential itself never leaves the resolver — only
/// the reservation it authorises and the details a caller needs to show or to audit.
/// </summary>
/// <param name="AlreadyUsed">
/// The credential was already spent by a confirmed arrival. Callers that allow this get the reservation
/// anyway, so presenting the same QR code twice answers with the visit it produced instead of a failure.
/// </param>
public sealed record ResolvedCheckInCredential(
    Guid ReservationId,
    Guid CustomerId,
    string CustomerName,
    string ProfessionalName,
    string RoomName,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    bool AlreadyUsed);

/// <summary>
/// Turns a presented check-in credential — the strong QR token or the 6-digit manual code — into the
/// reservation it authorises, or null when it does not authorise one. One implementation, so the kiosk
/// and any other reader (a physical access controller, say) apply the same validity rules: the hash must
/// match, and the credential must not be revoked, expired, attached to a reservation that is no longer
/// approved, attached to an inactive customer, or presented outside <see cref="Visits.CheckInWindow"/>.
/// </summary>
public interface ICheckInCredentialResolver
{
    Task<ResolvedCheckInCredential?> ResolveAsync(
        string presented,
        bool allowUsed,
        CancellationToken cancellationToken);
}

using GestaoPredio.Domain.Reservations;

namespace GestaoPredio.Application.Reservations;

public static class ReservationAvailability
{
    public static bool Overlaps(DateTimeOffset firstStart, DateTimeOffset firstEnd,
        DateTimeOffset secondStart, DateTimeOffset secondEnd) =>
        secondStart < firstEnd && firstStart < secondEnd;

    public static bool BlocksResources(ReservationStatus status, ReservationKind kind) =>
        status == ReservationStatus.Approved && kind != ReservationKind.Cancellation;
}

public sealed record ReservationResourceConflict(bool Room, bool Professional, bool Lease)
{
    public bool Any => Room || Professional || Lease;
}

public interface IReservationConflictDetector
{
    Task<ReservationResourceConflict> FindConflictAsync(
        Guid roomId,
        Guid professionalId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        Guid? excludedReservationId,
        CancellationToken cancellationToken);
}

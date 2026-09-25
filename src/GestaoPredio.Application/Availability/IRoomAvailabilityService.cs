using GestaoPredio.Domain.Availability;
using GestaoPredio.Domain.Leases;

namespace GestaoPredio.Application.Availability;

public enum RoomAvailabilityConflict
{
    None,
    OutsideOperatingHours,
    RoomBlock,
    Reservation,
    Lease
}

public interface IRoomAvailabilityService
{
    Task<IReadOnlyList<RoomOperationalStatus>> ReadRoomOperationalStatusAsync(
        DateTimeOffset now, CancellationToken cancellationToken);
    Task<RoomAvailabilityConflict> CheckScheduleAndBlocksAsync(Guid roomId,
        DateTimeOffset startAt, DateTimeOffset? endAt, Guid? excludedRoomBlockId,
        bool enforceOperatingHours, CancellationToken cancellationToken);

    Task<RoomAvailabilityConflict> CheckBlockConflictsAsync(Guid roomId,
        DateTimeOffset startAt, DateTimeOffset endAt, Guid? excludedRoomBlockId,
        CancellationToken cancellationToken);

    Task<RoomAvailabilityConflict> CheckLeaseRoomAsync(Guid roomId,
        DateTimeOffset startAt, DateTimeOffset? endAt, LeaseMode mode,
        CancellationToken cancellationToken);

    /// <summary>The first commitment the proposed schedule would leave outside the opening hours, or null.</summary>
    Task<OperatingHoursConflict?> CanApplyScheduleAsync(IReadOnlyCollection<OperatingHourInterval> proposedIntervals,
        DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>
/// What a proposed schedule would leave outside the opening hours. Carried back to the screen so it can say which
/// booking is in the way instead of asking the operator to guess which period to adjust.
/// </summary>
public sealed record OperatingHoursConflict(OperatingHoursConflictKind Kind, string RoomName, string? TenantName,
    DateTimeOffset StartAt, DateTimeOffset EndAt);

public enum OperatingHoursConflictKind { Reservation, Lease }

public sealed record RoomOperationalStatus(Guid RoomId, string RoomName, string Status,
    DateTimeOffset? NextCommitmentAt);

using GestaoPredio.Domain.Availability;

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
    Task<RoomAvailabilityConflict> CheckScheduleAndBlocksAsync(Guid roomId,
        DateTimeOffset startAt, DateTimeOffset? endAt, Guid? excludedRoomBlockId,
        bool enforceOperatingHours, CancellationToken cancellationToken);

    Task<RoomAvailabilityConflict> CheckBlockConflictsAsync(Guid roomId,
        DateTimeOffset startAt, DateTimeOffset endAt, Guid? excludedRoomBlockId,
        CancellationToken cancellationToken);

    Task<RoomAvailabilityConflict> CheckLeaseRoomAsync(Guid roomId,
        DateTimeOffset startAt, DateTimeOffset? endAt, bool enforceOperatingHours,
        CancellationToken cancellationToken);

    Task<bool> CanApplyScheduleAsync(IReadOnlyCollection<OperatingHourInterval> proposedIntervals,
        DateTimeOffset now, CancellationToken cancellationToken);
}

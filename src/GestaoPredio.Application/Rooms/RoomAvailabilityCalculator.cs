using GestaoPredio.Domain.Leases;
using GestaoPredio.Domain.Rooms;

namespace GestaoPredio.Application.Rooms;

public static class RoomAvailabilityCalculator
{
    public static RoomRentalAvailability? Calculate(
        IEnumerable<Lease> leases,
        DateTimeOffset now,
        TimeZoneInfo timeZone)
    {
        var blocking = leases.Where(x => x.GetOperationalStatus(now)
            is LeaseOperationalStatus.Active or LeaseOperationalStatus.Scheduled).ToArray();
        if (blocking.Length == 0)
            return new(PublicRoomAvailabilityStatus.AvailableNow, null);
        if (blocking.Any(x => x.OccupancyEndAt is null))
            return null;

        var end = blocking.Max(x => x.OccupancyEndAt!.Value);
        var civilEnd = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(end, timeZone).DateTime);
        return new(PublicRoomAvailabilityStatus.AvailableSoon, civilEnd.AddDays(1));
    }
}

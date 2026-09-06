using GestaoPredio.Application.Reservations;
using GestaoPredio.Domain.Reservations;

namespace GestaoPredio.UnitTests;

public sealed class ReservationAvailabilityTests
{
    [Theory]
    [InlineData(10, 11, 11, 12, false)]
    [InlineData(10, 12, 11, 13, true)]
    [InlineData(10, 12, 9, 10, false)]
    [InlineData(10, 12, 9, 11, true)]
    public void Uses_half_open_intervals(int firstStart, int firstEnd, int secondStart, int secondEnd, bool expected)
    {
        var day = new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(expected, ReservationAvailability.Overlaps(
            day.AddHours(firstStart), day.AddHours(firstEnd), day.AddHours(secondStart), day.AddHours(secondEnd)));
    }

    [Theory]
    [InlineData(ReservationStatus.Pending, ReservationKind.New, false)]
    [InlineData(ReservationStatus.Approved, ReservationKind.New, true)]
    [InlineData(ReservationStatus.Approved, ReservationKind.Reschedule, true)]
    [InlineData(ReservationStatus.Approved, ReservationKind.Cancellation, false)]
    [InlineData(ReservationStatus.Rejected, ReservationKind.New, false)]
    [InlineData(ReservationStatus.Cancelled, ReservationKind.New, false)]
    public void Only_approved_actual_reservations_block_resources(
        ReservationStatus status, ReservationKind kind, bool expected)
    {
        Assert.Equal(expected, ReservationAvailability.BlocksResources(status, kind));
    }
}

using GestaoPredio.Application.Rooms;
using GestaoPredio.Application.Scheduling;
using GestaoPredio.Domain.Leases;
using GestaoPredio.Domain.Rooms;

namespace GestaoPredio.UnitTests;

public sealed class RoomAvailabilityTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-13T12:00:00Z");
    private static readonly TimeZoneInfo Zone = OperationalTimeZone.Resolve("America/Porto_Velho");

    [Fact]
    public void Empty_leases_are_available_now()
    {
        var result = RoomAvailabilityCalculator.Calculate([], Now, Zone);

        Assert.Equal(PublicRoomAvailabilityStatus.AvailableNow, result!.Status);
        Assert.Null(result.AvailableFrom);
    }

    [Fact]
    public void Consecutive_active_and_scheduled_use_latest_civil_end_plus_one_day()
    {
        var leases = new[]
        {
            Contract(DateTimeOffset.Parse("2026-09-01T04:00:00Z"),
                DateTimeOffset.Parse("2026-10-01T02:00:00Z"), Now),
            Contract(DateTimeOffset.Parse("2026-10-01T04:00:00Z"),
                DateTimeOffset.Parse("2026-11-16T02:00:00Z"), Now)
        };

        var result = RoomAvailabilityCalculator.Calculate(leases, Now, Zone);

        Assert.Equal(PublicRoomAvailabilityStatus.AvailableSoon, result!.Status);
        Assert.Equal(new DateOnly(2026, 11, 16), result.AvailableFrom);
    }

    [Fact]
    public void Active_finite_lease_is_available_soon()
    {
        var result = RoomAvailabilityCalculator.Calculate(
            [Contract(Now.AddDays(-1), DateTimeOffset.Parse("2026-09-16T02:00:00Z"), Now)], Now, Zone);

        Assert.Equal(PublicRoomAvailabilityStatus.AvailableSoon, result!.Status);
        Assert.Equal(new DateOnly(2026, 9, 16), result.AvailableFrom);
    }

    [Fact]
    public void Scheduled_finite_lease_is_available_soon()
    {
        var result = RoomAvailabilityCalculator.Calculate(
            [Contract(Now.AddDays(1), DateTimeOffset.Parse("2026-09-16T02:00:00Z"), Now)], Now, Zone);

        Assert.Equal(PublicRoomAvailabilityStatus.AvailableSoon, result!.Status);
        Assert.Equal(new DateOnly(2026, 9, 16), result.AvailableFrom);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void Indefinite_active_or_scheduled_lease_returns_null(int startOffsetDays)
    {
        var leases = new[]
        {
            Contract(Now.AddDays(-1), Now.AddDays(2), Now),
            Contract(Now.AddDays(startOffsetDays), null, Now)
        };

        var result = RoomAvailabilityCalculator.Calculate(leases, Now, Zone);

        Assert.Null(result);
    }

    [Fact]
    public void Cancelled_ended_and_ending_pending_leases_are_ignored()
    {
        var cancelled = Contract(Now.AddDays(1), Now.AddDays(2), Now);
        cancelled.Cancel(Now);
        var ended = Contract(Now.AddDays(-2), Now.AddDays(2), Now);
        ended.MarkEnded(Now);
        var pending = Contract(Now.AddDays(-2), Now.AddDays(2), Now);
        pending.MarkEndingPending(Now);

        var result = RoomAvailabilityCalculator.Calculate([cancelled, ended, pending], Now, Zone);

        Assert.Equal(PublicRoomAvailabilityStatus.AvailableNow, result!.Status);
        Assert.Null(result.AvailableFrom);
    }

    [Fact]
    public void Lease_ending_at_now_is_ending_pending_and_available_now()
    {
        var lease = Contract(Now.AddDays(-1), Now, Now);

        Assert.Equal(LeaseOperationalStatus.EndingPending, lease.GetOperationalStatus(Now));

        var result = RoomAvailabilityCalculator.Calculate([lease], Now, Zone);

        Assert.Equal(PublicRoomAvailabilityStatus.AvailableNow, result!.Status);
        Assert.Null(result.AvailableFrom);
    }

    [Fact]
    public void Formatter_formats_available_now()
    {
        Assert.Equal("Disponível agora", RoomAvailabilityFormatter.Format(
            PublicRoomAvailabilityStatus.AvailableNow, null));
    }

    [Fact]
    public void Formatter_formats_available_soon()
    {
        Assert.Equal("Disponível em breve — a partir de 16/11/2026", RoomAvailabilityFormatter.Format(
            PublicRoomAvailabilityStatus.AvailableSoon, new DateOnly(2026, 11, 16)));
    }

    [Theory]
    [InlineData(PublicRoomAvailabilityStatus.AvailableNow, "2026-11-16")]
    [InlineData(PublicRoomAvailabilityStatus.AvailableSoon, null)]
    public void Formatter_rejects_invalid_availability_pairs(PublicRoomAvailabilityStatus status, string? date)
    {
        DateOnly? availableFrom = date is null ? null : DateOnly.Parse(date);

        Assert.Throws<ArgumentException>(() =>
        {
            RoomAvailabilityFormatter.Format(status, availableFrom);
        });
    }

    private static Lease Contract(DateTimeOffset start, DateTimeOffset? end, DateTimeOffset now) =>
        Lease.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LeaseMode.Monthly,
            100m, start, 1, start, end, 1, now);
}

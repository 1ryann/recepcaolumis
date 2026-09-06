using GestaoPredio.Application.Availability;
using GestaoPredio.Domain.Availability;

namespace GestaoPredio.UnitTests;

public sealed class OperatingHoursAndRoomBlockTests
{
    private static readonly TimeZoneInfo TimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Porto_Velho");

    [Fact]
    public void Multiple_non_overlapping_intervals_are_valid_and_a_closed_day_has_none()
    {
        var schedule = OperatingHoursSchedule.Create(DateTimeOffset.UtcNow);
        var intervals = OperatingHourInterval.CreateDay(schedule.Id, DayOfWeek.Monday,
        [
            new LocalTimeRange(new TimeOnly(8, 0), new TimeOnly(12, 0)),
            new LocalTimeRange(new TimeOnly(14, 0), new TimeOnly(18, 0))
        ]);

        Assert.Equal(2, intervals.Count);
        Assert.Empty(OperatingHourInterval.CreateDay(schedule.Id, DayOfWeek.Sunday, []));
        Assert.Throws<ArgumentException>(() => OperatingHourInterval.CreateDay(schedule.Id, DayOfWeek.Monday,
        [
            new LocalTimeRange(new TimeOnly(8, 0), new TimeOnly(12, 0)),
            new LocalTimeRange(new TimeOnly(11, 0), new TimeOnly(13, 0))
        ]));
    }

    [Fact]
    public void Evaluator_uses_porto_velho_civil_time_and_rejects_closed_or_gap_periods()
    {
        var scheduleId = Guid.NewGuid();
        var intervals = OperatingHourInterval.CreateDay(scheduleId, DayOfWeek.Monday,
        [
            new LocalTimeRange(new TimeOnly(8, 0), new TimeOnly(12, 0)),
            new LocalTimeRange(new TimeOnly(14, 0), new TimeOnly(18, 0))
        ]);
        var evaluator = new OperatingHoursEvaluator(TimeZone);

        Assert.True(evaluator.Contains(intervals,
            new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 7, 16, 0, 0, TimeSpan.Zero)));
        Assert.False(evaluator.Contains(intervals,
            new DateTimeOffset(2026, 9, 7, 15, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 7, 18, 30, 0, TimeSpan.Zero)));
        Assert.False(evaluator.Contains(intervals,
            new DateTimeOffset(2026, 9, 6, 13, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 6, 14, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Room_block_requires_a_valid_period_reason_and_supports_logical_cancellation()
    {
        var now = DateTimeOffset.UtcNow;
        var block = RoomBlock.Create(Guid.NewGuid(), now.AddHours(1), now.AddHours(2),
            "Manutenção", "actor", now);

        block.Update(now.AddHours(2), now.AddHours(3), "Limpeza", now.AddMinutes(1));
        block.Cancel("actor", now.AddMinutes(2));

        Assert.Equal(RoomBlockStatus.Cancelled, block.Status);
        Assert.NotNull(block.CancelledAt);
        Assert.Throws<ArgumentException>(() => RoomBlock.Create(Guid.NewGuid(), now, now,
            "Motivo", "actor", now));
        Assert.Throws<ArgumentException>(() => RoomBlock.Create(Guid.NewGuid(), now, now.AddHours(1),
            " ", "actor", now));
        Assert.Throws<InvalidOperationException>(() => block.Update(now, now.AddHours(1), "Uso interno", now));
    }
}

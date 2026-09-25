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

    // An occupancy that crosses midnight fits in no daily interval, so Contains always said no and a single
    // month-long lease made every possible schedule invalid (production, 2026-09-25). Such a period is judged
    // by the days it covers instead.
    [Fact]
    public void A_period_crossing_midnight_is_judged_by_the_days_it_covers()
    {
        var scheduleId = Guid.NewGuid();
        var evaluator = new OperatingHoursEvaluator(TimeZone);
        var mondayAndTuesday = OperatingHourInterval.CreateDay(scheduleId, DayOfWeek.Monday,
                [new LocalTimeRange(new TimeOnly(8, 0), new TimeOnly(18, 0))])
            .Concat(OperatingHourInterval.CreateDay(scheduleId, DayOfWeek.Tuesday,
                [new LocalTimeRange(new TimeOnly(8, 0), new TimeOnly(18, 0))])).ToList();
        // Monday 08:00 to Tuesday 08:00 in Porto Velho.
        var start = new DateTimeOffset(2027, 1, 4, 12, 0, 0, TimeSpan.Zero);

        Assert.True(evaluator.CoversPeriod(mondayAndTuesday, start, start.AddDays(1)));
        Assert.False(evaluator.CoversPeriod(mondayAndTuesday, start, start.AddDays(2)));   // Wednesday is closed
        // Inside a single day it is the old rule, to the minute.
        Assert.True(evaluator.CoversPeriod(mondayAndTuesday, start, start.AddHours(10)));
        Assert.False(evaluator.CoversPeriod(mondayAndTuesday, start, start.AddHours(11)));
    }

    [Fact]
    public void An_occupancy_of_a_week_or_more_needs_every_day_open_and_ends_at_midnight_without_the_next_day()
    {
        var scheduleId = Guid.NewGuid();
        var evaluator = new OperatingHoursEvaluator(TimeZone);
        var everyDay = Enum.GetValues<DayOfWeek>().SelectMany(day => OperatingHourInterval.CreateDay(scheduleId, day,
            [new LocalTimeRange(new TimeOnly(8, 0), new TimeOnly(18, 0))])).ToList();
        var withoutSunday = everyDay.Where(interval => interval.DayOfWeek != DayOfWeek.Sunday).ToList();
        var start = new DateTimeOffset(2027, 1, 4, 12, 0, 0, TimeSpan.Zero);

        Assert.True(evaluator.CoversPeriod(everyDay, start, start.AddDays(30)));
        Assert.False(evaluator.CoversPeriod(withoutSunday, start, start.AddDays(30)));
        // Monday 08:00 to Wednesday 00:00 occupies Monday and Tuesday; Wednesday is only the closing instant.
        var mondayAndTuesday = everyDay.Where(interval =>
            interval.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Tuesday).ToList();
        Assert.True(evaluator.CoversPeriod(mondayAndTuesday,
            start, new DateTimeOffset(2027, 1, 6, 4, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Civil_day_is_open_when_it_has_at_least_one_local_interval()
    {
        var evaluator = new OperatingHoursEvaluator(TimeZone);
        var intervals = OperatingHourInterval.CreateDay(OperatingHoursSchedule.SingletonId,
            DayOfWeek.Monday, [new LocalTimeRange(new TimeOnly(8, 0), new TimeOnly(12, 0))]);
        var mondayAtMidnightUtc = new DateTimeOffset(2027, 1, 4, 4, 0, 0, TimeSpan.Zero);

        Assert.True(evaluator.IsCivilDayOpen(intervals, mondayAtMidnightUtc));
        Assert.False(evaluator.IsCivilDayOpen(intervals, mondayAtMidnightUtc.AddDays(1)));
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

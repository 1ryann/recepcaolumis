using GestaoPredio.Application.Availability;
using GestaoPredio.Domain.Availability;
using GestaoPredio.Domain.Professionals;

namespace GestaoPredio.UnitTests;

public sealed class PresenceEvaluatorTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/Porto_Velho"); // UTC-4, no DST

    // 2026-09-07 is a Monday.
    private static DateTimeOffset LocalMonday(int hour, int minute = 0) =>
        new(2026, 9, 7, hour, minute, 0, TimeSpan.FromHours(-4));

    private static OperatingHourInterval[] MondayHours(params (int oh, int om, int ch, int cm)[] ranges) =>
        OperatingHourInterval.CreateDay(OperatingHoursSchedule.SingletonId, DayOfWeek.Monday,
            ranges.Select(r => new LocalTimeRange(new TimeOnly(r.oh, r.om), new TimeOnly(r.ch, r.cm))).ToArray())
            .ToArray();

    [Fact]
    public void Effective_after_personal_availability_but_before_close()
    {
        var presence = ProfessionalPresence.StartByQr(Guid.NewGuid(), LocalMonday(9));
        var hours = MondayHours((8, 0, 18, 30));
        Assert.True(PresenceEvaluator.IsEffective(presence, hours, LocalMonday(15), Zone));
    }

    [Fact]
    public void Not_effective_past_the_last_close()
    {
        var presence = ProfessionalPresence.StartByQr(Guid.NewGuid(), LocalMonday(9));
        var hours = MondayHours((8, 0, 18, 30));
        Assert.False(PresenceEvaluator.IsEffective(presence, hours, LocalMonday(18, 31), Zone));
    }

    [Fact]
    public void Not_effective_on_a_new_civil_day()
    {
        var presence = ProfessionalPresence.StartByQr(Guid.NewGuid(), LocalMonday(9));
        var hours = MondayHours((8, 0, 18, 30));
        Assert.False(PresenceEvaluator.IsEffective(presence, hours, LocalMonday(9).AddDays(1), Zone));
    }

    [Fact]
    public void Fail_closed_when_no_operating_hours_for_the_day()
    {
        var presence = ProfessionalPresence.StartByQr(Guid.NewGuid(), LocalMonday(9));
        Assert.False(PresenceEvaluator.IsEffective(presence, Array.Empty<OperatingHourInterval>(), LocalMonday(12), Zone));
    }

    [Fact]
    public void Not_effective_when_presence_is_null_or_ended()
    {
        var hours = MondayHours((8, 0, 18, 30));
        Assert.False(PresenceEvaluator.IsEffective(null, hours, LocalMonday(12), Zone));

        var ended = ProfessionalPresence.StartByQr(Guid.NewGuid(), LocalMonday(9));
        ended.EndManually("mgr", LocalMonday(11));
        Assert.False(PresenceEvaluator.IsEffective(ended, hours, LocalMonday(12), Zone));
    }

    [Fact]
    public void Gaps_between_intervals_do_not_end_presence()
    {
        var presence = ProfessionalPresence.StartByQr(Guid.NewGuid(), LocalMonday(9));
        var hours = MondayHours((8, 0, 12, 0), (13, 0, 18, 0));
        Assert.True(PresenceEvaluator.IsEffective(presence, hours, LocalMonday(12, 30), Zone));
    }

    [Fact]
    public void Effective_before_the_establishment_opens_when_scanned_early()
    {
        var presence = ProfessionalPresence.StartByQr(Guid.NewGuid(), LocalMonday(6, 50));
        var hours = MondayHours((8, 0, 18, 30));
        Assert.True(PresenceEvaluator.IsEffective(presence, hours, LocalMonday(7, 0), Zone));
    }

    [Fact]
    public void OperatingHoursEndInstant_is_the_local_last_close_in_utc()
    {
        var hours = MondayHours((8, 0, 12, 0), (13, 0, 18, 30));
        var instant = PresenceEvaluator.OperatingHoursEndInstant(new DateOnly(2026, 9, 7), hours, Zone);
        Assert.Equal(new DateTimeOffset(2026, 9, 7, 22, 30, 0, TimeSpan.Zero), instant); // 18:30 -04:00 == 22:30Z
        Assert.Null(PresenceEvaluator.OperatingHoursEndInstant(new DateOnly(2026, 9, 7), Array.Empty<OperatingHourInterval>(), Zone));
    }
}

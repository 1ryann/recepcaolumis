using GestaoPredio.Application.Availability;
using GestaoPredio.Domain.Availability;
using GestaoPredio.Domain.Professionals;

namespace GestaoPredio.UnitTests;

public sealed class ProfessionalAvailabilityEvaluatorTests
{
    private static readonly DateOnly Monday = new(2026, 9, 7);

    [Fact]
    public void Inherit_global_ignores_stored_custom_intervals()
    {
        var global = Global(DayOfWeek.Monday, (8, 0, 18, 0));
        var custom = Custom(DayOfWeek.Monday, (9, 0, 11, 0));

        var result = ProfessionalAvailabilityEvaluator.GetEffectiveRanges(
            ProfessionalAvailabilityMode.InheritGlobal, Monday, global, custom, []);

        Assert.Equal([new ProfessionalLocalTimeRange(new(8, 0), new(18, 0))], result);
    }

    [Fact]
    public void Custom_intersects_global_without_destroying_stored_ranges()
    {
        var global = Global(DayOfWeek.Monday, (8, 0, 12, 0));
        var custom = Custom(DayOfWeek.Monday, (7, 0, 10, 0), (14, 0, 18, 0));

        var result = ProfessionalAvailabilityEvaluator.GetEffectiveRanges(
            ProfessionalAvailabilityMode.Custom, Monday, global, custom, []);

        Assert.Equal([new ProfessionalLocalTimeRange(new(8, 0), new(10, 0))], result);
        Assert.Equal(2, custom.Count);
    }

    [Fact]
    public void All_day_and_partial_exceptions_only_reduce_availability()
    {
        var professionalId = Guid.NewGuid();
        var global = Global(DayOfWeek.Monday, (8, 0, 18, 0));
        var custom = ProfessionalAvailabilityInterval.CreateDay(professionalId, DayOfWeek.Monday,
            [new(new(8, 0), new(18, 0))]);
        var partial = ProfessionalAvailabilityException.Create(professionalId, Monday, false,
            new(12, 0), new(14, 0), null, DateTimeOffset.UtcNow);

        var partialResult = ProfessionalAvailabilityEvaluator.GetEffectiveRanges(
            ProfessionalAvailabilityMode.Custom, Monday, global, custom, [partial]);
        var allDay = ProfessionalAvailabilityException.Create(professionalId, Monday, true,
            null, null, null, DateTimeOffset.UtcNow);
        var allDayResult = ProfessionalAvailabilityEvaluator.GetEffectiveRanges(
            ProfessionalAvailabilityMode.Custom, Monday, global, custom, [allDay]);

        Assert.Equal(
        [
            new ProfessionalLocalTimeRange(new(8, 0), new(12, 0)),
            new ProfessionalLocalTimeRange(new(14, 0), new(18, 0))
        ], partialResult);
        Assert.Empty(allDayResult);
    }

    [Fact]
    public void Contains_requires_one_effective_range_to_cover_the_whole_period()
    {
        ProfessionalLocalTimeRange[] ranges =
        [
            new(new(8, 0), new(12, 0)),
            new(new(14, 0), new(18, 0))
        ];

        Assert.True(ProfessionalAvailabilityEvaluator.Contains(ranges, new(9, 0), new(10, 30)));
        Assert.False(ProfessionalAvailabilityEvaluator.Contains(ranges, new(11, 0), new(15, 0)));
        Assert.False(ProfessionalAvailabilityEvaluator.Contains(ranges, new(10, 0), new(10, 0)));
    }

    private static IReadOnlyList<OperatingHourInterval> Global(DayOfWeek day, params (int Oh, int Om, int Ch, int Cm)[] values) =>
        OperatingHourInterval.CreateDay(OperatingHoursSchedule.SingletonId, day,
            values.Select(value => new LocalTimeRange(new(value.Oh, value.Om), new(value.Ch, value.Cm))).ToArray());

    private static IReadOnlyList<ProfessionalAvailabilityInterval> Custom(DayOfWeek day,
        params (int Sh, int Sm, int Eh, int Em)[] values) =>
        ProfessionalAvailabilityInterval.CreateDay(Guid.NewGuid(), day,
            values.Select(value => new ProfessionalLocalTimeRange(new(value.Sh, value.Sm), new(value.Eh, value.Em))).ToArray());
}

using GestaoPredio.Domain.Professionals;

namespace GestaoPredio.UnitTests;

public sealed class ProfessionalAvailabilityDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Professional_starts_inherit_global_and_switching_mode_does_not_own_interval_deletion()
    {
        var professional = Professional.Create("Ana", "Fisioterapia", "69999999999", Now);

        Assert.Equal(ProfessionalAvailabilityMode.InheritGlobal, professional.AvailabilityMode);

        professional.SetAvailabilityMode(ProfessionalAvailabilityMode.Custom, Now.AddMinutes(1));
        professional.SetAvailabilityMode(ProfessionalAvailabilityMode.InheritGlobal, Now.AddMinutes(2));

        Assert.Equal(ProfessionalAvailabilityMode.InheritGlobal, professional.AvailabilityMode);
        Assert.Equal(Now.AddMinutes(2), professional.UpdatedAt);
    }

    [Fact]
    public void Weekly_day_accepts_split_and_adjacent_ranges()
    {
        var professionalId = Guid.NewGuid();
        var intervals = ProfessionalAvailabilityInterval.CreateDay(professionalId, DayOfWeek.Monday,
        [
            new ProfessionalLocalTimeRange(new TimeOnly(8, 0), new TimeOnly(12, 0)),
            new ProfessionalLocalTimeRange(new TimeOnly(12, 0), new TimeOnly(13, 0)),
            new ProfessionalLocalTimeRange(new TimeOnly(14, 0), new TimeOnly(18, 0))
        ]);

        Assert.Equal(3, intervals.Count);
        Assert.All(intervals, value => Assert.Equal(professionalId, value.ProfessionalId));
        Assert.Equal(new TimeOnly(8, 0), intervals[0].StartTime);
        Assert.Equal(new TimeOnly(18, 0), intervals[2].EndTime);
    }

    [Fact]
    public void Weekly_day_rejects_invalid_overlap_and_duplicate_start()
    {
        var professionalId = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => ProfessionalAvailabilityInterval.CreateDay(professionalId, DayOfWeek.Monday,
            [new ProfessionalLocalTimeRange(new TimeOnly(8, 0), new TimeOnly(8, 0))]));
        Assert.Throws<ArgumentException>(() => ProfessionalAvailabilityInterval.CreateDay(professionalId, DayOfWeek.Monday,
        [
            new ProfessionalLocalTimeRange(new TimeOnly(8, 0), new TimeOnly(12, 0)),
            new ProfessionalLocalTimeRange(new TimeOnly(11, 0), new TimeOnly(13, 0))
        ]));
        Assert.Throws<ArgumentException>(() => ProfessionalAvailabilityInterval.CreateDay(professionalId, DayOfWeek.Monday,
        [
            new ProfessionalLocalTimeRange(new TimeOnly(8, 0), new TimeOnly(12, 0)),
            new ProfessionalLocalTimeRange(new TimeOnly(8, 0), new TimeOnly(10, 0))
        ]));
    }

    [Fact]
    public void Exception_enforces_all_day_or_partial_shape_and_normalizes_reason()
    {
        var professionalId = Guid.NewGuid();
        var allDay = ProfessionalAvailabilityException.Create(professionalId, new DateOnly(2026, 9, 15), true,
            null, null, "  Viagem  ", Now);
        var partial = ProfessionalAvailabilityException.Create(professionalId, new DateOnly(2026, 9, 16), false,
            new TimeOnly(14, 0), new TimeOnly(16, 0), " ", Now);

        Assert.True(allDay.AllDay);
        Assert.Equal("Viagem", allDay.Reason);
        Assert.Null(partial.Reason);
        Assert.Throws<ArgumentException>(() => ProfessionalAvailabilityException.Create(professionalId,
            new DateOnly(2026, 9, 17), true, new TimeOnly(8, 0), null, null, Now));
        Assert.Throws<ArgumentException>(() => ProfessionalAvailabilityException.Create(professionalId,
            new DateOnly(2026, 9, 17), false, new TimeOnly(10, 0), new TimeOnly(9, 0), null, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProfessionalAvailabilityException.Create(professionalId,
            new DateOnly(2026, 9, 17), true, null, null, new string('x', 301), Now));
    }

    [Fact]
    public void Exception_origin_defaults_to_planned_and_can_be_created_as_incident()
    {
        var professionalId = Guid.NewGuid();
        var planned = ProfessionalAvailabilityException.Create(professionalId, new DateOnly(2026, 9, 15), true,
            null, null, null, Now);
        Assert.Equal(ProfessionalAvailabilityExceptionOrigin.Planned, planned.Origin);

        var incident = ProfessionalAvailabilityException.Create(professionalId, new DateOnly(2026, 9, 15), false,
            new TimeOnly(14, 0), new TimeOnly(16, 0), "carro quebrou", Now,
            ProfessionalAvailabilityExceptionOrigin.Incident);
        Assert.Equal(ProfessionalAvailabilityExceptionOrigin.Incident, incident.Origin);

        incident.Update(new DateOnly(2026, 9, 15), false, new TimeOnly(15, 0), new TimeOnly(17, 0), null, Now);
        Assert.Equal(ProfessionalAvailabilityExceptionOrigin.Incident, incident.Origin);
    }
}

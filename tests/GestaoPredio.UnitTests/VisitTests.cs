using GestaoPredio.Domain.Visits;

namespace GestaoPredio.UnitTests;

public sealed class VisitTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Normal_flow_requires_service_to_start_before_ending()
    {
        var visit = Visit.Arrive(Guid.NewGuid(), Guid.NewGuid(), null, "Maria da Silva", "actor", Now);

        Assert.Equal(VisitStatus.Waiting, visit.Status);
        Assert.Throws<InvalidOperationException>(() => visit.End("actor", Now.AddMinutes(5)));
        visit.StartService("actor", Now.AddMinutes(10));
        visit.End("actor", Now.AddMinutes(40));

        Assert.Equal(VisitStatus.Ended, visit.Status);
        Assert.Equal(Now.AddMinutes(10), visit.ServiceStartedAt);
        Assert.Equal(Now.AddMinutes(40), visit.EndedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Waiting_or_in_service_visit_can_be_cancelled(bool startService)
    {
        var visit = Visit.Arrive(Guid.NewGuid(), null, null, "João", "actor", Now);
        if (startService) visit.StartService("actor", Now.AddMinutes(2));

        visit.Cancel("actor", Now.AddMinutes(3));

        Assert.Equal(VisitStatus.Cancelled, visit.Status);
        Assert.Equal(Now.AddMinutes(3), visit.CancelledAt);
        Assert.Throws<InvalidOperationException>(() => visit.StartService("actor", Now.AddMinutes(4)));
    }

    [Fact]
    public void Administrative_correction_requires_reason_and_preserves_a_consistent_current_state()
    {
        var visit = Visit.Arrive(Guid.NewGuid(), null, null, "Ana", "actor", Now);
        visit.StartService("actor", Now.AddMinutes(1));
        visit.End("actor", Now.AddMinutes(2));

        Assert.Throws<ArgumentException>(() =>
            visit.Correct(VisitStatus.InService, " ", "admin", Now.AddMinutes(3)));
        visit.Correct(VisitStatus.InService, "Encerramento lançado por engano", "admin", Now.AddMinutes(3));

        Assert.Equal(VisitStatus.InService, visit.Status);
        Assert.Null(visit.EndedAt);
        Assert.Null(visit.CancelledAt);
        Assert.NotNull(visit.ServiceStartedAt);
    }

    [Fact]
    public void Arrival_validates_only_the_minimum_visitor_identification()
    {
        Assert.Throws<ArgumentException>(() =>
            Visit.Arrive(Guid.NewGuid(), null, null, " ", "actor", Now));
        Assert.Throws<ArgumentException>(() =>
            Visit.Arrive(Guid.Empty, null, null, "Maria", "actor", Now));

        var visit = Visit.Arrive(Guid.NewGuid(), null, null, "  Maria da Silva  ", "actor", Now);
        Assert.Equal("Maria da Silva", visit.VisitorName);
        Assert.Equal(Now, visit.ArrivedAt);
    }
}

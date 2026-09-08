using GestaoPredio.Domain.Professionals;

namespace GestaoPredio.UnitTests;

public sealed class ProfessionalPresenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StartByQr_opens_a_presence()
    {
        var presence = ProfessionalPresence.StartByQr(Guid.NewGuid(), Now);

        Assert.True(presence.IsOpen);
        Assert.Null(presence.EndedAt);
        Assert.Null(presence.EndReason);
        Assert.Equal(PresenceSource.QrSelfScan, presence.Source);
        Assert.Null(presence.CreatedByUserId);
        Assert.Equal(Now, presence.StartedAt);
    }

    [Fact]
    public void StartByManager_records_the_actor()
    {
        var presence = ProfessionalPresence.StartByManager(Guid.NewGuid(), "mgr-1", Now);

        Assert.Equal(PresenceSource.ManagerManual, presence.Source);
        Assert.Equal("mgr-1", presence.CreatedByUserId);
    }

    [Fact]
    public void StartByManager_rejects_empty_actor() =>
        Assert.Throws<ArgumentException>(() => ProfessionalPresence.StartByManager(Guid.NewGuid(), " ", Now));

    [Fact]
    public void StartByQr_rejects_empty_professional() =>
        Assert.Throws<ArgumentException>(() => ProfessionalPresence.StartByQr(Guid.Empty, Now));

    [Fact]
    public void EndManually_sets_the_reason_and_blocks_a_second_end()
    {
        var presence = ProfessionalPresence.StartByQr(Guid.NewGuid(), Now);

        presence.EndManually("mgr-1", Now.AddHours(1));

        Assert.False(presence.IsOpen);
        Assert.Equal(PresenceEndReason.ManagerManual, presence.EndReason);
        Assert.Equal(Now.AddHours(1), presence.EndedAt);
        Assert.Throws<InvalidOperationException>(() => presence.EndManually("mgr-1", Now.AddHours(2)));
    }

    [Fact]
    public void EndForIncident_sets_the_matching_reason()
    {
        var restOfDay = ProfessionalPresence.StartByQr(Guid.NewGuid(), Now);
        restOfDay.EndForIncident(restOfDay: true, Now.AddHours(1));
        Assert.Equal(PresenceEndReason.IncidentRestOfDay, restOfDay.EndReason);

        var untilTime = ProfessionalPresence.StartByQr(Guid.NewGuid(), Now);
        untilTime.EndForIncident(restOfDay: false, Now.AddHours(1));
        Assert.Equal(PresenceEndReason.IncidentUntilTime, untilTime.EndReason);
    }

    [Fact]
    public void MaterialiseOperatingHoursEnd_closes_with_the_elapsed_reason()
    {
        var presence = ProfessionalPresence.StartByQr(Guid.NewGuid(), Now);

        presence.MaterialiseOperatingHoursEnd(Now.AddHours(6));

        Assert.Equal(PresenceEndReason.OperatingHoursElapsed, presence.EndReason);
        Assert.Equal(Now.AddHours(6), presence.EndedAt);
        Assert.Throws<InvalidOperationException>(() => presence.MaterialiseOperatingHoursEnd(Now.AddHours(7)));
    }
}

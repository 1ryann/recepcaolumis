using GestaoPredio.Domain.Customers;

namespace GestaoPredio.UnitTests;

public sealed class TotemBookingHandoffTests
{
    private static byte[] Hash(byte b) => Enumerable.Repeat(b, 32).ToArray();
    private static readonly DateTimeOffset T0 = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    private static TotemBookingHandoff New() =>
        TotemBookingHandoff.Create(Guid.NewGuid(), Hash(1), Hash(2), T0, T0.AddMinutes(5));

    [Fact]
    public void Create_rejects_bad_hashes_and_inverted_window()
    {
        Assert.Throws<ArgumentException>(() => TotemBookingHandoff.Create(Guid.NewGuid(), new byte[8], Hash(2), T0, T0.AddMinutes(5)));
        Assert.Throws<ArgumentException>(() => TotemBookingHandoff.Create(Guid.Empty, Hash(1), Hash(2), T0, T0.AddMinutes(5)));
        Assert.Throws<ArgumentException>(() => TotemBookingHandoff.Create(Guid.NewGuid(), Hash(1), Hash(2), T0, T0));
    }

    [Fact]
    public void MarkStarted_sets_StartedAt_once_and_extends_within_the_hard_ceiling()
    {
        var h = New();
        h.MarkStarted(T0.AddMinutes(4), TimeSpan.FromMinutes(10), T0.AddMinutes(20));
        Assert.Equal(T0.AddMinutes(4), h.StartedAt);
        Assert.Equal(T0.AddMinutes(14), h.ExpiresAt);          // max(5, 4+10) = 14, under the 20 ceiling

        h.MarkStarted(T0.AddMinutes(9), TimeSpan.FromMinutes(10), T0.AddMinutes(20));
        Assert.Equal(T0.AddMinutes(4), h.StartedAt);           // unchanged
        Assert.Equal(T0.AddMinutes(14), h.ExpiresAt);          // not re-extended
    }

    [Fact]
    public void MarkStarted_never_pushes_ExpiresAt_past_the_hard_ceiling()
    {
        var h = TotemBookingHandoff.Create(Guid.NewGuid(), Hash(1), Hash(2), T0, T0.AddMinutes(5));
        h.MarkStarted(T0.AddMinutes(19), TimeSpan.FromMinutes(10), T0.AddMinutes(20));
        Assert.Equal(T0.AddMinutes(20), h.ExpiresAt);
    }

    [Fact]
    public void Complete_moves_to_Completed_and_links_the_reservation()
    {
        var h = New();
        var rid = Guid.NewGuid();
        h.Complete(rid, T0.AddMinutes(3));
        Assert.Equal(TotemBookingHandoffStatus.Completed, h.Status);
        Assert.Equal(rid, h.ReservationId);
        Assert.Equal(T0.AddMinutes(3), h.CompletedAt);
        Assert.Throws<InvalidOperationException>(() => h.Complete(Guid.NewGuid(), T0.AddMinutes(4)));
    }

    [Fact]
    public void MarkExpired_only_from_pending_and_IsUsable_reflects_state_and_clock()
    {
        var h = New();
        Assert.True(h.IsUsable(T0.AddMinutes(1)));
        Assert.False(h.IsUsable(T0.AddMinutes(6)));            // past ExpiresAt
        h.MarkExpired(T0.AddMinutes(6));
        Assert.Equal(TotemBookingHandoffStatus.Expired, h.Status);
        Assert.False(h.IsUsable(T0));
        Assert.Throws<InvalidOperationException>(() => h.MarkExpired(T0.AddMinutes(7)));
    }
}

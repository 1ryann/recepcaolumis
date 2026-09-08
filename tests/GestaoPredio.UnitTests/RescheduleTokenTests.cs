using System.Security.Cryptography;
using GestaoPredio.Domain.Customers;

namespace GestaoPredio.UnitTests;

public sealed class RescheduleTokenTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public void Create_validates_reservation_hash_and_expiry()
    {
        Assert.Throws<ArgumentException>(() => RescheduleToken.Create(Guid.Empty, RandomNumberGenerator.GetBytes(32), Now, Now.AddHours(48)));
        Assert.Throws<ArgumentException>(() => RescheduleToken.Create(Guid.NewGuid(), new byte[16], Now, Now.AddHours(48)));
        Assert.Throws<ArgumentException>(() => RescheduleToken.Create(Guid.NewGuid(), RandomNumberGenerator.GetBytes(32), Now, Now));
    }

    [Fact]
    public void Create_stores_the_48h_window_and_Rotate_resets_state()
    {
        var token = RescheduleToken.Create(Guid.NewGuid(), RandomNumberGenerator.GetBytes(32), Now, Now.AddHours(48));
        Assert.Equal(32, token.TokenHash.Length);

        token.MarkUsed(Now.AddHours(1));
        token.Revoke(Now.AddHours(1));
        var fresh = RandomNumberGenerator.GetBytes(32);
        token.Rotate(fresh, Now.AddHours(2), Now.AddHours(50));

        Assert.Equal(fresh, token.TokenHash);
        Assert.Null(token.UsedAt);
        Assert.Null(token.RevokedAt);
    }
}

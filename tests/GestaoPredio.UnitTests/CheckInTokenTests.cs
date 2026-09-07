using System.Security.Cryptography;
using GestaoPredio.Domain.Customers;

namespace GestaoPredio.UnitTests;

public sealed class CheckInTokenTests
{
    [Fact]
    public void Token_requires_sha256_length_and_tracks_revoke_use()
    {
        var now = DateTimeOffset.UtcNow;
        var token = CheckInToken.Create(Guid.NewGuid(), RandomNumberGenerator.GetBytes(32), now, now.AddHours(1));
        token.Revoke(now.AddMinutes(1));
        token.MarkUsed(now.AddMinutes(2));
        Assert.NotNull(token.RevokedAt);
        Assert.NotNull(token.UsedAt);
        Assert.Equal(32, token.TokenHash.Length);
    }

    [Fact]
    public void Token_rejects_invalid_hash()
    {
        Assert.Throws<ArgumentException>(() => CheckInToken.Create(Guid.NewGuid(), new byte[31], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1)));
    }
}

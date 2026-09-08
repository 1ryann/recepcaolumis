using System.Security.Cryptography;
using GestaoPredio.Domain.Professionals;

namespace GestaoPredio.UnitTests;

public sealed class ProfessionalPresenceTokenTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public void Create_requires_a_32_byte_hash_and_a_forward_expiry()
    {
        Assert.Throws<ArgumentException>(() => ProfessionalPresenceToken.Create(Guid.NewGuid(), new byte[31], Now, Now.AddMinutes(2)));
        Assert.Throws<ArgumentException>(() => ProfessionalPresenceToken.Create(Guid.NewGuid(), RandomNumberGenerator.GetBytes(32), Now, Now));
        Assert.Throws<ArgumentException>(() => ProfessionalPresenceToken.Create(Guid.Empty, RandomNumberGenerator.GetBytes(32), Now, Now.AddMinutes(2)));
    }

    [Fact]
    public void Create_stores_the_hash_and_window()
    {
        var token = ProfessionalPresenceToken.Create(Guid.NewGuid(), RandomNumberGenerator.GetBytes(32), Now, Now.AddSeconds(120));
        Assert.Equal(32, token.TokenHash.Length);
        Assert.Null(token.UsedAt);
        Assert.Null(token.RevokedAt);
        Assert.True(token.ExpiresAt > token.IssuedAt);
    }

    [Fact]
    public void MarkUsed_and_Revoke_track_their_instants()
    {
        var token = ProfessionalPresenceToken.Create(Guid.NewGuid(), RandomNumberGenerator.GetBytes(32), Now, Now.AddSeconds(120));
        token.MarkUsed(Now.AddSeconds(30));
        token.Revoke(Now.AddSeconds(31));
        Assert.NotNull(token.UsedAt);
        Assert.NotNull(token.RevokedAt);
    }

    [Fact]
    public void Rotate_replaces_the_hash_and_clears_used_and_revoked()
    {
        var token = ProfessionalPresenceToken.Create(Guid.NewGuid(), RandomNumberGenerator.GetBytes(32), Now, Now.AddSeconds(120));
        token.MarkUsed(Now.AddSeconds(10));
        token.Revoke(Now.AddSeconds(11));

        var fresh = RandomNumberGenerator.GetBytes(32);
        token.Rotate(fresh, Now.AddMinutes(5), Now.AddMinutes(5).AddSeconds(120));

        Assert.Equal(fresh, token.TokenHash);
        Assert.Null(token.UsedAt);
        Assert.Null(token.RevokedAt);
        Assert.Throws<ArgumentException>(() => token.Rotate(new byte[10], Now, Now.AddMinutes(1)));
    }
}

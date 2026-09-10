using System.Security.Cryptography;
using GestaoPredio.Domain.Customers;
using Xunit;

namespace GestaoPredio.UnitTests;

public sealed class CheckInTokenTests
{
    private static byte[] H() => RandomNumberGenerator.GetBytes(32);

    [Fact]
    public void Create_requires_32_byte_hashes()
    {
        var now = DateTimeOffset.UtcNow;
        var t = CheckInToken.Create(Guid.NewGuid(), H(), H(), now, now.AddHours(1));
        Assert.Equal(32, t.TokenHash.Length);
        Assert.Equal(32, t.ManualCodeHash!.Length);
        Assert.Throws<ArgumentException>(() => CheckInToken.Create(Guid.NewGuid(), new byte[31], H(), now, now.AddHours(1)));
        Assert.Throws<ArgumentException>(() => CheckInToken.Create(Guid.NewGuid(), H(), new byte[10], now, now.AddHours(1)));
    }

    [Fact]
    public void MarkUsed_and_Revoke_null_the_manual_code_hash_but_keep_the_token_hash()
    {
        var now = DateTimeOffset.UtcNow;
        var a = CheckInToken.Create(Guid.NewGuid(), H(), H(), now, now.AddHours(1));
        a.MarkUsed(now.AddMinutes(1));
        Assert.NotNull(a.UsedAt);
        Assert.Null(a.ManualCodeHash);
        Assert.Equal(32, a.TokenHash.Length);

        var b = CheckInToken.Create(Guid.NewGuid(), H(), H(), now, now.AddHours(1));
        b.Revoke(now.AddMinutes(1));
        Assert.NotNull(b.RevokedAt);
        Assert.Null(b.ManualCodeHash);
        Assert.Equal(32, b.TokenHash.Length);
    }

    [Fact]
    public void ClearManualCode_frees_the_hash_without_touching_used_or_revoked()
    {
        var now = DateTimeOffset.UtcNow;
        var t = CheckInToken.Create(Guid.NewGuid(), H(), H(), now, now.AddHours(1));
        t.ClearManualCode();
        Assert.Null(t.ManualCodeHash);
        Assert.Null(t.UsedAt);
        Assert.Null(t.RevokedAt);
    }

    [Fact]
    public void Rotate_replaces_both_hashes_and_clears_state()
    {
        var now = DateTimeOffset.UtcNow;
        var t = CheckInToken.Create(Guid.NewGuid(), H(), H(), now, now.AddHours(1));
        t.MarkUsed(now); t.Revoke(now);
        var t2 = H(); var m2 = H();
        t.Rotate(t2, m2, now.AddMinutes(5), now.AddHours(2));
        Assert.Equal(t2, t.TokenHash);
        Assert.Equal(m2, t.ManualCodeHash);
        Assert.Null(t.UsedAt);
        Assert.Null(t.RevokedAt);
    }
}

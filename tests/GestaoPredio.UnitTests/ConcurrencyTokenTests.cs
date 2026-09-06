using recepcaototem.Features.Common;

namespace GestaoPredio.UnitTests;

public sealed class ConcurrencyTokenTests
{
    [Fact]
    public void PostgreSql_xmin_round_trips_as_opaque_base64()
    {
        const uint value = 0x01020304;

        var encoded = ConcurrencyToken.Encode(value);

        Assert.Equal("AQIDBA==", encoded);
        Assert.True(ConcurrencyToken.TryDecode(encoded, out var decoded));
        Assert.Equal(value, decoded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-base64")]
    [InlineData("AQID")]
    [InlineData("AQIDBAUGBwgJ")]
    public void Missing_malformed_or_wrong_length_token_is_rejected(string? token)
    {
        Assert.False(ConcurrencyToken.TryDecode(token, out var decoded));
        Assert.Equal(0u, decoded);
    }
}

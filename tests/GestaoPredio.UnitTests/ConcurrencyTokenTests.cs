using recepcaototem.Features.Common;

namespace GestaoPredio.UnitTests;

public sealed class ConcurrencyTokenTests
{
    [Fact]
    public void Current_rowversion_round_trips_as_opaque_base64()
    {
        byte[] value = [1, 2, 3, 4, 5, 6, 7, 8];

        var encoded = ConcurrencyToken.Encode(value);

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
        Assert.Empty(decoded);
    }
}

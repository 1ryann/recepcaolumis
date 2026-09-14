using recepcaototem.Features.Rooms;

namespace GestaoPredio.UnitTests;

public sealed class WhatsappLinkBuilderTests
{
    [Fact]
    public void Builder_encodes_exact_message()
    {
        Assert.True(WhatsappLinkBuilder.TryBuild("+5569999999999", "Olá!\nSala: A & B", out var url));

        var uri = new Uri(url);
        Assert.Equal("wa.me", uri.Host);
        Assert.Equal("/5569999999999", uri.AbsolutePath);
        Assert.Equal("Olá!\nSala: A & B", Uri.UnescapeDataString(uri.Query[6..]));
        Assert.DoesNotContain("+5569999999999", url, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    public void Builder_rejects_empty_or_invalid_phone(string configuredPhone)
    {
        Assert.False(WhatsappLinkBuilder.TryBuild(configuredPhone, "Sala A?", out var url));
        Assert.Equal(string.Empty, url);
    }
}

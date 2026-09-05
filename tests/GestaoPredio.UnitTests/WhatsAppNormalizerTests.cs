using GestaoPredio.Domain.Professionals;

namespace GestaoPredio.UnitTests;

public sealed class WhatsAppNormalizerTests
{
    [Theory]
    [InlineData("(11) 98765-4321", "+5511987654321")]
    [InlineData("1133334444", "+551133334444")]
    [InlineData("11.98765-4321", "+5511987654321")]
    [InlineData("+55 (11) 98765-4321", "+5511987654321")]
    [InlineData("+14155552671", "+14155552671")]
    [InlineData("  +442071838750  ", "+442071838750")]
    public void TryNormalize_returns_canonical_e164_for_recognized_numbers(string input, string expected)
    {
        var valid = WhatsAppNormalizer.TryNormalize(input, out var canonical);

        Assert.True(valid);
        Assert.Equal(expected, canonical);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("119876543")]
    [InlineData("442071838750")]
    [InlineData("119876543210")]
    [InlineData("+1234567890123456")]
    [InlineData("+44 2071838750")]
    [InlineData("(11) 98765-4321 ramal 9")]
    [InlineData("11987654321x9")]
    public void TryNormalize_rejects_ambiguous_or_invalid_input(string? input)
    {
        var valid = WhatsAppNormalizer.TryNormalize(input, out var canonical);

        Assert.False(valid);
        Assert.Equal(string.Empty, canonical);
    }
}

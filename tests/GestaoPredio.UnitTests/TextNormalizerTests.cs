using GestaoPredio.Domain.Common;

namespace GestaoPredio.UnitTests;

public sealed class TextNormalizerTests
{
    [Theory]
    [InlineData("  Fisióterapia  ", "FISIOTERAPIA")]
    [InlineData("Fisio\u0301terapia", "FISIOTERAPIA")]
    [InlineData("Sala   01", "SALA 01")]
    [InlineData("  Sala\t\u00A001\r\n", "SALA 01")]
    [InlineData("SALA 01", "SALA 01")]
    [InlineData("straße", "STRASSE")]
    public void Normalize_returns_a_deterministic_key(string input, string expected)
    {
        Assert.Equal(expected, TextNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_is_repeatable_for_equivalent_unicode_forms()
    {
        var composed = TextNormalizer.Normalize("Clínica São José");
        var decomposed = TextNormalizer.Normalize("Cli\u0301nica Sa\u0303o Jose\u0301");

        Assert.Equal("CLINICA SAO JOSE", composed);
        Assert.Equal(composed, decomposed);
        Assert.Equal(composed, TextNormalizer.Normalize("Clínica São José"));
    }

    [Fact]
    public void Normalize_removes_non_spacing_marks_outside_the_basic_multilingual_plane()
    {
        Assert.Equal("A", TextNormalizer.Normalize("A\U0001E944"));
    }
}

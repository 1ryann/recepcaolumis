using System.Security.Cryptography;
using GestaoPredio.Domain.Customers;
using Xunit;

namespace GestaoPredio.UnitTests;

public sealed class ManualCheckInCodeTests
{
    [Fact]
    public void Generate_yields_exactly_six_digits()
    {
        for (var i = 0; i < 10_000; i++)
        {
            var code = ManualCheckInCode.Generate().Value;
            Assert.Equal(6, code.Length);
            Assert.All(code, c => Assert.InRange(c, '0', '9'));
        }
    }

    [Fact]
    public void Generate_preserves_leading_zeros_and_has_reasonable_spread()
    {
        var values = new HashSet<string>();
        var anyLeadingZero = false;
        for (var i = 0; i < 10_000; i++)
        {
            var v = ManualCheckInCode.Generate().Value;
            values.Add(v);
            anyLeadingZero |= v[0] == '0';
        }
        Assert.True(anyLeadingZero, "expected at least one code starting with 0 in 10k draws");
        Assert.True(values.Count > 9_000, "RNG spread sanity");
    }

    [Theory]
    [InlineData("482731", true, "482731")]
    [InlineData("004821", true, "004821")]
    [InlineData(" 482731 ", true, "482731")]
    [InlineData("48273", false, null)]
    [InlineData("4827311", false, null)]
    [InlineData("48a731", false, null)]
    [InlineData("", false, null)]
    [InlineData(null, false, null)]
    public void TryParse_accepts_only_six_ascii_digits(string? raw, bool ok, string? expected)
    {
        var parsed = ManualCheckInCode.TryParse(raw, out var code);
        Assert.Equal(ok, parsed);
        if (ok) Assert.Equal(expected, code.Value);
    }

    [Fact]
    public void Struct_has_no_hashing_member()
    {
        // Guard: hashing is keyed (IManualCheckInCodeHasher). The value object must not
        // expose a Hash()/GetHash()-style method taking no args and returning byte[].
        var offenders = typeof(ManualCheckInCode).GetMethods()
            .Where(m => m.ReturnType == typeof(byte[]) && m.GetParameters().Length == 0);
        Assert.Empty(offenders);
    }
}

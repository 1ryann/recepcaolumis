using System.Security.Cryptography;
using System.Text;
using GestaoPredio.Application.Customers;
using GestaoPredio.Domain.Customers;
using GestaoPredio.Infrastructure.Customers;
using Microsoft.Extensions.Options;
using Xunit;

namespace GestaoPredio.UnitTests;

public sealed class HmacManualCheckInCodeHasherTests
{
    private sealed class Env(string name) : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
    private static HmacManualCheckInCodeHasher Make(string key, string env = "Development") =>
        new(Options.Create(new ManualCheckInCodeHashingOptions { ManualCodeHmacKey = key }), new Env(env));
    private static ManualCheckInCode Code(string v) { ManualCheckInCode.TryParse(v, out var c); return c; }

    [Fact]
    public void Deterministic_for_same_key_and_code()
    {
        var h = Make("k-abc-123");
        Assert.Equal(32, h.Hash(Code("482731")).Length);
        Assert.True(h.Hash(Code("482731")).AsSpan().SequenceEqual(h.Hash(Code("482731"))));
    }

    [Fact]
    public void Different_key_yields_different_hash()
    {
        Assert.False(Make("key-A").Hash(Code("482731")).AsSpan()
            .SequenceEqual(Make("key-B").Hash(Code("482731"))));
    }

    [Fact]
    public void Not_equal_to_plain_sha256()
    {
        var hmac = Make("some-key").Hash(Code("482731"));
        var sha = SHA256.HashData(Encoding.ASCII.GetBytes("482731"));
        Assert.False(hmac.AsSpan().SequenceEqual(sha));
    }

    [Fact]
    public void Leading_zeros_change_the_hash()
    {
        var h = Make("k");
        Assert.False(h.Hash(Code("004821")).AsSpan().SequenceEqual(h.Hash(Code("048210"))));
    }

    [Fact]
    public void Missing_key_in_production_fails_closed()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Make("", "Production"));
        Assert.DoesNotContain("dev-only", ex.Message); // no secret material in the message
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    public void Missing_key_outside_production_uses_explicit_dev_fallback(string env)
    {
        var h = Make("", env);
        Assert.Equal(32, h.Hash(Code("000000")).Length); // does not throw
    }
}

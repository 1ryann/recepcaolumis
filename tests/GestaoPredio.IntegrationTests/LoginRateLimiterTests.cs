using Microsoft.Extensions.Configuration;
using recepcaototem.Features.Auth;

namespace GestaoPredio.IntegrationTests;

public sealed class LoginRateLimiterTests
{
    [Fact]
    public async Task Ip_limit_applies_across_different_email_identifiers()
    {
        using var limiter = Create(ipPermits: 2, identifierPermits: 10);
        using var first = await limiter.AcquireAsync("192.0.2.10", "FIRST@TEST", default);
        using var second = await limiter.AcquireAsync("192.0.2.10", "SECOND@TEST", default);
        using var blocked = await limiter.AcquireAsync("192.0.2.10", "THIRD@TEST", default);
        Assert.True(first.IsAcquired);
        Assert.True(second.IsAcquired);
        Assert.False(blocked.IsAcquired);
    }

    [Fact]
    public async Task Identifier_limit_applies_across_different_ip_addresses()
    {
        using var limiter = Create(ipPermits: 10, identifierPermits: 2);
        using var first = await limiter.AcquireAsync("192.0.2.11", "SAME@TEST", default);
        using var second = await limiter.AcquireAsync("192.0.2.12", "SAME@TEST", default);
        using var blocked = await limiter.AcquireAsync("192.0.2.13", "SAME@TEST", default);
        Assert.True(first.IsAcquired);
        Assert.True(second.IsAcquired);
        Assert.False(blocked.IsAcquired);
    }

    private static LoginRateLimiter Create(int ipPermits, int identifierPermits)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:LoginPermitLimit"] = ipPermits.ToString(),
            ["RateLimiting:LoginIdentifierPermitLimit"] = identifierPermits.ToString(),
            ["RateLimiting:LoginWindowSeconds"] = "60"
        }).Build();
        return new LoginRateLimiter(configuration);
    }
}

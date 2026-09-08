using Microsoft.Extensions.Configuration;
using recepcaototem.Features.ProfessionalRegistrations;

namespace GestaoPredio.UnitTests;

public sealed class ProfessionalRegistrationRateLimiterTests
{
    [Fact]
    public async Task Uses_dedicated_registration_limits()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:ProfessionalRegistrationIpPermitLimit"] = "1",
            ["RateLimiting:ProfessionalRegistrationIdentifierPermitLimit"] = "1",
            ["RateLimiting:ProfessionalRegistrationWindowSeconds"] = "60"
        }).Build();
        using var limiter = new ProfessionalRegistrationRateLimiter(configuration);
        using var first = await limiter.AcquireAsync("127.0.0.1", "applicant@example.test", default);
        using var second = await limiter.AcquireAsync("127.0.0.1", "other@example.test", default);
        Assert.True(first.IsAcquired);
        Assert.False(second.IsAcquired);
    }
}

using Microsoft.Extensions.Configuration;
using recepcaototem.Features.Professionals;

namespace GestaoPredio.UnitTests;

public sealed class ProfessionalPhotoUploadRateLimiterTests
{
    [Fact]
    public async Task AcquireAsync_denies_after_identifier_permit_limit_is_reached()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:PhotoUploadIdentifierPermitLimit"] = "2",
            ["RateLimiting:PhotoUploadIpPermitLimit"] = "50",
            ["RateLimiting:PhotoUploadWindowSeconds"] = "600",
        }).Build();
        using var limiter = new ProfessionalPhotoUploadRateLimiter(configuration);
        (await limiter.AcquireAsync("1.1.1.1", "prof-1", CancellationToken.None)).Dispose();
        (await limiter.AcquireAsync("1.1.1.1", "prof-1", CancellationToken.None)).Dispose();
        var thirdLease = await limiter.AcquireAsync("1.1.1.1", "prof-1", CancellationToken.None);
        Assert.False(thirdLease.IsAcquired);
    }

    [Fact]
    public async Task AcquireAsync_denies_after_ip_permit_limit_is_reached()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:PhotoUploadIdentifierPermitLimit"] = "50",
            ["RateLimiting:PhotoUploadIpPermitLimit"] = "2",
            ["RateLimiting:PhotoUploadWindowSeconds"] = "600",
        }).Build();
        using var limiter = new ProfessionalPhotoUploadRateLimiter(configuration);
        (await limiter.AcquireAsync("2.2.2.2", "prof-a", CancellationToken.None)).Dispose();
        (await limiter.AcquireAsync("2.2.2.2", "prof-b", CancellationToken.None)).Dispose();
        var thirdLease = await limiter.AcquireAsync("2.2.2.2", "prof-c", CancellationToken.None);
        Assert.False(thirdLease.IsAcquired);
    }

    [Fact]
    public async Task AcquireAsync_uses_defaults_when_configuration_is_absent()
    {
        var configuration = new ConfigurationBuilder().Build();
        using var limiter = new ProfessionalPhotoUploadRateLimiter(configuration);
        var lease = await limiter.AcquireAsync("3.3.3.3", "prof-default", CancellationToken.None);
        Assert.True(lease.IsAcquired);
        lease.Dispose();
    }
}

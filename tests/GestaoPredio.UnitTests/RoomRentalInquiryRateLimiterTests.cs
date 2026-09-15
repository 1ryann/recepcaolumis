using Microsoft.Extensions.Configuration;
using recepcaototem.Features.Rooms;

namespace GestaoPredio.UnitTests;

public sealed class RoomRentalInquiryRateLimiterTests
{
    [Fact]
    public async Task AcquireAsync_denies_after_identifier_permit_limit_is_reached()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:RoomRentalInquiryIdentifierPermitLimit"] = "2",
            ["RateLimiting:RoomRentalInquiryIpPermitLimit"] = "50",
            ["RateLimiting:RoomRentalInquiryWindowSeconds"] = "600",
        }).Build();
        using var limiter = new RoomRentalInquiryRateLimiter(configuration);
        (await limiter.AcquireAsync("1.1.1.1", "+5569999999999", CancellationToken.None)).Dispose();
        (await limiter.AcquireAsync("1.1.1.1", "+5569999999999", CancellationToken.None)).Dispose();
        var thirdLease = await limiter.AcquireAsync("1.1.1.1", "+5569999999999", CancellationToken.None);
        Assert.False(thirdLease.IsAcquired);
    }

    [Fact]
    public async Task AcquireAsync_denies_after_ip_permit_limit_is_reached()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:RoomRentalInquiryIdentifierPermitLimit"] = "50",
            ["RateLimiting:RoomRentalInquiryIpPermitLimit"] = "2",
            ["RateLimiting:RoomRentalInquiryWindowSeconds"] = "600",
        }).Build();
        using var limiter = new RoomRentalInquiryRateLimiter(configuration);
        (await limiter.AcquireAsync("2.2.2.2", "+5569999990001", CancellationToken.None)).Dispose();
        (await limiter.AcquireAsync("2.2.2.2", "+5569999990002", CancellationToken.None)).Dispose();
        var thirdLease = await limiter.AcquireAsync("2.2.2.2", "+5569999990003", CancellationToken.None);
        Assert.False(thirdLease.IsAcquired);
    }

    [Fact]
    public async Task AcquireAsync_uses_default_ip_limit_of_eight_when_configuration_is_absent()
    {
        var configuration = new ConfigurationBuilder().Build();
        using var limiter = new RoomRentalInquiryRateLimiter(configuration);
        for (var i = 0; i < 8; i++)
            Assert.True((await limiter.AcquireAsync("3.3.3.3", $"+556999900{i:00}0", CancellationToken.None)).IsAcquired);
        Assert.False((await limiter.AcquireAsync("3.3.3.3", "+5569999009090", CancellationToken.None)).IsAcquired);
    }

    [Fact]
    public async Task AcquireAsync_uses_default_identifier_limit_of_three_when_configuration_is_absent()
    {
        var configuration = new ConfigurationBuilder().Build();
        using var limiter = new RoomRentalInquiryRateLimiter(configuration);
        for (var i = 0; i < 3; i++)
            Assert.True((await limiter.AcquireAsync($"4.4.4.{i}", "+5569999999999", CancellationToken.None)).IsAcquired);
        Assert.False((await limiter.AcquireAsync("4.4.4.9", "+5569999999999", CancellationToken.None)).IsAcquired);
    }

    [Fact]
    public async Task AcquireAsync_treats_non_positive_configured_limits_as_one()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:RoomRentalInquiryIpPermitLimit"] = "0",
            ["RateLimiting:RoomRentalInquiryIdentifierPermitLimit"] = "-5",
            ["RateLimiting:RoomRentalInquiryWindowSeconds"] = "600",
        }).Build();
        using var limiter = new RoomRentalInquiryRateLimiter(configuration);
        Assert.True((await limiter.AcquireAsync("5.5.5.5", "+5569999990001", CancellationToken.None)).IsAcquired);
        Assert.False((await limiter.AcquireAsync("5.5.5.5", "+5569999990002", CancellationToken.None)).IsAcquired);
    }

    [Fact]
    public async Task AcquireAsync_window_renews_after_it_elapses()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:RoomRentalInquiryIpPermitLimit"] = "1",
            ["RateLimiting:RoomRentalInquiryIdentifierPermitLimit"] = "50",
            ["RateLimiting:RoomRentalInquiryWindowSeconds"] = "1",
        }).Build();
        using var limiter = new RoomRentalInquiryRateLimiter(configuration);
        Assert.True((await limiter.AcquireAsync("6.6.6.6", "+5569999990001", CancellationToken.None)).IsAcquired);
        Assert.False((await limiter.AcquireAsync("6.6.6.6", "+5569999990002", CancellationToken.None)).IsAcquired);
        await Task.Delay(TimeSpan.FromSeconds(1.2));
        Assert.True((await limiter.AcquireAsync("6.6.6.6", "+5569999990003", CancellationToken.None)).IsAcquired);
    }
}

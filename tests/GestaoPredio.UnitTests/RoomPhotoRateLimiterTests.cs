using Microsoft.Extensions.Configuration;
using recepcaototem.Features.Totem;

namespace GestaoPredio.UnitTests;

public sealed class RoomPhotoRateLimiterTests
{
    [Fact]
    public async Task AcquireAsync_denies_after_identifier_permit_limit_is_reached()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:RoomPhotoIdentifierPermitLimit"] = "2",
            ["RateLimiting:RoomPhotoIpPermitLimit"] = "50",
            ["RateLimiting:RoomPhotoWindowSeconds"] = "600",
        }).Build();
        using var limiter = new RoomPhotoRateLimiter(configuration);
        (await limiter.AcquireAsync("1.1.1.1", "room-photo:r1:p1", CancellationToken.None)).Dispose();
        (await limiter.AcquireAsync("1.1.1.1", "room-photo:r1:p1", CancellationToken.None)).Dispose();
        var thirdLease = await limiter.AcquireAsync("1.1.1.1", "room-photo:r1:p1", CancellationToken.None);
        Assert.False(thirdLease.IsAcquired);
    }

    [Fact]
    public async Task AcquireAsync_denies_after_ip_permit_limit_is_reached()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:RoomPhotoIdentifierPermitLimit"] = "50",
            ["RateLimiting:RoomPhotoIpPermitLimit"] = "2",
            ["RateLimiting:RoomPhotoWindowSeconds"] = "600",
        }).Build();
        using var limiter = new RoomPhotoRateLimiter(configuration);
        (await limiter.AcquireAsync("2.2.2.2", "room-photo:r1:p1", CancellationToken.None)).Dispose();
        (await limiter.AcquireAsync("2.2.2.2", "room-photo:r1:p2", CancellationToken.None)).Dispose();
        var thirdLease = await limiter.AcquireAsync("2.2.2.2", "room-photo:r1:p3", CancellationToken.None);
        Assert.False(thirdLease.IsAcquired);
    }

    [Fact]
    public async Task AcquireAsync_uses_default_ip_limit_of_ninety_when_configuration_is_absent()
    {
        var configuration = new ConfigurationBuilder().Build();
        using var limiter = new RoomPhotoRateLimiter(configuration);
        for (var i = 0; i < 90; i++)
            Assert.True((await limiter.AcquireAsync("3.3.3.3", $"room-photo:r1:p{i}", CancellationToken.None)).IsAcquired);
        Assert.False((await limiter.AcquireAsync("3.3.3.3", "room-photo:r1:p9000", CancellationToken.None)).IsAcquired);
    }

    [Fact]
    public async Task AcquireAsync_uses_default_identifier_limit_of_ninety_when_configuration_is_absent()
    {
        var configuration = new ConfigurationBuilder().Build();
        using var limiter = new RoomPhotoRateLimiter(configuration);
        for (var i = 0; i < 90; i++)
            Assert.True((await limiter.AcquireAsync($"4.4.4.{i}", "room-photo:r1:p1", CancellationToken.None)).IsAcquired);
        Assert.False((await limiter.AcquireAsync("4.4.4.9", "room-photo:r1:p1", CancellationToken.None)).IsAcquired);
    }

    [Fact]
    public async Task AcquireAsync_treats_non_positive_configured_limits_as_one()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:RoomPhotoIpPermitLimit"] = "0",
            ["RateLimiting:RoomPhotoIdentifierPermitLimit"] = "-5",
            ["RateLimiting:RoomPhotoWindowSeconds"] = "600",
        }).Build();
        using var limiter = new RoomPhotoRateLimiter(configuration);
        Assert.True((await limiter.AcquireAsync("5.5.5.5", "room-photo:r1:p1", CancellationToken.None)).IsAcquired);
        Assert.False((await limiter.AcquireAsync("5.5.5.5", "room-photo:r1:p2", CancellationToken.None)).IsAcquired);
    }

    [Fact]
    public async Task AcquireAsync_window_renews_after_it_elapses()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:RoomPhotoIpPermitLimit"] = "1",
            ["RateLimiting:RoomPhotoIdentifierPermitLimit"] = "50",
            ["RateLimiting:RoomPhotoWindowSeconds"] = "1",
        }).Build();
        using var limiter = new RoomPhotoRateLimiter(configuration);
        Assert.True((await limiter.AcquireAsync("6.6.6.6", "room-photo:r1:p1", CancellationToken.None)).IsAcquired);
        Assert.False((await limiter.AcquireAsync("6.6.6.6", "room-photo:r1:p2", CancellationToken.None)).IsAcquired);
        await Task.Delay(TimeSpan.FromSeconds(1.2));
        Assert.True((await limiter.AcquireAsync("6.6.6.6", "room-photo:r1:p3", CancellationToken.None)).IsAcquired);
    }
}

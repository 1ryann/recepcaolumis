using GestaoPredio.Application.AccessControl;
using GestaoPredio.Infrastructure.AccessControl;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace GestaoPredio.UnitTests;

public sealed class AccessControlProviderTests
{
    private static AccessControlReleaseRequest Request =>
        new("MAIN_ENTRANCE", null, "actor-id");

    [Fact]
    public async Task Demo_provider_accepts_without_external_call()
    {
        var recorder = new AccessControlDemoRecorder();
        var provider = new DemoAccessControlService(recorder);

        var result = await provider.ReleaseDoorAsync(Request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.CommandAccepted);
        Assert.Equal("Demo", result.Provider);
        Assert.Null(result.FailureCode);
        var attempt = Assert.Single(recorder.Attempts);
        Assert.Equal("MAIN_ENTRANCE", attempt.DoorId);
        Assert.True(attempt.Success);
    }

    [Fact]
    public async Task Demo_provider_can_return_a_controlled_failure()
    {
        var recorder = new AccessControlDemoRecorder { ForceFailure = true };
        var provider = new DemoAccessControlService(recorder);

        var result = await provider.ReleaseDoorAsync(Request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.CommandAccepted);
        Assert.Equal("DEMO_ACCESS_FAILURE", result.FailureCode);
        Assert.False(Assert.Single(recorder.Attempts).Success);
    }

    [Fact]
    public async Task Demo_provider_honors_cancellation_without_sleeping()
    {
        var recorder = new AccessControlDemoRecorder { ForceCancellation = true };
        var provider = new DemoAccessControlService(recorder);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            provider.ReleaseDoorAsync(Request, CancellationToken.None));
        Assert.Empty(recorder.Attempts);
    }

    [Fact]
    public async Task Orchestrator_sanitizes_provider_cancellation()
    {
        var recorder = new AccessControlDemoRecorder { ForceCancellation = true };
        var provider = new DemoAccessControlService(recorder);
        var service = new AccessControlService(provider, NullLogger<AccessControlService>.Instance,
            new ConfigurationBuilder().Build());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await service.ReleaseDoorAsync(Request, cancellation.Token);

        Assert.False(result.Success);
        Assert.Equal("ACCESS_CONTROL_TIMEOUT", result.FailureCode);
    }

    [Fact]
    public async Task Intelbras_provider_fails_closed_when_not_configured()
    {
        var configuration = new ConfigurationBuilder().Build();
        var provider = new IntelbrasAccessControlService(configuration);

        var result = await provider.ReleaseDoorAsync(Request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.CommandAccepted);
        Assert.Equal("Intelbras", result.Provider);
        Assert.Equal("INTELBRAS_NOT_CONFIGURED", result.FailureCode);
    }

    [Fact]
    public void Cooldown_is_per_door_and_blocks_immediate_repeat()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AccessControl:CooldownMilliseconds"] = "1000"
            })
            .Build();
        var cooldown = new AccessControlCooldown(configuration);
        var now = DateTimeOffset.UtcNow;

        Assert.True(cooldown.TryAcquire("MAIN_ENTRANCE", now));
        Assert.False(cooldown.TryAcquire("MAIN_ENTRANCE", now.AddMilliseconds(500)));
        Assert.True(cooldown.TryAcquire("SIDE_ENTRANCE", now.AddMilliseconds(500)));
        Assert.True(cooldown.TryAcquire("MAIN_ENTRANCE", now.AddMilliseconds(1001)));
    }
}

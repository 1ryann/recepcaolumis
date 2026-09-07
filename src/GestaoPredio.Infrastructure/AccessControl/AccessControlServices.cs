using System.Collections.Concurrent;
using GestaoPredio.Application.AccessControl;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GestaoPredio.Infrastructure.AccessControl;

public sealed record DemoAccessControlAttempt(
    string DoorId,
    string Provider,
    bool Success,
    string? FailureCode);

public sealed class AccessControlDemoRecorder
{
    private readonly ConcurrentQueue<DemoAccessControlAttempt> attempts = new();

    public bool ForceFailure { get; set; }
    public bool ForceCancellation { get; set; }
    public IReadOnlyList<DemoAccessControlAttempt> Attempts => attempts.ToArray();

    internal void Record(DemoAccessControlAttempt attempt) => attempts.Enqueue(attempt);

    public void Clear()
    {
        ForceFailure = false;
        ForceCancellation = false;
        while (attempts.TryDequeue(out _)) { }
    }
}

public sealed class DemoAccessControlService(
    AccessControlDemoRecorder recorder) : IAccessControlProvider
{
    public const string ProviderName = "Demo";
    public string Name => ProviderName;

    public Task<AccessControlResult> ReleaseDoorAsync(
        AccessControlReleaseRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (recorder.ForceCancellation)
            throw new OperationCanceledException(cancellationToken);

        var result = recorder.ForceFailure
            ? AccessControlResult.Failed(Name, "DEMO_ACCESS_FAILURE")
            : AccessControlResult.Accepted(Name);
        recorder.Record(new DemoAccessControlAttempt(request.DoorId, Name, result.Success, result.FailureCode));
        return Task.FromResult(result);
    }
}

public sealed class IntelbrasAccessControlService(
    IConfiguration configuration) : IAccessControlProvider
{
    public const string ProviderName = "Intelbras";
    public string Name => ProviderName;

    public Task<AccessControlResult> ReleaseDoorAsync(
        AccessControlReleaseRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var configured = !string.IsNullOrWhiteSpace(configuration["AccessControl:Intelbras:Model"])
            && !string.IsNullOrWhiteSpace(configuration["AccessControl:Intelbras:ControllerId"])
            && !string.IsNullOrWhiteSpace(configuration["AccessControl:Intelbras:CredentialReference"]);
        return Task.FromResult(AccessControlResult.Failed(Name,
            configured ? "INTELBRAS_PROTOCOL_UNSUPPORTED" : "INTELBRAS_NOT_CONFIGURED"));
    }
}

public sealed class AccessControlCooldown(IConfiguration configuration)
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> lastRelease = new(StringComparer.Ordinal);
    private readonly TimeSpan cooldown = TimeSpan.FromMilliseconds(
        Math.Clamp(configuration.GetValue("AccessControl:CooldownMilliseconds", 3000), 250, 60000));

    public bool TryAcquire(string doorId, DateTimeOffset now)
    {
        while (true)
        {
            if (lastRelease.TryGetValue(doorId, out var previous))
            {
                if (now - previous < cooldown) return false;
                if (lastRelease.TryUpdate(doorId, now, previous)) return true;
            }
            else if (lastRelease.TryAdd(doorId, now)) return true;
        }
    }

    public void Clear() => lastRelease.Clear();
}

public sealed class AccessControlService(
    IAccessControlProvider provider,
    ILogger<AccessControlService> logger,
    IConfiguration configuration) : IAccessControlService
{
    private readonly TimeSpan timeout = TimeSpan.FromMilliseconds(
        Math.Clamp(configuration.GetValue("AccessControl:TimeoutMilliseconds", 5000), 250, 30000));

    public async Task<AccessControlResult> ReleaseDoorAsync(
        AccessControlReleaseRequest request,
        CancellationToken cancellationToken)
    {
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(timeout);
        try
        {
            var result = await provider.ReleaseDoorAsync(request, timeoutCancellation.Token);
            if (!result.Success)
                logger.LogWarning("Access-control release failed. Provider: {Provider}; DoorId: {DoorId}; FailureCode: {FailureCode}",
                    result.Provider, request.DoorId, result.FailureCode);
            return result;
        }
        catch (OperationCanceledException)
        {
            var result = AccessControlResult.Failed(provider.Name, "ACCESS_CONTROL_TIMEOUT");
            logger.LogWarning("Access-control release cancelled. Provider: {Provider}; DoorId: {DoorId}; FailureCode: {FailureCode}",
                result.Provider, request.DoorId, result.FailureCode);
            return result;
        }
        catch
        {
            var result = AccessControlResult.Failed(provider.Name, "ACCESS_CONTROL_FAILURE");
            logger.LogWarning("Access-control release failed. Provider: {Provider}; DoorId: {DoorId}; FailureCode: {FailureCode}",
                result.Provider, request.DoorId, result.FailureCode);
            return result;
        }
    }
}

public static class AccessControlServiceCollectionExtensions
{
    public static IServiceCollection AddLumisAccessControl(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        var provider = configuration["AccessControl:Provider"];
        if (string.IsNullOrWhiteSpace(provider))
            provider = environment.IsDevelopment() || environment.EnvironmentName == "Testing" ? "Demo" : "Intelbras";

        if (provider.Equals("Demo", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<AccessControlDemoRecorder>();
            services.AddSingleton<DemoAccessControlService>();
            services.AddSingleton<IAccessControlProvider>(sp => sp.GetRequiredService<DemoAccessControlService>());
        }
        else if (provider.Equals("Intelbras", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IntelbrasAccessControlService>();
            services.AddSingleton<IAccessControlProvider>(sp => sp.GetRequiredService<IntelbrasAccessControlService>());
        }
        else
        {
            throw new InvalidOperationException("AccessControl:Provider must be Demo or Intelbras.");
        }

        services.AddSingleton<AccessControlCooldown>();
        services.AddScoped<IAccessControlService, AccessControlService>();
        return services;
    }
}

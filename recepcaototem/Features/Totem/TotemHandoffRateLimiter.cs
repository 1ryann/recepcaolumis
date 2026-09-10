using System.Threading.RateLimiting;

namespace recepcaototem.Features.Totem;

/// <summary>
/// Dedicated fixed-window limiter for the anonymous booking-handoff surface. Each bucket
/// (<c>create</c>, <c>status</c>, <c>cancel</c>, <c>claim</c>, <c>resolve</c>) gets its own
/// budget so a kiosk polling <c>status</c> can never starve <c>create</c> and vice-versa.
/// Mirrors <see cref="recepcaototem.Features.Professionals.ProfessionalPresenceRateLimiter"/>
/// (fixed window, <c>QueueLimit = 0</c>, <c>AutoReplenishment = true</c>).
/// </summary>
public sealed class TotemHandoffRateLimiter : IDisposable
{
    private readonly Dictionary<string, PartitionedRateLimiter<string>> _buckets;

    public TotemHandoffRateLimiter(IConfiguration configuration)
    {
        var window = configuration.GetValue("RateLimiting:HandoffWindowSeconds", 60);
        _buckets = new()
        {
            ["create"] = Make(configuration.GetValue("RateLimiting:HandoffCreateIpPermitLimit", 10), window),
            ["status"] = Make(configuration.GetValue("RateLimiting:HandoffStatusPermitLimit", 50), window),
            ["cancel"] = Make(configuration.GetValue("RateLimiting:HandoffCancelIpPermitLimit", 15), window),
            ["claim"] = Make(configuration.GetValue("RateLimiting:HandoffClaimIpPermitLimit", 20), window),
            ["resolve"] = Make(configuration.GetValue("RateLimiting:HandoffResolveIpPermitLimit", 20), window),
        };
    }

    public async ValueTask<TotemHandoffRateLimitLease> AcquireAsync(string partitionKey, string bucket, CancellationToken ct)
        => new(await _buckets[bucket].AcquireAsync(partitionKey, 1, ct));

    private static PartitionedRateLimiter<string> Make(int permits, int seconds) =>
        PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetFixedWindowLimiter(key,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, permits),
                Window = TimeSpan.FromSeconds(Math.Max(1, seconds)),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    public void Dispose()
    {
        foreach (var bucket in _buckets.Values) bucket.Dispose();
    }
}

public sealed class TotemHandoffRateLimitLease(RateLimitLease lease) : IDisposable
{
    public bool IsAcquired => lease.IsAcquired;
    public void Dispose() => lease.Dispose();
}

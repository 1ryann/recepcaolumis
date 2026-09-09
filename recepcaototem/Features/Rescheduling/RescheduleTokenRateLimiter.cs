using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;

namespace recepcaototem.Features.Rescheduling;

/// <summary>
/// Per-IP + per-token fixed-window limiter for the public rescheduling surface.
/// Mirrors <c>CustomerPublicRateLimiter</c>.
/// </summary>
public sealed class RescheduleTokenRateLimiter(IConfiguration configuration) : IDisposable
{
    private readonly PartitionedRateLimiter<string> ip = Create(
        configuration.GetValue("RateLimiting:RescheduleIpPermitLimit", 30),
        configuration.GetValue("RateLimiting:RescheduleWindowSeconds", 60));
    private readonly PartitionedRateLimiter<string> identifier = Create(
        configuration.GetValue("RateLimiting:RescheduleIdentifierPermitLimit", 15),
        configuration.GetValue("RateLimiting:RescheduleWindowSeconds", 60));

    public async ValueTask<RescheduleTokenRateLimitLease> AcquireAsync(string ipAddress, string identifierValue, CancellationToken ct)
    {
        var ipLease = await ip.AcquireAsync(ipAddress, 1, ct);
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identifierValue ?? string.Empty)));
        var idLease = await identifier.AcquireAsync(id, 1, ct);
        return new RescheduleTokenRateLimitLease(ipLease, idLease);
    }

    private static PartitionedRateLimiter<string> Create(int permits, int seconds) =>
        PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetFixedWindowLimiter(key,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, permits),
                Window = TimeSpan.FromSeconds(Math.Max(1, seconds)),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    public void Dispose() { ip.Dispose(); identifier.Dispose(); }
}

public sealed class RescheduleTokenRateLimitLease(RateLimitLease ipLease, RateLimitLease identifierLease) : IDisposable
{
    public bool IsAcquired => ipLease.IsAcquired && identifierLease.IsAcquired;
    public void Dispose() { ipLease.Dispose(); identifierLease.Dispose(); }
}

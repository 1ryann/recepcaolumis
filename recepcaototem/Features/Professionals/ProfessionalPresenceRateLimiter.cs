using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;

namespace recepcaototem.Features.Professionals;

/// <summary>
/// Per-IP + per-token fixed-window limiter for the public presence surface
/// (Totem presence confirmation). Mirrors <c>CustomerPublicRateLimiter</c>.
/// </summary>
public sealed class ProfessionalPresenceRateLimiter(IConfiguration configuration) : IDisposable
{
    private readonly PartitionedRateLimiter<string> ip = Create(
        configuration.GetValue("RateLimiting:PresenceIpPermitLimit", 30),
        configuration.GetValue("RateLimiting:PresenceWindowSeconds", 60));
    private readonly PartitionedRateLimiter<string> identifier = Create(
        configuration.GetValue("RateLimiting:PresenceIdentifierPermitLimit", 15),
        configuration.GetValue("RateLimiting:PresenceWindowSeconds", 60));

    public async ValueTask<ProfessionalPresenceRateLimitLease> AcquireAsync(string ipAddress, string identifierValue, CancellationToken ct)
    {
        var ipLease = await ip.AcquireAsync(ipAddress, 1, ct);
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identifierValue ?? string.Empty)));
        var idLease = await identifier.AcquireAsync(id, 1, ct);
        return new ProfessionalPresenceRateLimitLease(ipLease, idLease);
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

public sealed class ProfessionalPresenceRateLimitLease(RateLimitLease ipLease, RateLimitLease identifierLease) : IDisposable
{
    public bool IsAcquired => ipLease.IsAcquired && identifierLease.IsAcquired;
    public void Dispose() { ipLease.Dispose(); identifierLease.Dispose(); }
}

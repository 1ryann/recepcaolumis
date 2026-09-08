using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using recepcaototem.Features.Customers;

namespace recepcaototem.Features.ProfessionalRegistrations;

public sealed class ProfessionalRegistrationRateLimiter(IConfiguration configuration) : IDisposable
{
    private readonly PartitionedRateLimiter<string> ip = Create(
        configuration.GetValue("RateLimiting:ProfessionalRegistrationIpPermitLimit", 10),
        configuration.GetValue("RateLimiting:ProfessionalRegistrationWindowSeconds", 60));
    private readonly PartitionedRateLimiter<string> identifier = Create(
        configuration.GetValue("RateLimiting:ProfessionalRegistrationIdentifierPermitLimit", 5),
        configuration.GetValue("RateLimiting:ProfessionalRegistrationWindowSeconds", 60));

    public async ValueTask<CustomerPublicRateLimitLease> AcquireAsync(string ipAddress, string identifierValue, CancellationToken ct)
    {
        var ipLease = await ip.AcquireAsync(ipAddress, 1, ct);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identifierValue ?? string.Empty)));
        var identifierLease = await identifier.AcquireAsync(key, 1, ct);
        return new CustomerPublicRateLimitLease(ipLease, identifierLease);
    }

    private static PartitionedRateLimiter<string> Create(int permits, int seconds) =>
        PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetFixedWindowLimiter(key, _ =>
            new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, permits), Window = TimeSpan.FromSeconds(Math.Max(1, seconds)), QueueLimit = 0, AutoReplenishment = true }));

    public void Dispose() { ip.Dispose(); identifier.Dispose(); }
}

using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;

namespace recepcaototem.Features.Auth;

public sealed class LoginRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _ipLimiter;
    private readonly PartitionedRateLimiter<string> _identifierLimiter;

    public LoginRateLimiter(IConfiguration configuration)
    {
        var windowSeconds = configuration.GetValue("RateLimiting:LoginWindowSeconds", 60);
        var ipPermits = configuration.GetValue("RateLimiting:LoginPermitLimit", 10);
        var identifierPermits = configuration.GetValue("RateLimiting:LoginIdentifierPermitLimit", 5);
        if (windowSeconds < 1 || ipPermits < 1 || identifierPermits < 1)
            throw new InvalidOperationException("Invalid login rate limiting configuration.");

        _ipLimiter = Create(ipPermits, windowSeconds);
        _identifierLimiter = Create(identifierPermits, windowSeconds);
    }

    public async ValueTask<LoginRateLimitLease> AcquireAsync(string ipAddress, string normalizedEmail, CancellationToken cancellationToken)
    {
        var ipLease = await _ipLimiter.AcquireAsync(ipAddress, 1, cancellationToken);
        var identifier = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEmail)));
        var identifierLease = await _identifierLimiter.AcquireAsync(identifier, 1, cancellationToken);
        return new LoginRateLimitLease(ipLease, identifierLease);
    }

    private static PartitionedRateLimiter<string> Create(int permits, int windowSeconds) =>
        PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetFixedWindowLimiter(key, _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = TimeSpan.FromSeconds(windowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    public void Dispose()
    {
        _ipLimiter.Dispose();
        _identifierLimiter.Dispose();
    }
}

public sealed class LoginRateLimitLease(RateLimitLease ipLease, RateLimitLease identifierLease) : IDisposable
{
    public bool IsAcquired => ipLease.IsAcquired && identifierLease.IsAcquired;
    public void Dispose()
    {
        ipLease.Dispose();
        identifierLease.Dispose();
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;

namespace recepcaototem.Features.Rooms;

/// <summary>
/// Per-IP + per-WhatsApp fixed-window limiter for the public rental inquiry write surface
/// (<c>POST /api/totem/rooms/{roomId}/rental-inquiries</c>). A dedicated limiter rather than reusing
/// <see cref="recepcaototem.Features.Customers.CustomerPublicRateLimiter"/> directly — the same reasoning
/// that led to a dedicated <c>ProfessionalPhotoUploadRateLimiter</c>: this is a public write and deserves
/// its own, more restrictive budget than public reads.
/// </summary>
public sealed class RoomRentalInquiryRateLimiter(IConfiguration configuration) : IDisposable
{
    private readonly PartitionedRateLimiter<string> ip = Create(
        configuration.GetValue("RateLimiting:RoomRentalInquiryIpPermitLimit", 8),
        configuration.GetValue("RateLimiting:RoomRentalInquiryWindowSeconds", 600));
    private readonly PartitionedRateLimiter<string> identifier = Create(
        configuration.GetValue("RateLimiting:RoomRentalInquiryIdentifierPermitLimit", 3),
        configuration.GetValue("RateLimiting:RoomRentalInquiryWindowSeconds", 600));

    public async ValueTask<RoomRentalInquiryRateLimitLease> AcquireAsync(string ipAddress, string identifierValue, CancellationToken ct)
    {
        var ipLease = await ip.AcquireAsync(ipAddress, 1, ct);
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identifierValue ?? string.Empty)));
        var idLease = await identifier.AcquireAsync(id, 1, ct);
        return new RoomRentalInquiryRateLimitLease(ipLease, idLease);
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

public sealed class RoomRentalInquiryRateLimitLease(RateLimitLease ipLease, RateLimitLease identifierLease) : IDisposable
{
    public bool IsAcquired => ipLease.IsAcquired && identifierLease.IsAcquired;
    public void Dispose() { ipLease.Dispose(); identifierLease.Dispose(); }
}

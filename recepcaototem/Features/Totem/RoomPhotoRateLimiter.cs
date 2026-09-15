using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;

namespace recepcaototem.Features.Totem;

/// <summary>
/// Per-IP + per-photo fixed-window limiter for the public room-photo-bytes surface
/// (<c>GET /api/totem/rooms/{roomId}/photos/{photoId}</c>). A dedicated limiter rather than reusing
/// <see cref="recepcaototem.Features.Customers.CustomerPublicRateLimiter"/> — that limiter's IP bucket is shared
/// across every public Totem read (catalog list, room detail, and every room's photos alike), so a single visitor
/// opening one room with a full 8-photo gallery already spends 9 permits (1 detail JSON + 8 photo bytes) from the
/// same budget the catalog itself draws from, self-throttling normal browsing. Static image bytes are not a
/// write/abuse-sensitive surface the way a public POST is (see <see cref="recepcaototem.Features.Rooms.RoomRentalInquiryRateLimiter"/>),
/// so this budget is sized generously for gallery browsing rather than restrictively like a write endpoint.
/// </summary>
public sealed class RoomPhotoRateLimiter(IConfiguration configuration) : IDisposable
{
    private readonly PartitionedRateLimiter<string> ip = Create(
        configuration.GetValue("RateLimiting:RoomPhotoIpPermitLimit", 90),
        configuration.GetValue("RateLimiting:RoomPhotoWindowSeconds", 60));
    private readonly PartitionedRateLimiter<string> identifier = Create(
        configuration.GetValue("RateLimiting:RoomPhotoIdentifierPermitLimit", 90),
        configuration.GetValue("RateLimiting:RoomPhotoWindowSeconds", 60));

    public async ValueTask<RoomPhotoRateLimitLease> AcquireAsync(string ipAddress, string identifierValue, CancellationToken ct)
    {
        var ipLease = await ip.AcquireAsync(ipAddress, 1, ct);
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identifierValue ?? string.Empty)));
        var idLease = await identifier.AcquireAsync(id, 1, ct);
        return new RoomPhotoRateLimitLease(ipLease, idLease);
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

public sealed class RoomPhotoRateLimitLease(RateLimitLease ipLease, RateLimitLease identifierLease) : IDisposable
{
    public bool IsAcquired => ipLease.IsAcquired && identifierLease.IsAcquired;
    public void Dispose() { ipLease.Dispose(); identifierLease.Dispose(); }
}

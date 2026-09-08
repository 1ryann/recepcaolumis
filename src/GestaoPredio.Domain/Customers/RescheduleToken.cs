using GestaoPredio.Domain.Common;

namespace GestaoPredio.Domain.Customers;

public sealed class RescheduleToken
{
    private RescheduleToken() { }

    public Guid Id { get; private set; }
    public Guid ReservationId { get; private set; }
    public byte[] TokenHash { get; private set; } = [];
    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }
    public uint Version { get; private set; }

    public static RescheduleToken Create(Guid reservationId, byte[] tokenHash,
        DateTimeOffset issuedAt, DateTimeOffset expiresAt)
    {
        if (reservationId == Guid.Empty) throw new ArgumentException("A reserva deve ser informada.", nameof(reservationId));
        if (tokenHash is null || tokenHash.Length != 32) throw new ArgumentException("O hash deve ter 32 bytes.", nameof(tokenHash));
        if (expiresAt <= issuedAt) throw new ArgumentException("A expiração deve ser posterior à emissão.", nameof(expiresAt));
        return new RescheduleToken
        {
            Id = Guid.NewGuid(), ReservationId = reservationId, TokenHash = tokenHash.ToArray(),
            IssuedAt = TimestampNormalizer.ToUtcMicroseconds(issuedAt), ExpiresAt = TimestampNormalizer.ToUtcMicroseconds(expiresAt)
        };
    }

    public void Revoke(DateTimeOffset at) => RevokedAt = TimestampNormalizer.ToUtcMicroseconds(at);
    public void MarkUsed(DateTimeOffset at) => UsedAt = TimestampNormalizer.ToUtcMicroseconds(at);

    public void Rotate(byte[] tokenHash, DateTimeOffset issuedAt, DateTimeOffset expiresAt)
    {
        if (tokenHash is null || tokenHash.Length != 32) throw new ArgumentException("O hash deve ter 32 bytes.", nameof(tokenHash));
        if (expiresAt <= issuedAt) throw new ArgumentException("A expiração deve ser posterior à emissão.", nameof(expiresAt));
        TokenHash = tokenHash.ToArray();
        IssuedAt = TimestampNormalizer.ToUtcMicroseconds(issuedAt);
        ExpiresAt = TimestampNormalizer.ToUtcMicroseconds(expiresAt);
        RevokedAt = null;
        UsedAt = null;
    }
}

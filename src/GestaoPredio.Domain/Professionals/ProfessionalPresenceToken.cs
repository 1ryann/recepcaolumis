using GestaoPredio.Domain.Common;

namespace GestaoPredio.Domain.Professionals;

public sealed class ProfessionalPresenceToken
{
    private ProfessionalPresenceToken() { }

    public Guid Id { get; private set; }
    public Guid ProfessionalId { get; private set; }
    public byte[] TokenHash { get; private set; } = [];
    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }
    public uint Version { get; private set; }

    public static ProfessionalPresenceToken Create(Guid professionalId, byte[] tokenHash,
        DateTimeOffset issuedAt, DateTimeOffset expiresAt)
    {
        if (professionalId == Guid.Empty) throw new ArgumentException("O profissional deve ser informado.", nameof(professionalId));
        if (tokenHash is null || tokenHash.Length != 32) throw new ArgumentException("O hash deve ter 32 bytes.", nameof(tokenHash));
        if (expiresAt <= issuedAt) throw new ArgumentException("A expiração deve ser posterior à emissão.", nameof(expiresAt));
        return new ProfessionalPresenceToken
        {
            Id = Guid.NewGuid(), ProfessionalId = professionalId, TokenHash = tokenHash.ToArray(),
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

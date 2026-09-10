using GestaoPredio.Domain.Common;

namespace GestaoPredio.Domain.Customers;

public enum TotemBookingHandoffStatus : short { Pending = 0, Completed = 1, Expired = 2 }

/// <summary>
/// A one-shot bridge from the public Totem to a visitor's phone: the phone completes a booking for
/// <see cref="ProfessionalId"/> while the kiosk polls for the result. No PII, no plaintext token.
/// </summary>
public sealed class TotemBookingHandoff
{
    private TotemBookingHandoff() { }

    public Guid Id { get; private set; }
    public Guid ProfessionalId { get; private set; }
    public byte[] HandoffTokenHash { get; private set; } = [];
    public byte[] StatusTokenHash { get; private set; } = [];
    public TotemBookingHandoffStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public Guid? ReservationId { get; private set; }
    public uint Version { get; private set; }

    public static TotemBookingHandoff Create(Guid professionalId, byte[] handoffTokenHash, byte[] statusTokenHash,
        DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        if (professionalId == Guid.Empty) throw new ArgumentException("O profissional deve ser informado.", nameof(professionalId));
        if (handoffTokenHash is not { Length: 32 }) throw new ArgumentException("O hash deve ter 32 bytes.", nameof(handoffTokenHash));
        if (statusTokenHash is not { Length: 32 }) throw new ArgumentException("O hash deve ter 32 bytes.", nameof(statusTokenHash));
        if (expiresAt <= createdAt) throw new ArgumentException("A expiração deve ser posterior à criação.", nameof(expiresAt));
        return new TotemBookingHandoff
        {
            Id = Guid.NewGuid(), ProfessionalId = professionalId,
            HandoffTokenHash = handoffTokenHash.ToArray(), StatusTokenHash = statusTokenHash.ToArray(),
            Status = TotemBookingHandoffStatus.Pending,
            CreatedAt = TimestampNormalizer.ToUtcMicroseconds(createdAt),
            ExpiresAt = TimestampNormalizer.ToUtcMicroseconds(expiresAt),
        };
    }

    public void MarkStarted(DateTimeOffset at, TimeSpan graceWindow, DateTimeOffset hardCeiling)
    {
        Require(TotemBookingHandoffStatus.Pending);
        if (StartedAt is not null) return;
        StartedAt = TimestampNormalizer.ToUtcMicroseconds(at);
        var extended = at + graceWindow;
        var target = extended > ExpiresAt ? extended : ExpiresAt;
        if (target > hardCeiling) target = hardCeiling;
        ExpiresAt = TimestampNormalizer.ToUtcMicroseconds(target);
    }

    public void Complete(Guid reservationId, DateTimeOffset at)
    {
        Require(TotemBookingHandoffStatus.Pending);
        if (reservationId == Guid.Empty) throw new ArgumentException("A reserva deve ser informada.", nameof(reservationId));
        Status = TotemBookingHandoffStatus.Completed;
        ReservationId = reservationId;
        CompletedAt = TimestampNormalizer.ToUtcMicroseconds(at);
    }

    public void MarkExpired(DateTimeOffset at)
    {
        Require(TotemBookingHandoffStatus.Pending);
        Status = TotemBookingHandoffStatus.Expired;
        _ = at;
    }

    public bool IsUsable(DateTimeOffset now) => Status == TotemBookingHandoffStatus.Pending && ExpiresAt > now;

    private void Require(TotemBookingHandoffStatus expected)
    {
        if (Status != expected) throw new InvalidOperationException($"Handoff em {Status}; esperado {expected}.");
    }
}

using GestaoPredio.Domain.Common;

namespace GestaoPredio.Domain.Availability;

public enum RoomBlockStatus
{
    Active,
    Cancelled
}

public sealed class RoomBlock
{
    public const int MaximumReasonLength = 500;

    private RoomBlock() { }

    public Guid Id { get; private set; }
    public Guid RoomId { get; private set; }
    public DateTimeOffset StartAt { get; private set; }
    public DateTimeOffset EndAt { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public RoomBlockStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? CancelledBy { get; private set; }
    public uint Version { get; private set; }

    public static RoomBlock Create(Guid roomId, DateTimeOffset startAt, DateTimeOffset endAt,
        string reason, string createdBy, DateTimeOffset occurredAt)
    {
        ValidateRoom(roomId);
        ValidateActor(createdBy);
        var block = new RoomBlock
        {
            Id = Guid.NewGuid(), RoomId = roomId, Status = RoomBlockStatus.Active,
            CreatedBy = createdBy, CreatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt),
            UpdatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt)
        };
        block.SetPeriod(startAt, endAt, reason);
        return block;
    }

    public void Update(DateTimeOffset startAt, DateTimeOffset endAt, string reason, DateTimeOffset occurredAt)
    {
        EnsureApplicable(occurredAt);
        SetPeriod(startAt, endAt, reason);
        UpdatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
    }

    public void Cancel(string actorUserId, DateTimeOffset occurredAt)
    {
        EnsureApplicable(occurredAt);
        ValidateActor(actorUserId);
        Status = RoomBlockStatus.Cancelled;
        CancelledAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
        CancelledBy = actorUserId;
        UpdatedAt = CancelledAt.Value;
    }

    private void SetPeriod(DateTimeOffset startAt, DateTimeOffset endAt, string reason)
    {
        var start = TimestampNormalizer.ToUtcMicroseconds(startAt);
        var end = TimestampNormalizer.ToUtcMicroseconds(endAt);
        var normalizedReason = reason?.Trim();
        if (end <= start) throw new ArgumentException("O término deve ser posterior ao início.", nameof(endAt));
        if (string.IsNullOrWhiteSpace(normalizedReason) || normalizedReason.Length > MaximumReasonLength)
            throw new ArgumentException("O motivo deve ser informado.", nameof(reason));
        StartAt = start;
        EndAt = end;
        Reason = normalizedReason;
    }

    private void EnsureApplicable(DateTimeOffset occurredAt)
    {
        if (Status != RoomBlockStatus.Active || EndAt <= TimestampNormalizer.ToUtcMicroseconds(occurredAt))
            throw new InvalidOperationException("O bloqueio não pode mais ser alterado.");
    }

    private static void ValidateRoom(Guid roomId)
    {
        if (roomId == Guid.Empty) throw new ArgumentException("A sala deve ser informada.", nameof(roomId));
    }

    private static void ValidateActor(string actorUserId)
    {
        if (string.IsNullOrWhiteSpace(actorUserId) || actorUserId.Length > 450)
            throw new ArgumentException("O ator deve ser informado.", nameof(actorUserId));
    }
}

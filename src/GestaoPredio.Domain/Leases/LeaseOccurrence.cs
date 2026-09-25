using GestaoPredio.Domain.Common;

namespace GestaoPredio.Domain.Leases;

public sealed class LeaseOccurrence
{
    private LeaseOccurrence()
    {
    }

    public Guid Id { get; private set; }
    public Guid LeaseId { get; private set; }
    public DateTimeOffset StartAt { get; private set; }
    public DateTimeOffset EndAt { get; private set; }
    public LeaseOccurrenceState State { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public uint Version { get; private set; }

    public static LeaseOccurrence Create(
        Guid leaseId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        DateTimeOffset occurredAt)
    {
        if (leaseId == Guid.Empty) throw new ArgumentException("A locação deve ser informada.", nameof(leaseId));
        var start = TimestampNormalizer.ToUtcMicroseconds(startAt);
        var end = TimestampNormalizer.ToUtcMicroseconds(endAt);
        if (end <= start) throw new ArgumentException("O término deve ser posterior ao início.", nameof(endAt));
        var timestamp = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
        return new LeaseOccurrence
        {
            Id = Guid.NewGuid(),
            LeaseId = leaseId,
            StartAt = start,
            EndAt = end,
            State = LeaseOccurrenceState.Planned,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
    }

    public void Cancel(DateTimeOffset occurredAt)
    {
        if (State != LeaseOccurrenceState.Planned) return;
        State = LeaseOccurrenceState.Cancelled;
        UpdatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
    }

    /// <summary>
    /// Takes back the cancellation when the lease it belongs to is reactivated. The row is reused instead of being
    /// replaced: (LeaseId, StartAt) is unique, so a fresh occurrence for the same start would collide with the one
    /// being removed in the same save.
    /// </summary>
    public void Reopen(DateTimeOffset occurredAt)
    {
        if (State != LeaseOccurrenceState.Cancelled)
            throw new InvalidOperationException("Somente uma ocorrência cancelada pode ser reaberta.");
        State = LeaseOccurrenceState.Planned;
        UpdatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
    }

    public void Complete(DateTimeOffset occurredAt)
    {
        if (State != LeaseOccurrenceState.Planned)
            throw new InvalidOperationException("A ocorrência não pode ser concluída neste estado.");
        State = LeaseOccurrenceState.Completed;
        UpdatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
    }

    public void RescheduleEnd(DateTimeOffset endAt, DateTimeOffset occurredAt)
    {
        if (State != LeaseOccurrenceState.Planned)
            throw new InvalidOperationException("Somente uma ocorrência planejada pode ser ajustada.");
        var end = TimestampNormalizer.ToUtcMicroseconds(endAt);
        if (end <= StartAt) throw new ArgumentException("O término deve ser posterior ao início.", nameof(endAt));
        EndAt = end;
        UpdatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
    }
}

using GestaoPredio.Domain.Common;

namespace GestaoPredio.Domain.Professionals;

public sealed class ProfessionalAvailabilityException
{
    public const int MaximumReasonLength = 300;

    private ProfessionalAvailabilityException() { }

    public Guid Id { get; private set; }
    public Guid ProfessionalId { get; private set; }
    public DateOnly Date { get; private set; }
    public bool AllDay { get; private set; }
    public TimeOnly? StartTime { get; private set; }
    public TimeOnly? EndTime { get; private set; }
    public string? Reason { get; private set; }
    public ProfessionalAvailabilityExceptionOrigin Origin { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public uint Version { get; private set; }

    public static ProfessionalAvailabilityException Create(Guid professionalId, DateOnly date,
        bool allDay, TimeOnly? startTime, TimeOnly? endTime, string? reason, DateTimeOffset occurredAt,
        ProfessionalAvailabilityExceptionOrigin origin = ProfessionalAvailabilityExceptionOrigin.Planned)
    {
        if (professionalId == Guid.Empty) throw new ArgumentException("O profissional deve ser informado.", nameof(professionalId));
        var timestamp = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
        var value = new ProfessionalAvailabilityException
        {
            Id = Guid.NewGuid(),
            ProfessionalId = professionalId,
            Origin = origin,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
        value.Set(date, allDay, startTime, endTime, reason);
        return value;
    }

    public void Update(DateOnly date, bool allDay, TimeOnly? startTime, TimeOnly? endTime,
        string? reason, DateTimeOffset occurredAt)
    {
        Set(date, allDay, startTime, endTime, reason);
        UpdatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
    }

    private void Set(DateOnly date, bool allDay, TimeOnly? startTime, TimeOnly? endTime, string? reason)
    {
        if (allDay && (startTime is not null || endTime is not null) ||
            !allDay && (startTime is null || endTime is null || endTime <= startTime))
            throw new ArgumentException("A exceção deve representar um dia inteiro ou um intervalo válido.");

        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (normalizedReason?.Length > MaximumReasonLength)
            throw new ArgumentOutOfRangeException(nameof(reason), $"O motivo deve ter até {MaximumReasonLength} caracteres.");

        Date = date;
        AllDay = allDay;
        StartTime = startTime;
        EndTime = endTime;
        Reason = normalizedReason;
    }
}

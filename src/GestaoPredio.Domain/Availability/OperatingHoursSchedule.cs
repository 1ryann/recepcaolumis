using GestaoPredio.Domain.Common;

namespace GestaoPredio.Domain.Availability;

public sealed class OperatingHoursSchedule
{
    public static readonly Guid SingletonId = Guid.Parse("3d410af9-a922-46bd-8a17-e605922f8460");

    private OperatingHoursSchedule() { }

    public Guid Id { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public uint Version { get; private set; }

    public static OperatingHoursSchedule Create(DateTimeOffset occurredAt)
    {
        var timestamp = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
        return new OperatingHoursSchedule
        {
            Id = SingletonId,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
    }

    public void MarkUpdated(DateTimeOffset occurredAt) =>
        UpdatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
}

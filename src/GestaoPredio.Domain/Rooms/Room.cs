using GestaoPredio.Domain.Common;

namespace GestaoPredio.Domain.Rooms;

public sealed class Room
{
    private Room()
    {
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public string NormalizedName { get; private set; } = "";
    public string? Description { get; private set; }
    public decimal HourlyRate { get; private set; }
    public decimal DailyRate { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static Room Create(string name, string? description, decimal hourlyRate, decimal dailyRate, DateTimeOffset occurredAt)
    {
        var room = new Room
        {
            Id = Guid.NewGuid(),
            IsActive = true,
            CreatedAt = occurredAt.ToUniversalTime(),
            UpdatedAt = occurredAt.ToUniversalTime()
        };

        room.SetDetails(name, description, hourlyRate, dailyRate);
        return room;
    }

    public void Update(string name, string? description, decimal hourlyRate, decimal dailyRate, DateTimeOffset occurredAt)
    {
        SetDetails(name, description, hourlyRate, dailyRate);
        UpdatedAt = occurredAt.ToUniversalTime();
    }

    public void Activate(DateTimeOffset occurredAt) => SetActive(true, occurredAt);

    public void Deactivate(DateTimeOffset occurredAt) => SetActive(false, occurredAt);

    private void SetDetails(string name, string? description, decimal hourlyRate, decimal dailyRate)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("O nome deve ser informado.", nameof(name));
        }

        if (!RoomRate.IsValid(hourlyRate))
        {
            throw new ArgumentOutOfRangeException(nameof(hourlyRate));
        }

        if (!RoomRate.IsValid(dailyRate))
        {
            throw new ArgumentOutOfRangeException(nameof(dailyRate));
        }

        Name = name;
        NormalizedName = TextNormalizer.Normalize(name);
        Description = description;
        HourlyRate = hourlyRate;
        DailyRate = dailyRate;
    }

    private void SetActive(bool isActive, DateTimeOffset occurredAt)
    {
        IsActive = isActive;
        UpdatedAt = occurredAt.ToUniversalTime();
    }
}

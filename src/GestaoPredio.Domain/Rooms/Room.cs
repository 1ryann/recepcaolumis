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

    // The public-catalogue attributes. All optional — see RoomFeatures for why. They are
    // held as individual columns rather than one serialized blob so the database can check
    // each one, and they are set and read as a single RoomFeatures so no caller can update
    // half of a capacity range.
    public decimal? MonthlyRate { get; private set; }
    public decimal? AreaSquareMeters { get; private set; }
    public int? BathroomCount { get; private set; }
    public int? CapacityMin { get; private set; }
    public int? CapacityMax { get; private set; }
    public RoomCategory? Category { get; private set; }

    // Persisted as an array of RoomAmenityCode strings; the backing field is what EF maps.
    private readonly List<string> _amenityCodes = [];

    public IReadOnlyList<RoomAmenity> Amenities =>
        _amenityCodes.Select(code => RoomAmenityCode.TryParse(code, out var amenity)
            ? (RoomAmenity?)amenity : null).OfType<RoomAmenity>().ToArray();

    public RoomFeatures Features => new(MonthlyRate, AreaSquareMeters, BathroomCount,
        CapacityMin, CapacityMax, Category, Amenities);

    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public uint Version { get; private set; }

    // `features` is optional and defaults to RoomFeatures.None: a room is created with a
    // name and its rates long before anyone measures, prices or photographs it.
    public static Room Create(string name, string? description, decimal hourlyRate, decimal dailyRate,
        DateTimeOffset occurredAt, RoomFeatures? features = null)
    {
        var room = new Room
        {
            Id = Guid.NewGuid(),
            IsActive = true,
            CreatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt),
            UpdatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt)
        };

        room.SetDetails(name, description, hourlyRate, dailyRate, features);
        return room;
    }

    // Update replaces the room's details wholesale, features included: omitting them
    // clears them, exactly as omitting a description clears it.
    public void Update(string name, string? description, decimal hourlyRate, decimal dailyRate,
        DateTimeOffset occurredAt, RoomFeatures? features = null)
    {
        SetDetails(name, description, hourlyRate, dailyRate, features);
        UpdatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
    }

    public void Activate(DateTimeOffset occurredAt) => SetActive(true, occurredAt);

    public void Deactivate(DateTimeOffset occurredAt) => SetActive(false, occurredAt);

    private void SetDetails(string name, string? description, decimal hourlyRate, decimal dailyRate,
        RoomFeatures? features)
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

        var applied = features ?? RoomFeatures.None;
        applied.Validate();

        Name = name;
        NormalizedName = TextNormalizer.Normalize(name);
        Description = description;
        HourlyRate = hourlyRate;
        DailyRate = dailyRate;
        MonthlyRate = applied.MonthlyRate;
        AreaSquareMeters = applied.AreaSquareMeters;
        BathroomCount = applied.BathroomCount;
        CapacityMin = applied.CapacityMin;
        CapacityMax = applied.CapacityMax;
        Category = applied.Category;
        _amenityCodes.Clear();
        _amenityCodes.AddRange(applied.Amenities.Select(RoomAmenityCode.From));
    }

    private void SetActive(bool isActive, DateTimeOffset occurredAt)
    {
        IsActive = isActive;
        UpdatedAt = TimestampNormalizer.ToUtcMicroseconds(occurredAt);
    }
}

namespace GestaoPredio.Domain.Rooms;

/// <summary>
/// The commercial attributes the public catalogue shows for a room: a monthly price, the
/// area, how many bathrooms and people it holds, what kind of room it is and its comforts.
///
/// Every member is optional, and that is the point. A room is created with a name and its
/// hourly/daily rates long before anyone measures it or photographs it, so the absence of
/// a value has to be an ordinary state rather than an error — the catalogue leaves out
/// what it does not know instead of printing a zero. <see cref="None"/> is that state.
///
/// Validation lives here so the HTTP layer and the entity apply the same rules: the
/// endpoint calls <see cref="IsValid"/> to answer with an error code, and the entity calls
/// <see cref="Validate"/> to refuse to hold a value that no endpoint should have let in.
/// </summary>
public sealed record RoomFeatures(
    decimal? MonthlyRate,
    decimal? AreaSquareMeters,
    int? BathroomCount,
    int? CapacityMin,
    int? CapacityMax,
    RoomCategory? Category,
    IReadOnlyList<RoomAmenity> Amenities)
{
    public const decimal MaximumAreaSquareMeters = 100_000m;
    public const int MaximumBathroomCount = 20;
    public const int MaximumCapacity = 200;

    public static readonly RoomFeatures None = new(null, null, null, null, null, null, []);

    public bool IsValid() =>
        IsValidMonthlyRate(MonthlyRate)
        && IsValidArea(AreaSquareMeters)
        && IsValidBathroomCount(BathroomCount)
        && IsValidCapacity(CapacityMin, CapacityMax)
        && IsValidCategory(Category)
        && IsValidAmenities(Amenities);

    public void Validate()
    {
        if (!IsValid()) throw new ArgumentException("As características da sala são inválidas.");
    }

    public static bool IsValidMonthlyRate(decimal? value) =>
        value is null || RoomRate.IsValid(value.Value);

    public static bool IsValidArea(decimal? value) =>
        value is null || (value > 0m && value < MaximumAreaSquareMeters && Scale(value.Value) <= 2);

    /// A room may genuinely have no bathroom of its own, so zero is a real answer.
    public static bool IsValidBathroomCount(int? value) =>
        value is null || value is >= 0 and <= MaximumBathroomCount;

    /// A maximum on its own says nothing ("holds up to 8" with no floor is how many?), so
    /// a range always starts at its minimum; repeating the minimum as the maximum is how a
    /// fixed-size room is written.
    public static bool IsValidCapacity(int? minimum, int? maximum)
    {
        if (minimum is null) return maximum is null;
        if (minimum is < 1 or > MaximumCapacity) return false;
        return maximum is null || (maximum >= minimum && maximum <= MaximumCapacity);
    }

    public static bool IsValidCategory(RoomCategory? value) =>
        value is null || RoomCategoryCode.IsDefined(value.Value);

    public static bool IsValidAmenities(IReadOnlyList<RoomAmenity>? amenities)
    {
        if (amenities is null) return false;
        if (amenities.Distinct().Count() != amenities.Count) return false;
        return amenities.All(RoomAmenityCode.IsDefined);
    }

    // A record compares its members with Equals, and for IReadOnlyList that is reference
    // equality — two features holding the same amenities in two different lists would
    // compare unequal, which would make the admin endpoint record a change on every save.
    // Amenities are a set, so order is not part of the comparison either.
    public bool Equals(RoomFeatures? other) =>
        other is not null
        && MonthlyRate == other.MonthlyRate
        && AreaSquareMeters == other.AreaSquareMeters
        && BathroomCount == other.BathroomCount
        && CapacityMin == other.CapacityMin
        && CapacityMax == other.CapacityMax
        && Category == other.Category
        && Amenities.Count == other.Amenities.Count
        && !Amenities.Except(other.Amenities).Any();

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(MonthlyRate);
        hash.Add(AreaSquareMeters);
        hash.Add(BathroomCount);
        hash.Add(CapacityMin);
        hash.Add(CapacityMax);
        hash.Add(Category);
        foreach (var amenity in Amenities.OrderBy(x => x)) hash.Add(amenity);
        return hash.ToHashCode();
    }

    private static int Scale(decimal value) => (decimal.GetBits(value)[3] >> 16) & 0xFF;
}

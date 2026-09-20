using System.Text.RegularExpressions;
using GestaoPredio.Domain.Rooms;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Rooms;

public sealed record CreateRoomRequest(
    string? Name,
    string? Description,
    decimal? HourlyRate,
    decimal? DailyRate,
    decimal? MonthlyRate = null,
    decimal? AreaSquareMeters = null,
    int? BathroomCount = null,
    int? CapacityMin = null,
    int? CapacityMax = null,
    string? Category = null,
    IReadOnlyList<string>? Amenities = null) : IStrictModuleRequest;

public sealed record UpdateRoomRequest(
    string? Name,
    string? Description,
    decimal? HourlyRate,
    decimal? DailyRate,
    string? ConcurrencyToken,
    decimal? MonthlyRate = null,
    decimal? AreaSquareMeters = null,
    int? BathroomCount = null,
    int? CapacityMin = null,
    int? CapacityMax = null,
    string? Category = null,
    IReadOnlyList<string>? Amenities = null) : IStrictModuleRequest;

public sealed record RoomConcurrencyRequest(string? ConcurrencyToken) : IStrictModuleRequest;

public sealed record RoomResponse(
    Guid Id,
    string Name,
    string? Description,
    decimal HourlyRate,
    decimal DailyRate,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string ConcurrencyToken,
    decimal? MonthlyRate,
    decimal? AreaSquareMeters,
    int? BathroomCount,
    int? CapacityMin,
    int? CapacityMax,
    string? Category,
    IReadOnlyList<string> Amenities);

internal sealed record ValidRoomInput(string Name, string? Description, decimal HourlyRate, decimal DailyRate,
    RoomFeatures Features);

internal static partial class RoomInput
{
    public static bool TryValidate(CreateRoomRequest request, out ValidRoomInput? input, out ApiError? error) =>
        TryValidate(request.Name, request.Description, request.HourlyRate, request.DailyRate,
            new FeatureFields(request.MonthlyRate, request.AreaSquareMeters, request.BathroomCount,
                request.CapacityMin, request.CapacityMax, request.Category, request.Amenities),
            out input, out error);

    public static bool TryValidate(UpdateRoomRequest request, out ValidRoomInput? input, out ApiError? error) =>
        TryValidate(request.Name, request.Description, request.HourlyRate, request.DailyRate,
            new FeatureFields(request.MonthlyRate, request.AreaSquareMeters, request.BathroomCount,
                request.CapacityMin, request.CapacityMax, request.Category, request.Amenities),
            out input, out error);

    // The catalogue attributes as they arrive over the wire: the category and the amenities
    // are codes, which have to be parsed before RoomFeatures can judge them.
    internal sealed record FeatureFields(decimal? MonthlyRate, decimal? AreaSquareMeters, int? BathroomCount,
        int? CapacityMin, int? CapacityMax, string? Category, IReadOnlyList<string>? Amenities);

    private static bool TryValidate(string? name, string? description, decimal? hourlyRate, decimal? dailyRate,
        FeatureFields fields, out ValidRoomInput? input, out ApiError? error)
    {
        var displayName = Collapse(name);
        var cleanDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (displayName.Length is < 1 or > 100 || cleanDescription?.Length > 1000)
            return Fail("INVALID_ROOM", "Os dados da sala são inválidos.", out input, out error);
        if (!hourlyRate.HasValue || !dailyRate.HasValue ||
            !RoomRate.IsValid(hourlyRate.Value) || !RoomRate.IsValid(dailyRate.Value))
            return Fail("INVALID_ROOM_RATE", "As tarifas da sala são inválidas.", out input, out error);
        if (!TryReadFeatures(fields, out var features))
            return Fail("INVALID_ROOM_FEATURES", "As características da sala são inválidas.", out input, out error);

        input = new ValidRoomInput(displayName, cleanDescription, hourlyRate.Value, dailyRate.Value, features!);
        error = null;
        return true;
    }

    private static bool TryReadFeatures(FeatureFields fields, out RoomFeatures? features)
    {
        features = null;

        RoomCategory? category = null;
        if (!string.IsNullOrWhiteSpace(fields.Category))
        {
            if (!RoomCategoryCode.TryParse(fields.Category, out var parsed)) return false;
            category = parsed;
        }

        var amenities = new List<RoomAmenity>();
        foreach (var code in fields.Amenities ?? [])
        {
            if (!RoomAmenityCode.TryParse(code, out var amenity)) return false;
            amenities.Add(amenity);
        }

        var candidate = new RoomFeatures(fields.MonthlyRate, fields.AreaSquareMeters, fields.BathroomCount,
            fields.CapacityMin, fields.CapacityMax, category, amenities);
        if (!candidate.IsValid()) return false;

        features = candidate;
        return true;
    }

    private static bool Fail(string code, string message, out ValidRoomInput? input, out ApiError? error)
    {
        input = null;
        error = new ApiError(code, message);
        return false;
    }

    private static string Collapse(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : Whitespace().Replace(value.Trim(), " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

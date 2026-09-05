using System.Text.RegularExpressions;
using GestaoPredio.Domain.Rooms;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Rooms;

public sealed record CreateRoomRequest(
    string? Name,
    string? Description,
    decimal? HourlyRate,
    decimal? DailyRate) : IStrictModuleRequest;

public sealed record UpdateRoomRequest(
    string? Name,
    string? Description,
    decimal? HourlyRate,
    decimal? DailyRate,
    string? ConcurrencyToken) : IStrictModuleRequest;

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
    string ConcurrencyToken);

internal sealed record ValidRoomInput(string Name, string? Description, decimal HourlyRate, decimal DailyRate);

internal static partial class RoomInput
{
    public static bool TryValidate(CreateRoomRequest request, out ValidRoomInput? input, out ApiError? error) =>
        TryValidate(request.Name, request.Description, request.HourlyRate, request.DailyRate, out input, out error);

    public static bool TryValidate(UpdateRoomRequest request, out ValidRoomInput? input, out ApiError? error) =>
        TryValidate(request.Name, request.Description, request.HourlyRate, request.DailyRate, out input, out error);

    private static bool TryValidate(string? name, string? description, decimal? hourlyRate, decimal? dailyRate,
        out ValidRoomInput? input, out ApiError? error)
    {
        var displayName = Collapse(name);
        var cleanDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (displayName.Length is < 1 or > 100 || cleanDescription?.Length > 1000)
            return Fail("INVALID_ROOM", "Os dados da sala são inválidos.", out input, out error);
        if (!hourlyRate.HasValue || !dailyRate.HasValue ||
            !RoomRate.IsValid(hourlyRate.Value) || !RoomRate.IsValid(dailyRate.Value))
            return Fail("INVALID_ROOM_RATE", "As tarifas da sala são inválidas.", out input, out error);

        input = new ValidRoomInput(displayName, cleanDescription, hourlyRate.Value, dailyRate.Value);
        error = null;
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

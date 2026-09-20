using GestaoPredio.Domain.Rooms;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Rooms;

internal static class RoomMappings
{
    public static RoomResponse ToResponse(this Room room) => new(
        room.Id,
        room.Name,
        room.Description,
        room.HourlyRate,
        room.DailyRate,
        room.IsActive,
        room.CreatedAt,
        room.UpdatedAt,
        ConcurrencyToken.Encode(room.Version),
        room.MonthlyRate,
        room.AreaSquareMeters,
        room.BathroomCount,
        room.CapacityMin,
        room.CapacityMax,
        room.Category is { } category ? RoomCategoryCode.From(category) : null,
        [.. room.Amenities.Select(RoomAmenityCode.From)]);
}

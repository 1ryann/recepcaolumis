using GestaoPredio.Domain.Rooms;

namespace GestaoPredio.Application.Rooms;

public sealed record RoomRentalAvailability(
    PublicRoomAvailabilityStatus Status,
    DateOnly? AvailableFrom);

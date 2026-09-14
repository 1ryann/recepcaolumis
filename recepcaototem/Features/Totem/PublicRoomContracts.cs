using GestaoPredio.Domain.Rooms;

namespace recepcaototem.Features.Totem;

public sealed record PublicRoomCard(
    Guid Id,
    string Name,
    string? Description,
    PublicRoomAvailabilityStatus Availability,
    DateOnly? AvailableFrom,
    string? CoverPhotoUrl);

public sealed record PublicRoomDetail(
    Guid Id,
    string Name,
    string? Description,
    PublicRoomAvailabilityStatus Availability,
    DateOnly? AvailableFrom,
    IReadOnlyList<string> PhotoUrls);

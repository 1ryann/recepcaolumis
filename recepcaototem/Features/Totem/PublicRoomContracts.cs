using GestaoPredio.Domain.Rooms;

namespace recepcaototem.Features.Totem;

/// <summary>
/// What an anonymous visitor is told about a room. Prices used to be excluded from here on
/// purpose; the catalogue now advertises them, so the monthly, hourly and daily rates are
/// part of the contract. What stays out is everything about *who occupies* the room — the
/// tenant, the professional and the rate they negotiated — which is nobody else's business
/// and is asserted in PublicRoomContractsTests.
/// </summary>
public sealed record PublicRoomCard(
    Guid Id,
    string Name,
    string? Description,
    PublicRoomAvailabilityStatus Availability,
    DateOnly? AvailableFrom,
    string? CoverPhotoUrl,
    decimal? MonthlyRate,
    int? CapacityMin,
    int? CapacityMax,
    string? Category);

public sealed record PublicRoomDetail(
    Guid Id,
    string Name,
    string? Description,
    PublicRoomAvailabilityStatus Availability,
    DateOnly? AvailableFrom,
    IReadOnlyList<string> PhotoUrls,
    decimal? MonthlyRate,
    decimal? HourlyRate,
    decimal? DailyRate,
    decimal? AreaSquareMeters,
    int? BathroomCount,
    int? CapacityMin,
    int? CapacityMax,
    string? Category,
    IReadOnlyList<string> Amenities,
    // Built on the server from the configured reception number, never in the browser, and
    // null when no number is configured so the button can simply not be offered.
    string? WhatsappUrl);

using recepcaototem.Features.Common;

namespace recepcaototem.Features.Reservations;

public sealed record CreateReservationRequest(
    Guid RoomId,
    Guid ProfessionalId,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt) : IStrictModuleRequest;

public sealed record ReservationResponse(
    Guid Id,
    Guid RoomId,
    string RoomName,
    Guid ProfessionalId,
    string ProfessionalName,
    Guid? OriginalReservationId,
    string Kind,
    string Status,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    DateTimeOffset RequestedAt,
    DateTimeOffset? DecidedAt,
    string? RejectionReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string ConcurrencyToken);

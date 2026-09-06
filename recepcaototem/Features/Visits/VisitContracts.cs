using recepcaototem.Features.Common;

namespace recepcaototem.Features.Visits;

public sealed record CreateVisitRequest(Guid ProfessionalId, Guid? RoomId, Guid? ReservationId,
    string? VisitorName) : IStrictModuleRequest;
public sealed record VisitConcurrencyRequest(string? ConcurrencyToken) : IStrictModuleRequest;
public sealed record CorrectVisitRequest(string? Status, string? Reason,
    string? ConcurrencyToken) : IStrictModuleRequest;
public sealed record VisitTransitionResponse(Guid Id, string? PreviousStatus, string NewStatus,
    DateTimeOffset OccurredAt, string? Reason, bool IsCorrection);
public sealed record VisitResponse(Guid Id, Guid ProfessionalId, string ProfessionalName,
    Guid? RoomId, string? RoomName, Guid? ReservationId, string VisitorName, string Status,
    DateTimeOffset ArrivedAt, DateTimeOffset? ServiceStartedAt, DateTimeOffset? EndedAt,
    DateTimeOffset? CancelledAt, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    string ConcurrencyToken, IReadOnlyList<VisitTransitionResponse> History);

using recepcaototem.Features.Common;

namespace recepcaototem.Features.Reception;

public sealed record ReceptionVisitResponse(Guid Id, Guid ProfessionalId, Guid? RoomId, Guid? ReservationId,
    Guid? CustomerId, string VisitorName, string Status, DateTimeOffset ArrivedAt,
    DateTimeOffset? ServiceStartedAt, DateTimeOffset? EndedAt, string ConcurrencyToken);

public sealed record ReceptionCheckInRequest(string? ConcurrencyToken, string? VisitorName) : IStrictModuleRequest;

public sealed record ReceptionPresenceRequest(Guid ProfessionalId, string State) : IStrictModuleRequest;

public sealed record ReceptionProfessionalResponse(Guid ProfessionalId, string Name, string Profession, string? Description,
    bool HasPhoto, string? PhotoUrl, string OperationalStatus, Guid? CurrentRoomId,
    Guid? CurrentVisitId, int WaitingVisitorsCount, DateTimeOffset? NextReservationAt,
    bool CanReceiveVisitor, string Presence, DateTimeOffset? AbsentUntil);

public sealed record ReceptionRoomResponse(Guid Id, string Name, string OperationalStatus,
    Guid? CurrentProfessionalId, string? CurrentProfessionalName, Guid? CurrentVisitId,
    bool Blocked, DateTimeOffset? NextReservationAt, bool CanReceiveVisitor);

public sealed record ReceptionAgendaItem(Guid ReservationId, Guid? CustomerId, string? CustomerName,
    string? CustomerPhone, Guid ProfessionalId, string ProfessionalName, Guid RoomId, string RoomName,
    DateTimeOffset StartAt, DateTimeOffset EndAt, string ReservationStatus, Guid? VisitId,
    string? VisitStatus, bool CanCheckIn);

public sealed record ReceptionReservationItem(Guid ReservationId, Guid? CustomerId, string? CustomerName,
    string? CustomerPhone, Guid ProfessionalId, string ProfessionalName, Guid RoomId, string RoomName,
    DateTimeOffset StartAt, DateTimeOffset EndAt, string Status, Guid? VisitId, string? VisitStatus,
    string ConcurrencyToken);

public sealed record ReceptionVisitItem(Guid Id, Guid ProfessionalId, string ProfessionalName, Guid? RoomId,
    string? RoomName, Guid? ReservationId, Guid? CustomerId, string? CustomerName, string VisitorName,
    string Status, DateTimeOffset ArrivedAt, DateTimeOffset? ServiceStartedAt,
    DateTimeOffset? EndedAt, string ConcurrencyToken);

public sealed record ReceptionOverviewResponse(int VisitorsWaiting, int VisitsInService,
    int ProfessionalsAvailable, int ProfessionalsInService, int RoomsAvailable, int RoomsOccupied,
    int ReservationsToday, IReadOnlyList<ReceptionAgendaItem> UpcomingReservations,
    IReadOnlyList<ReceptionVisitItem> WaitingVisits, IReadOnlyList<ReceptionVisitItem> CurrentVisits,
    int WarningAlerts, int CriticalAlerts);

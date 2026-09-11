using GestaoPredio.Application.Finance;

namespace GestaoPredio.Application.Dashboard;

public sealed record DashboardCounts(
    int ActiveProfessionals, int ActiveRooms, int OccupiedRooms, int ReservedRooms,
    int ActiveLeases, int ScheduledLeases, int PendingReservations, int TodayReservations,
    int WaitingVisits, int InServiceVisits, int TodayCheckIns);

public sealed record DashboardAgendaItem(Guid ReservationId, Guid ProfessionalId, string ProfessionalName,
    Guid RoomId, string RoomName, DateTimeOffset StartAt, DateTimeOffset EndAt);

public sealed record DashboardCurrentVisit(Guid VisitId, string VisitorName, string Status,
    Guid ProfessionalId, string ProfessionalName, Guid? RoomId, string? RoomName,
    DateTimeOffset ArrivedAt, DateTimeOffset? ServiceStartedAt, int DurationMinutes);

public sealed record DashboardAlertItem(string Id, string Type, string Severity, string Title,
    string Message, DateTimeOffset ConditionAt, Guid? RoomId, Guid? ProfessionalId,
    Guid? ReservationId, Guid? VisitId, Guid? LeaseId);

public sealed record DashboardAlertSummary(int Total, int Warning, int Critical,
    IReadOnlyList<DashboardAlertItem> Recent);

public sealed record DashboardRoomStatus(Guid RoomId, string RoomName, string Status,
    DateTimeOffset? NextCommitmentAt);

public sealed record DashboardSnapshot(DateOnly OperationalDate, DashboardCounts Counts,
    FinancialSummary Financial, DashboardAlertSummary Alerts,
    IReadOnlyList<DashboardAgendaItem> Agenda,
    IReadOnlyList<DashboardCurrentVisit> CurrentVisits,
    IReadOnlyList<DashboardRoomStatus> Rooms);

public interface IDashboardReader
{
    Task<DashboardSnapshot> ReadAsync(DateTimeOffset now, CancellationToken cancellationToken);
}

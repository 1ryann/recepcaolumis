namespace GestaoPredio.Application.OperationalAlerts;

public enum OperationalAlertType
{
    ReservationEndedVisitWaiting,
    ReservationEndedVisitInService,
    NextReservationSoon,
    NextReservationConflict,
    LeaseEndingWithActiveVisit,
    CustomerWaitingProfessionalAbsent,
    OpenVisitAffectedByIncident
}

public enum OperationalAlertSeverity
{
    Info,
    Warning,
    Critical
}

public sealed record OperationalAlert(
    string Id,
    OperationalAlertType Type,
    OperationalAlertSeverity Severity,
    string Title,
    string Message,
    DateTimeOffset ConditionAt,
    Guid? RoomId,
    string? RoomName,
    Guid? ProfessionalId,
    string? ProfessionalName,
    Guid? ReservationId,
    Guid? VisitId,
    Guid? LeaseId);

public sealed record OperationalAlertFilter(
    OperationalAlertSeverity? Severity,
    OperationalAlertType? Type,
    Guid? RoomId,
    Guid? ProfessionalId);

public interface IOperationalAlertReader
{
    Task<IReadOnlyList<OperationalAlert>> ReadAsync(
        OperationalAlertFilter filter,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

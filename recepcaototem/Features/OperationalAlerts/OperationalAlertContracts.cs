namespace recepcaototem.Features.OperationalAlerts;

public sealed record OperationalAlertResponse(
    string Id,
    string Type,
    string Severity,
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

public sealed record OperationalAlertSummaryResponse(
    int Total,
    int Info,
    int Warning,
    int Critical);

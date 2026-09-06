using GestaoPredio.Application.OperationalAlerts;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.OperationalAlerts;

public static class OperationalAlertEndpoints
{
    public static IEndpointRouteBuilder MapOperationalAlertEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/operational-alerts")
            .RequireAuthorization("Operations");
        group.MapGet("", List);
        group.MapGet("/summary", Summary);
        return endpoints;
    }

    private static async Task<IResult> List(
        int? page,
        int? pageSize,
        string? severity,
        string? type,
        Guid? roomId,
        Guid? professionalId,
        IOperationalAlertReader reader,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var actualPage = page ?? 1;
        if (actualPage < 1)
            return Results.BadRequest(new ApiError("INVALID_PAGE", "A página deve ser maior ou igual a 1."));
        var actualPageSize = pageSize ?? 20;
        if (actualPageSize is < 1 or > 100)
            return Results.BadRequest(new ApiError("INVALID_PAGE_SIZE", "O tamanho da página deve estar entre 1 e 100."));
        if (!TryFilter(severity, type, roomId, professionalId, out var filter, out var error))
            return error!;

        var alerts = await reader.ReadAsync(filter!, timeProvider.GetUtcNow(), cancellationToken);
        var items = alerts.Skip((actualPage - 1) * actualPageSize).Take(actualPageSize)
            .Select(Map).ToArray();
        return Results.Ok(new PagedResponse<OperationalAlertResponse>(
            items, actualPage, actualPageSize, alerts.Count));
    }

    private static async Task<IResult> Summary(
        string? severity,
        string? type,
        Guid? roomId,
        Guid? professionalId,
        IOperationalAlertReader reader,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryFilter(severity, type, roomId, professionalId, out var filter, out var error))
            return error!;
        var alerts = await reader.ReadAsync(filter!, timeProvider.GetUtcNow(), cancellationToken);
        return Results.Ok(new OperationalAlertSummaryResponse(
            alerts.Count,
            alerts.Count(item => item.Severity == OperationalAlertSeverity.Info),
            alerts.Count(item => item.Severity == OperationalAlertSeverity.Warning),
            alerts.Count(item => item.Severity == OperationalAlertSeverity.Critical)));
    }

    private static bool TryFilter(
        string? severity,
        string? type,
        Guid? roomId,
        Guid? professionalId,
        out OperationalAlertFilter? filter,
        out IResult? error)
    {
        filter = null;
        error = null;
        if (roomId == Guid.Empty || professionalId == Guid.Empty)
        {
            error = Results.BadRequest(new ApiError("INVALID_ALERT_FILTER", "O filtro informado é inválido."));
            return false;
        }
        if (!TrySeverity(severity, out var parsedSeverity))
        {
            error = Results.BadRequest(new ApiError("INVALID_ALERT_SEVERITY", "A severidade informada é inválida."));
            return false;
        }
        if (!TryType(type, out var parsedType))
        {
            error = Results.BadRequest(new ApiError("INVALID_ALERT_TYPE", "O tipo de alerta informado é inválido."));
            return false;
        }
        filter = new OperationalAlertFilter(parsedSeverity, parsedType, roomId, professionalId);
        return true;
    }

    private static bool TrySeverity(string? value, out OperationalAlertSeverity? severity)
    {
        severity = value?.Trim().ToUpperInvariant() switch
        {
            null or "" or "ALL" => null,
            "INFO" => OperationalAlertSeverity.Info,
            "WARNING" => OperationalAlertSeverity.Warning,
            "CRITICAL" => OperationalAlertSeverity.Critical,
            _ => (OperationalAlertSeverity?)(-1)
        };
        return severity != (OperationalAlertSeverity?)(-1);
    }

    private static bool TryType(string? value, out OperationalAlertType? type)
    {
        type = value?.Trim().ToUpperInvariant() switch
        {
            null or "" or "ALL" => null,
            "RESERVATION_ENDED_VISIT_WAITING" => OperationalAlertType.ReservationEndedVisitWaiting,
            "RESERVATION_ENDED_VISIT_IN_SERVICE" => OperationalAlertType.ReservationEndedVisitInService,
            "NEXT_RESERVATION_SOON" => OperationalAlertType.NextReservationSoon,
            "NEXT_RESERVATION_CONFLICT" => OperationalAlertType.NextReservationConflict,
            "LEASE_ENDING_WITH_ACTIVE_VISIT" => OperationalAlertType.LeaseEndingWithActiveVisit,
            _ => (OperationalAlertType?)(-1)
        };
        return type != (OperationalAlertType?)(-1);
    }

    private static OperationalAlertResponse Map(OperationalAlert alert) => new(
        alert.Id,
        TypeContract(alert.Type),
        alert.Severity.ToString().ToUpperInvariant(),
        alert.Title,
        alert.Message,
        alert.ConditionAt,
        alert.RoomId,
        alert.RoomName,
        alert.ProfessionalId,
        alert.ProfessionalName,
        alert.ReservationId,
        alert.VisitId,
        alert.LeaseId);

    private static string TypeContract(OperationalAlertType type) => type switch
    {
        OperationalAlertType.ReservationEndedVisitWaiting => "RESERVATION_ENDED_VISIT_WAITING",
        OperationalAlertType.ReservationEndedVisitInService => "RESERVATION_ENDED_VISIT_IN_SERVICE",
        OperationalAlertType.NextReservationSoon => "NEXT_RESERVATION_SOON",
        OperationalAlertType.NextReservationConflict => "NEXT_RESERVATION_CONFLICT",
        OperationalAlertType.LeaseEndingWithActiveVisit => "LEASE_ENDING_WITH_ACTIVE_VISIT",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
}

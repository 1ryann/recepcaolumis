using GestaoPredio.Application.Availability;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Availability;

internal static class AppointmentAvailabilityResults
{
    internal static IResult Conflict(AppointmentAvailabilityFailure failure) => failure switch
    {
        AppointmentAvailabilityFailure.OperatingHoursNotConfigured => Results.Json(new ApiError(
            "OPERATING_HOURS_NOT_CONFIGURED", "O horário de funcionamento ainda não foi configurado."),
            statusCode: StatusCodes.Status409Conflict),
        AppointmentAvailabilityFailure.ProfessionalUnavailable => Results.Json(new ApiError(
            "PROFESSIONAL_UNAVAILABLE", "O profissional não está disponível no período informado."),
            statusCode: StatusCodes.Status409Conflict),
        AppointmentAvailabilityFailure.RoomBlocked => Results.Json(new ApiError(
            "ROOM_BLOCKED", "A sala está bloqueada no período informado."),
            statusCode: StatusCodes.Status409Conflict),
        AppointmentAvailabilityFailure.RoomNotFound or AppointmentAvailabilityFailure.ProfessionalNotFound =>
            Results.BadRequest(new ApiError(
                "INVALID_RESERVATION_RESOURCE", "A sala ou o profissional informado é inválido.")),
        _ => Results.Json(new ApiError(
            "RESERVATION_RESOURCE_CONFLICT", "A sala ou o profissional já possui ocupação conflitante."),
            statusCode: StatusCodes.Status409Conflict)
    };
}

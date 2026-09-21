using GestaoPredio.Application.Availability;
using recepcaototem.Features.Common;
using recepcaototem.Features.Customers;

namespace recepcaototem.Features.Availability;

internal static class AppointmentAvailabilityResults
{
    // The availability engine has no clock: it answers "is this period free", not "can this still be booked". That
    // second question belongs to the customer-facing flows (portal, the WhatsApp reschedule link), which must never
    // offer or accept a slot that has already started. Staff flows deliberately stay free to record past periods, and
    // the kiosk's walk-in booking starts at "now" by design, so the rule is not pushed down into the engine.
    internal static IResult SlotInThePast() => Results.Json(new ApiError(
        "SLOT_IN_THE_PAST", "Esse horário já passou. Escolha um horário a partir de agora."),
        statusCode: StatusCodes.Status409Conflict);

    internal static bool HasStarted(DateTimeOffset startAt, DateTimeOffset now) => startAt <= now;

    internal static AvailabilitySlotResponse[] Upcoming(IEnumerable<AppointmentAvailabilitySlot> slots, DateTimeOffset now) =>
        slots.Where(slot => !HasStarted(slot.StartAt, now))
            .Select(slot => new AvailabilitySlotResponse(slot.StartAt, slot.EndAt))
            .ToArray();

    internal static IResult Conflict(AppointmentAvailabilityFailure failure) => failure switch
    {
        AppointmentAvailabilityFailure.OperatingHoursNotConfigured => Results.Json(new ApiError(
            "OPERATING_HOURS_NOT_CONFIGURED", "O horário de funcionamento ainda não foi configurado."),
            statusCode: StatusCodes.Status409Conflict),
        AppointmentAvailabilityFailure.ProfessionalUnavailable => Results.Json(new ApiError(
            "PROFESSIONAL_UNAVAILABLE", "O profissional não está disponível no período informado."),
            statusCode: StatusCodes.Status409Conflict),
        AppointmentAvailabilityFailure.OutsideOperatingHours => Results.Json(new ApiError(
            "ROOM_OUTSIDE_OPERATING_HOURS", "O período está fora do horário de funcionamento."),
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

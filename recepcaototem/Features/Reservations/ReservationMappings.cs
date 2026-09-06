using GestaoPredio.Domain.Reservations;
using recepcaototem.Features.Common;

namespace recepcaototem.Features.Reservations;

internal static class ReservationMappings
{
    public static string ToContract(this ReservationKind kind) => kind switch
    {
        ReservationKind.New => "NEW",
        ReservationKind.Reschedule => "RESCHEDULE",
        ReservationKind.Cancellation => "CANCELLATION",
        _ => throw new InvalidOperationException("Tipo de reserva desconhecido.")
    };

    public static string ToContract(this ReservationStatus status) => status switch
    {
        ReservationStatus.Pending => "PENDING",
        ReservationStatus.Approved => "APPROVED",
        ReservationStatus.Rejected => "REJECTED",
        ReservationStatus.Cancelled => "CANCELLED",
        _ => throw new InvalidOperationException("Status de reserva desconhecido.")
    };

    public static ReservationResponse ToResponse(
        this Reservation reservation,
        string roomName,
        string professionalName) => new(
            reservation.Id,
            reservation.RoomId,
            roomName,
            reservation.ProfessionalId,
            professionalName,
            reservation.OriginalReservationId,
            reservation.Kind.ToContract(),
            reservation.Status.ToContract(),
            reservation.StartAt,
            reservation.EndAt,
            reservation.RequestedAt,
            reservation.DecidedAt,
            reservation.RejectionReason,
            reservation.CreatedAt,
            reservation.UpdatedAt,
            ConcurrencyToken.Encode(reservation.Version));
}

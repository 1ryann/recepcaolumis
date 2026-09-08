namespace GestaoPredio.Application.Availability;

public enum AppointmentAvailabilityFailure
{
    None,
    OperatingHoursNotConfigured,
    ProfessionalNotFound,
    ProfessionalUnavailable,
    RoomNotFound,
    RoomBlocked,
    ResourceConflict
}

public sealed record AppointmentAvailabilitySlot(DateTimeOffset StartAt, DateTimeOffset EndAt);

public sealed record AppointmentAvailabilityResult(
    Guid? RoomId,
    AppointmentAvailabilityFailure Failure)
{
    public bool IsAvailable => Failure == AppointmentAvailabilityFailure.None && RoomId is not null;
}

public interface IAppointmentAvailabilityService
{
    Task<IReadOnlyList<AppointmentAvailabilitySlot>> FindSlotsAsync(
        Guid professionalId,
        DateOnly date,
        int durationMinutes,
        CancellationToken cancellationToken);

    Task<AppointmentAvailabilityResult> FindAvailableRoomAsync(
        Guid professionalId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        Guid? requiredRoomId,
        Guid? excludedReservationId,
        CancellationToken cancellationToken);
}

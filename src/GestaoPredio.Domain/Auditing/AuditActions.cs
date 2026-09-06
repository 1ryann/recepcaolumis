namespace GestaoPredio.Domain.Auditing;

public static class AuditActions
{
    public const string ProfessionalCreated = "PROFESSIONAL_CREATED";
    public const string ProfessionalUpdated = "PROFESSIONAL_UPDATED";
    public const string ProfessionalActivated = "PROFESSIONAL_ACTIVATED";
    public const string ProfessionalDeactivated = "PROFESSIONAL_DEACTIVATED";
    public const string ProfessionalPhotoUploaded = "PROFESSIONAL_PHOTO_UPLOADED";
    public const string ProfessionalPhotoReplaced = "PROFESSIONAL_PHOTO_REPLACED";
    public const string ProfessionalPhotoRemoved = "PROFESSIONAL_PHOTO_REMOVED";
    public const string ProfessionalUserLinked = "PROFESSIONAL_USER_LINKED";
    public const string ProfessionalUserUnlinked = "PROFESSIONAL_USER_UNLINKED";
    public const string ProfessionalUserReplaced = "PROFESSIONAL_USER_REPLACED";
    public const string RoomCreated = "ROOM_CREATED";
    public const string RoomUpdated = "ROOM_UPDATED";
    public const string RoomActivated = "ROOM_ACTIVATED";
    public const string RoomDeactivated = "ROOM_DEACTIVATED";
    public const string LeaseCreated = "LEASE_CREATED";
    public const string LeaseUpdated = "LEASE_UPDATED";
    public const string LeaseOccupancyPostponed = "LEASE_OCCUPANCY_POSTPONED";
    public const string LeaseCancelled = "LEASE_CANCELLED";
    public const string LeaseEndScheduled = "LEASE_END_SCHEDULED";
    public const string LeaseEndingPending = "LEASE_ENDING_PENDING";
    public const string LeaseEnded = "LEASE_ENDED";
    public const string ReservationCreated = "RESERVATION_CREATED";
    public const string ReservationRequested = "RESERVATION_REQUESTED";
    public const string ReservationApproved = "RESERVATION_APPROVED";
    public const string ReservationRejected = "RESERVATION_REJECTED";
    public const string ReservationCancelled = "RESERVATION_CANCELLED";
    public const string ReservationRescheduleRequested = "RESERVATION_RESCHEDULE_REQUESTED";
    public const string ReservationCancellationRequested = "RESERVATION_CANCELLATION_REQUESTED";
}

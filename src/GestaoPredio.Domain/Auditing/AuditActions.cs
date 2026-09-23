namespace GestaoPredio.Domain.Auditing;

public static class AuditActions
{
    public const string ProfessionalAvailabilityUpdated = "PROFESSIONAL_AVAILABILITY_UPDATED";
    public const string ProfessionalAvailabilityUpdatedByOperations = "PROFESSIONAL_AVAILABILITY_UPDATED_BY_OPERATIONS";
    public const string ProfessionalAvailabilityExceptionCreated = "PROFESSIONAL_AVAILABILITY_EXCEPTION_CREATED";
    public const string ProfessionalAvailabilityExceptionCreatedByOperations = "PROFESSIONAL_AVAILABILITY_EXCEPTION_CREATED_BY_OPERATIONS";
    public const string ProfessionalAvailabilityExceptionUpdated = "PROFESSIONAL_AVAILABILITY_EXCEPTION_UPDATED";
    public const string ProfessionalAvailabilityExceptionUpdatedByOperations = "PROFESSIONAL_AVAILABILITY_EXCEPTION_UPDATED_BY_OPERATIONS";
    public const string ProfessionalAvailabilityExceptionRemoved = "PROFESSIONAL_AVAILABILITY_EXCEPTION_REMOVED";
    public const string ProfessionalAvailabilityExceptionRemovedByOperations = "PROFESSIONAL_AVAILABILITY_EXCEPTION_REMOVED_BY_OPERATIONS";
    public static readonly string[] ProfessionalAvailabilityActions =
    [
        ProfessionalAvailabilityUpdated, ProfessionalAvailabilityUpdatedByOperations,
        ProfessionalAvailabilityExceptionCreated, ProfessionalAvailabilityExceptionCreatedByOperations,
        ProfessionalAvailabilityExceptionUpdated, ProfessionalAvailabilityExceptionUpdatedByOperations,
        ProfessionalAvailabilityExceptionRemoved, ProfessionalAvailabilityExceptionRemovedByOperations
    ];
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
    public const string ProfessionalPresenceQrIssued = "PROFESSIONAL_PRESENCE_QR_ISSUED";
    public const string ProfessionalPresenceStarted = "PROFESSIONAL_PRESENCE_STARTED";
    public const string ProfessionalPresenceStartedByOperations = "PROFESSIONAL_PRESENCE_STARTED_BY_OPERATIONS";
    public const string ProfessionalPresenceEndedByOperations = "PROFESSIONAL_PRESENCE_ENDED_BY_OPERATIONS";
    public const string ProfessionalIncidentReportedNextAppointment = "PROFESSIONAL_INCIDENT_REPORTED_NEXT_APPOINTMENT";
    public const string ProfessionalIncidentReportedUntilTime = "PROFESSIONAL_INCIDENT_REPORTED_UNTIL_TIME";
    public const string ProfessionalIncidentReportedRestOfDay = "PROFESSIONAL_INCIDENT_REPORTED_REST_OF_DAY";
    public const string ReservationCancelledProfessionalUnavailable = "RESERVATION_CANCELLED_PROFESSIONAL_UNAVAILABLE";
    public const string RescheduleLinkIssued = "RESCHEDULE_LINK_ISSUED";
    public const string RescheduleLinkConsumed = "RESCHEDULE_LINK_CONSUMED";
    public const string RoomCreated = "ROOM_CREATED";
    public const string RoomUpdated = "ROOM_UPDATED";
    public const string RoomActivated = "ROOM_ACTIVATED";
    public const string RoomDeactivated = "ROOM_DEACTIVATED";
    public const string RoomRentalInquiryCreated = "ROOM_RENTAL_INQUIRY_CREATED";
    public const string RoomRentalInquiryConverted = "ROOM_RENTAL_INQUIRY_CONVERTED";
    public const string RoomPhotoUploaded = "ROOM_PHOTO_UPLOADED";
    public const string RoomPhotoRemoved = "ROOM_PHOTO_REMOVED";
    public const string RoomPhotosReordered = "ROOM_PHOTOS_REORDERED";
    public const string RoomPhotoCoverChanged = "ROOM_PHOTO_COVER_CHANGED";
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
    public const string ReservationRescheduled = "RESERVATION_RESCHEDULED";
    public const string VisitArrived = "VISIT_ARRIVED";

    /// <summary>Arrival confirmed at the kiosk with a check-in credential.</summary>
    public const string VisitCheckedIn = "VISIT_CHECKED_IN";

    /// <summary>Arrival confirmed at the reception desk by a member of staff.</summary>
    public const string VisitCheckedInManual = "VISIT_CHECKED_IN_MANUAL";

    public const string VisitServiceStarted = "VISIT_SERVICE_STARTED";
    public const string VisitEnded = "VISIT_ENDED";
    public const string VisitCancelled = "VISIT_CANCELLED";
    public const string VisitCorrected = "VISIT_CORRECTED";
    public const string OperatingHoursUpdated = "OPERATING_HOURS_UPDATED";
    public const string RoomBlockCreated = "ROOM_BLOCK_CREATED";
    public const string RoomBlockUpdated = "ROOM_BLOCK_UPDATED";
    public const string RoomBlockCancelled = "ROOM_BLOCK_CANCELLED";
    public const string FinancialChargeMaterialized = "FINANCIAL_CHARGE_MATERIALIZED";
    public const string FinancialChargeAdjusted = "FINANCIAL_CHARGE_ADJUSTED";
    public const string FinancialChargePaid = "FINANCIAL_CHARGE_PAID";
    public const string FinancialChargeCancelled = "FINANCIAL_CHARGE_CANCELLED";
}

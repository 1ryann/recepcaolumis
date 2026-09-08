using GestaoPredio.Domain.Reservations;

namespace GestaoPredio.UnitTests;

public sealed class ReservationTests
{
    private static readonly Guid RoomId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ProfessionalId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string ProfessionalUserId = "professional-user";
    private const string ManagerUserId = "manager-user";
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Professional_new_request_starts_pending_and_requires_one_hour_notice()
    {
        var reservation = Reservation.RequestNew(RoomId, ProfessionalId, Now.AddHours(1), Now.AddHours(2),
            ProfessionalUserId, Now);

        Assert.Equal(ReservationKind.New, reservation.Kind);
        Assert.Equal(ReservationStatus.Pending, reservation.Status);
        Assert.Throws<ArgumentException>(() => Reservation.RequestNew(RoomId, ProfessionalId,
            Now.AddHours(1).AddTicks(-10), Now.AddHours(2), ProfessionalUserId, Now));
    }

    [Fact]
    public void Rejection_requires_a_reason_and_preserves_the_requested_period()
    {
        var reservation = Reservation.RequestNew(RoomId, ProfessionalId, Now.AddHours(2), Now.AddHours(3),
            ProfessionalUserId, Now);

        Assert.Throws<ArgumentException>(() => reservation.Reject(" ", ManagerUserId, Now.AddMinutes(1)));
        reservation.Reject("Sala indisponível para manutenção.", ManagerUserId, Now.AddMinutes(1));

        Assert.Equal(ReservationStatus.Rejected, reservation.Status);
        Assert.Equal("Sala indisponível para manutenção.", reservation.RejectionReason);
        Assert.Equal(Now.AddHours(2), reservation.StartAt);
        Assert.Equal(Now.AddHours(3), reservation.EndAt);
    }

    [Fact]
    public void Reschedule_request_keeps_original_approved_until_decision_and_uses_the_same_room()
    {
        var original = Reservation.CreateApproved(RoomId, ProfessionalId, Now.AddHours(3), Now.AddHours(4),
            ManagerUserId, Now);

        var request = Reservation.RequestReschedule(original, Now.AddHours(5), Now.AddHours(6),
            ProfessionalUserId, Now);

        Assert.Equal(ReservationStatus.Approved, original.Status);
        Assert.Equal(ReservationStatus.Pending, request.Status);
        Assert.Equal(ReservationKind.Reschedule, request.Kind);
        Assert.Equal(original.Id, request.OriginalReservationId);
        Assert.Equal(original.RoomId, request.RoomId);
    }

    [Fact]
    public void Cancellation_request_requires_one_hour_notice_and_does_not_cancel_before_approval()
    {
        var original = Reservation.CreateApproved(RoomId, ProfessionalId, Now.AddHours(1), Now.AddHours(2),
            ManagerUserId, Now);

        var request = Reservation.RequestCancellation(original, ProfessionalUserId, Now);

        Assert.Equal(ReservationKind.Cancellation, request.Kind);
        Assert.Equal(ReservationStatus.Pending, request.Status);
        Assert.Equal(ReservationStatus.Approved, original.Status);
        Assert.False(request.BlocksResources);
    }

    [Fact]
    public void Approved_and_cancelled_transitions_are_explicit_and_terminal()
    {
        var reservation = Reservation.RequestNew(RoomId, ProfessionalId, Now.AddHours(2), Now.AddHours(3),
            ProfessionalUserId, Now);

        reservation.Approve(ManagerUserId, Now.AddMinutes(5));
        Assert.Equal(ReservationStatus.Approved, reservation.Status);
        Assert.True(reservation.BlocksResources);

        reservation.Cancel(ManagerUserId, Now.AddMinutes(10));
        Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
        Assert.False(reservation.BlocksResources);
        Assert.Throws<InvalidOperationException>(() => reservation.Approve(ManagerUserId, Now.AddMinutes(11)));
    }

    [Fact]
    public void Administrative_reschedule_is_approved_and_preserves_the_original_identity()
    {
        var original = Reservation.CreateApproved(RoomId, ProfessionalId, Now.AddHours(3), Now.AddHours(4),
            ManagerUserId, Now);

        var replacement = Reservation.CreateApprovedReschedule(
            original, Now.AddHours(5), Now.AddHours(6), ManagerUserId, Now.AddMinutes(1));

        Assert.Equal(ReservationKind.Reschedule, replacement.Kind);
        Assert.Equal(ReservationStatus.Approved, replacement.Status);
        Assert.Equal(original.Id, replacement.OriginalReservationId);
        Assert.Equal(original.RoomId, replacement.RoomId);
        Assert.Equal(original.ProfessionalId, replacement.ProfessionalId);
        Assert.Equal(ReservationStatus.Approved, original.Status);
    }

    private static readonly Guid CustomerId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ReplacementRoomId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static Reservation ApprovedCustomerReservation() =>
        Reservation.CreateApproved(RoomId, ProfessionalId, Now.AddHours(3), Now.AddHours(4), ManagerUserId, Now, CustomerId);

    [Fact]
    public void Cancel_defaults_to_no_recorded_reason()
    {
        var reservation = ApprovedCustomerReservation();
        reservation.Cancel(ManagerUserId, Now.AddMinutes(1));
        Assert.Equal(ReservationCancellationReason.None, reservation.CancellationReason);
        Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
    }

    [Fact]
    public void Cancel_records_the_professional_unavailable_reason()
    {
        var reservation = ApprovedCustomerReservation();
        reservation.Cancel("PROFESSIONAL_INCIDENT", Now.AddMinutes(1), ReservationCancellationReason.ProfessionalUnavailable);
        Assert.Equal(ReservationCancellationReason.ProfessionalUnavailable, reservation.CancellationReason);
    }

    [Fact]
    public void Incident_replacement_requires_a_cancelled_unavailable_original()
    {
        var approved = ApprovedCustomerReservation();
        Assert.Throws<InvalidOperationException>(() => Reservation.CreateApprovedReplacementForIncident(
            approved, ReplacementRoomId, Now.AddDays(1), Now.AddDays(1).AddHours(1), "RESCHEDULE_LINK", Now));

        approved.Cancel("PROFESSIONAL_INCIDENT", Now, ReservationCancellationReason.ProfessionalUnavailable);
        var replacement = Reservation.CreateApprovedReplacementForIncident(
            approved, ReplacementRoomId, Now.AddDays(1), Now.AddDays(1).AddHours(1), "RESCHEDULE_LINK", Now);

        Assert.Equal(ReservationKind.Reschedule, replacement.Kind);
        Assert.Equal(ReservationStatus.Approved, replacement.Status);
        Assert.Equal(ReplacementRoomId, replacement.RoomId);
        Assert.Equal(approved.Id, replacement.OriginalReservationId);
        Assert.Equal(CustomerId, replacement.CustomerId);
        Assert.Equal(ProfessionalId, replacement.ProfessionalId);
    }

    [Fact]
    public void Incident_replacement_rejects_a_normally_cancelled_original()
    {
        var reservation = ApprovedCustomerReservation();
        reservation.Cancel(ManagerUserId, Now);
        Assert.Throws<InvalidOperationException>(() => Reservation.CreateApprovedReplacementForIncident(
            reservation, ReplacementRoomId, Now.AddDays(1), Now.AddDays(1).AddHours(1), "RESCHEDULE_LINK", Now));
    }
}

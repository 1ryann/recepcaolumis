using GestaoPredio.Domain.Notifications;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Visits;
using GestaoPredio.Domain.Whatsapp;

namespace GestaoPredio.UnitTests;

public sealed class WhatsAppNotificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Client_checked_in_targets_the_professional_and_is_keyed_by_the_visit()
    {
        var reservationId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var visit = Visit.Arrive(Guid.NewGuid(), Guid.NewGuid(), reservationId, "Ana Souza", "TOTEM", Now, customerId);

        var notification = WhatsAppNotification.ClientCheckedIn(visit, Now);

        Assert.Equal(WhatsAppNotificationType.ClientCheckedIn, notification.Type);
        Assert.Equal(WhatsAppNotificationRecipient.Professional, notification.Recipient);
        Assert.Equal($"CHECKIN:{visit.Id}", notification.IdempotencyKey);
        Assert.Equal(visit.ProfessionalId, notification.ProfessionalId);
        Assert.Equal(reservationId, notification.ReservationId);
        Assert.Equal(visit.Id, notification.VisitId);
        Assert.Equal(customerId, notification.CustomerId);
        Assert.Equal(WhatsAppNotificationStatus.Pending, notification.Status);
        Assert.Equal(0, notification.Attempts);
        Assert.Equal(Now, notification.NextAttemptAt);
        Assert.Null(notification.MessageId);
        Assert.Null(notification.LastErrorCode);
    }

    [Fact]
    public void A_cancellation_without_a_customer_has_no_one_to_notify()
    {
        var reservation = Reservation.CreateApproved(Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(1), Now.AddDays(1).AddHours(1), "admin", Now);
        reservation.Cancel("admin", Now);

        Assert.Null(WhatsAppNotification.ReservationCancelled(reservation, Now));
    }

    [Theory]
    [InlineData(ReservationCancellationReason.ProfessionalUnavailable, WhatsAppNotificationType.ProfessionalCancelled)]
    [InlineData(ReservationCancellationReason.None, WhatsAppNotificationType.AppointmentCancelled)]
    public void A_cancellation_targets_the_customer_with_the_type_matching_its_reason(
        ReservationCancellationReason reason, WhatsAppNotificationType expected)
    {
        var customerId = Guid.NewGuid();
        var reservation = Reservation.CreateApproved(Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(1), Now.AddDays(1).AddHours(1), "admin", Now, customerId);
        reservation.Cancel("actor", Now, reason);

        var notification = WhatsAppNotification.ReservationCancelled(reservation, Now)!;

        Assert.Equal(expected, notification.Type);
        Assert.Equal(WhatsAppNotificationRecipient.Customer, notification.Recipient);
        Assert.Equal($"CANCEL:{reservation.Id}", notification.IdempotencyKey);
        Assert.Equal(customerId, notification.CustomerId);
        Assert.Equal(reservation.ProfessionalId, notification.ProfessionalId);
    }

    [Fact]
    public void An_uncancelled_reservation_cannot_produce_a_cancellation_notification()
    {
        var reservation = Reservation.CreateApproved(Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(1), Now.AddDays(1).AddHours(1), "admin", Now, Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => WhatsAppNotification.ReservationCancelled(reservation, Now));
    }

    [Fact]
    public void A_reschedule_is_keyed_by_the_replacement_reservation()
    {
        var original = Reservation.CreateApproved(Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(1), Now.AddDays(1).AddHours(1), "admin", Now, Guid.NewGuid());
        var replacement = Reservation.CreateApprovedReschedule(original, Now.AddDays(2), Now.AddDays(2).AddHours(1), "admin", Now);

        var notification = WhatsAppNotification.AppointmentRescheduled(replacement, Now)!;

        Assert.Equal(WhatsAppNotificationType.AppointmentRescheduled, notification.Type);
        Assert.Equal($"RESCHEDULE:{replacement.Id}", notification.IdempotencyKey);
        Assert.Equal(replacement.Id, notification.ReservationId);
        Assert.Equal(original.CustomerId, notification.CustomerId);
    }

    [Fact]
    public void A_confirmation_is_keyed_by_the_reservation_and_needs_a_customer()
    {
        var withCustomer = Reservation.CreateApproved(Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(1), Now.AddDays(1).AddHours(1), "c", Now, Guid.NewGuid());
        var withoutCustomer = Reservation.CreateApproved(Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(1), Now.AddDays(1).AddHours(1), "a", Now);

        Assert.Equal($"CONFIRM:{withCustomer.Id}", WhatsAppNotification.AppointmentConfirmed(withCustomer, Now)!.IdempotencyKey);
        Assert.Null(WhatsAppNotification.AppointmentConfirmed(withoutCustomer, Now));
    }

    [Fact]
    public void A_delay_is_keyed_by_the_reservation_and_the_repeat_step()
    {
        var reservation = Reservation.CreateApproved(Guid.NewGuid(), Guid.NewGuid(), Now, Now.AddHours(1), "c", Now.AddDays(-1), Guid.NewGuid());

        var notification = WhatsAppNotification.ProfessionalDelayed(reservation, 1, Now);

        Assert.Equal(WhatsAppNotificationType.ProfessionalDelayed, notification.Type);
        Assert.Equal($"DELAY:{reservation.Id}:1", notification.IdempotencyKey);
        Assert.Equal(WhatsAppNotificationRecipient.Customer, notification.Recipient);
    }

    [Fact]
    public void Accepted_records_the_wamid_and_clears_the_error()
    {
        var notification = Sending();
        notification.ScheduleRetry("WHATSAPP_PROVIDER_UNAVAILABLE", Now.AddSeconds(30), Now);
        notification.Claim(Now.AddSeconds(30), Now.AddMinutes(2));
        notification.BeginSend(Now.AddSeconds(30), Now.AddSeconds(120));

        notification.MarkAccepted("wamid.1", Now.AddSeconds(31));

        Assert.Equal(WhatsAppNotificationStatus.Accepted, notification.Status);
        Assert.Equal("wamid.1", notification.MessageId);
        Assert.Null(notification.LastErrorCode);
        Assert.Null(notification.LockedUntil);
        Assert.Equal(2, notification.Attempts);
    }

    [Fact]
    public void A_retry_returns_to_pending_with_the_error_code_and_next_attempt()
    {
        var notification = Sending();

        notification.ScheduleRetry("WHATSAPP_PROVIDER_UNAVAILABLE", Now.AddMinutes(2), Now);

        Assert.Equal(WhatsAppNotificationStatus.Pending, notification.Status);
        Assert.Equal("WHATSAPP_PROVIDER_UNAVAILABLE", notification.LastErrorCode);
        Assert.Equal(Now.AddMinutes(2), notification.NextAttemptAt);
        Assert.Null(notification.LockedUntil);
    }

    [Fact]
    public void A_failure_before_the_send_started_can_also_be_retried()
    {
        var notification = Claimed();

        notification.ScheduleRetry("DISPATCH_ERROR", Now.AddSeconds(30), Now);

        Assert.Equal(WhatsAppNotificationStatus.Pending, notification.Status);
    }

    [Fact]
    public void Begin_send_marks_the_point_of_no_return_with_a_lease_for_the_http_call()
    {
        var notification = Claimed();

        notification.BeginSend(Now.AddSeconds(5), Now.AddSeconds(95));

        Assert.Equal(WhatsAppNotificationStatus.Sending, notification.Status);
        Assert.Equal(Now.AddSeconds(95), notification.LockedUntil);
        Assert.Equal(1, notification.Attempts);
        Assert.Throws<InvalidOperationException>(() => notification.BeginSend(Now, Now.AddSeconds(90)));
        Assert.Throws<InvalidOperationException>(() => notification.Claim(Now.AddHours(1), Now.AddHours(2)));
    }

    [Fact]
    public void A_notification_must_be_claimed_before_its_send_begins()
    {
        var pending = WhatsAppNotification.ClientCheckedIn(Visit.Arrive(Guid.NewGuid(), null, null, "Ana", "A", Now), Now);

        Assert.Throws<InvalidOperationException>(() => pending.BeginSend(Now, Now.AddSeconds(90)));
    }

    [Fact]
    public void An_unknown_outcome_waits_for_evidence_and_is_never_retried()
    {
        var notification = Sending();

        notification.MarkUnconfirmed("WHATSAPP_TIMEOUT", Now.AddMinutes(15), Now);

        Assert.Equal(WhatsAppNotificationStatus.Unconfirmed, notification.Status);
        Assert.Equal("WHATSAPP_TIMEOUT", notification.LastErrorCode);
        Assert.Equal(Now.AddMinutes(15), notification.NextAttemptAt);
        Assert.Null(notification.LockedUntil);
        Assert.Null(notification.MessageId);
        Assert.False(notification.IsTerminal);
        Assert.Throws<InvalidOperationException>(() => notification.ScheduleRetry("X", Now, Now));
        Assert.Throws<InvalidOperationException>(() => notification.Claim(Now.AddHours(1), Now.AddHours(2)));
        Assert.Throws<InvalidOperationException>(() => notification.BeginSend(Now, Now.AddSeconds(90)));
    }

    [Fact]
    public void Only_a_send_in_flight_can_become_unconfirmed()
    {
        Assert.Throws<InvalidOperationException>(() => Claimed().MarkUnconfirmed("WHATSAPP_TIMEOUT", Now.AddMinutes(15), Now));
    }

    [Fact]
    public void Webhook_evidence_attaches_the_wamid_of_a_send_whose_answer_was_lost()
    {
        var unconfirmed = Sending();
        unconfirmed.MarkUnconfirmed("WHATSAPP_TIMEOUT", Now.AddMinutes(15), Now);
        var inFlight = Sending();

        Assert.True(unconfirmed.AttachMessageId("wamid.late", Now.AddSeconds(3)));
        Assert.True(inFlight.AttachMessageId("wamid.early", Now.AddSeconds(1)));

        Assert.Equal(WhatsAppNotificationStatus.Accepted, unconfirmed.Status);
        Assert.Equal("wamid.late", unconfirmed.MessageId);
        Assert.Null(unconfirmed.LastErrorCode);
        Assert.Equal(WhatsAppNotificationStatus.Accepted, inFlight.Status);
        Assert.True(unconfirmed.ApplyDeliveryStatus(WhatsAppDeliveryStatus.Sent, null, Now.AddSeconds(3)));
        Assert.Equal(WhatsAppNotificationStatus.Sent, unconfirmed.Status);
    }

    [Fact]
    public void Webhook_evidence_never_overrides_a_known_wamid_nor_a_notice_that_was_not_sent()
    {
        var accepted = Sending();
        accepted.MarkAccepted("wamid.first", Now);
        var pending = WhatsAppNotification.ClientCheckedIn(Visit.Arrive(Guid.NewGuid(), null, null, "Ana", "A", Now), Now);

        Assert.False(accepted.AttachMessageId("wamid.other", Now));
        Assert.Equal("wamid.first", accepted.MessageId);
        Assert.False(pending.AttachMessageId("wamid.other", Now));
        Assert.False(Claimed().AttachMessageId("wamid.other", Now));
    }

    [Fact]
    public void The_late_answer_of_the_same_attempt_still_records_the_wamid()
    {
        var notification = Sending();
        notification.MarkUnconfirmed("WHATSAPP_OUTCOME_UNKNOWN", Now.AddMinutes(15), Now);

        notification.MarkAccepted("wamid.same-attempt", Now.AddSeconds(40));

        Assert.Equal(WhatsAppNotificationStatus.Accepted, notification.Status);
        Assert.Equal("wamid.same-attempt", notification.MessageId);
    }

    [Fact]
    public void Without_evidence_an_unconfirmed_send_ends_failed_with_an_explicit_unknown_outcome()
    {
        var notification = Sending();
        notification.MarkUnconfirmed("WHATSAPP_TIMEOUT", Now.AddMinutes(15), Now);

        notification.MarkFailed("WHATSAPP_OUTCOME_UNKNOWN", Now.AddMinutes(15));

        Assert.Equal(WhatsAppNotificationStatus.Failed, notification.Status);
        Assert.Equal("WHATSAPP_OUTCOME_UNKNOWN", notification.LastErrorCode);
        Assert.Null(notification.MessageId);
    }

    [Fact]
    public void The_webhook_callback_data_identifies_the_notification_without_personal_data()
    {
        var notification = Claimed();

        Assert.Equal($"lumis-notification:{notification.Id:N}", notification.CallbackData);
        Assert.True(WhatsAppNotification.TryParseCallbackData(notification.CallbackData, out var id));
        Assert.Equal(notification.Id, id);
        Assert.False(WhatsAppNotification.TryParseCallbackData(null, out _));
        Assert.False(WhatsAppNotification.TryParseCallbackData("campaign-42", out _));
        Assert.False(WhatsAppNotification.TryParseCallbackData("lumis-notification:not-a-guid", out _));
    }

    [Fact]
    public void Failed_and_skipped_are_terminal()
    {
        var failed = Claimed();
        failed.MarkFailed("RECIPIENT_PHONE_INVALID", Now);
        var skipped = Claimed();
        skipped.MarkSkipped("OBSOLETE", Now);

        Assert.Equal(WhatsAppNotificationStatus.Failed, failed.Status);
        Assert.Equal(WhatsAppNotificationStatus.Skipped, skipped.Status);
        Assert.Throws<InvalidOperationException>(() => failed.ScheduleRetry("X", Now, Now));
        Assert.Throws<InvalidOperationException>(() => skipped.MarkAccepted("wamid.2", Now));
        Assert.Throws<InvalidOperationException>(() => failed.Claim(Now, Now.AddMinutes(1)));
    }

    [Fact]
    public void Only_a_send_in_flight_can_be_accepted_and_only_a_claim_can_be_retried()
    {
        var pending = WhatsAppNotification.ClientCheckedIn(Visit.Arrive(Guid.NewGuid(), null, null, "Ana", "A", Now), Now);

        Assert.Throws<InvalidOperationException>(() => pending.MarkAccepted("wamid.3", Now));
        Assert.Throws<InvalidOperationException>(() => pending.ScheduleRetry("X", Now, Now));
        Assert.Throws<InvalidOperationException>(() => Claimed().MarkAccepted("wamid.3", Now));
    }

    [Fact]
    public void Delivery_status_only_moves_forward_and_failure_is_terminal()
    {
        var notification = Sending();
        notification.MarkAccepted("wamid.4", Now);

        Assert.True(notification.ApplyDeliveryStatus(WhatsAppDeliveryStatus.Delivered, null, Now));
        Assert.False(notification.ApplyDeliveryStatus(WhatsAppDeliveryStatus.Sent, null, Now));      // out of order
        Assert.False(notification.ApplyDeliveryStatus(WhatsAppDeliveryStatus.Delivered, null, Now)); // replay
        Assert.True(notification.ApplyDeliveryStatus(WhatsAppDeliveryStatus.Read, null, Now));
        Assert.Equal(WhatsAppNotificationStatus.Read, notification.Status);

        var failing = Sending();
        failing.MarkAccepted("wamid.5", Now);
        Assert.True(failing.ApplyDeliveryStatus(WhatsAppDeliveryStatus.Failed, 131026, Now));
        Assert.Equal(WhatsAppNotificationStatus.Failed, failing.Status);
        Assert.Equal("WHATSAPP_DELIVERY_FAILED:131026", failing.LastErrorCode);
        Assert.False(failing.ApplyDeliveryStatus(WhatsAppDeliveryStatus.Read, null, Now));
    }

    [Fact]
    public void Delivery_status_is_ignored_until_a_wamid_is_known()
    {
        var claimed = Claimed();
        var sending = Sending();
        var unconfirmed = Sending();
        unconfirmed.MarkUnconfirmed("WHATSAPP_TIMEOUT", Now.AddMinutes(15), Now);

        foreach (var notification in new[] { claimed, sending, unconfirmed })
        {
            var before = notification.Status;
            Assert.False(notification.ApplyDeliveryStatus(WhatsAppDeliveryStatus.Delivered, null, Now));
            Assert.False(notification.ApplyDeliveryStatus(WhatsAppDeliveryStatus.Failed, 131026, Now));
            Assert.Equal(before, notification.Status);
        }
    }

    private static WhatsAppNotification Sending()
    {
        var notification = Claimed();
        notification.BeginSend(Now, Now.AddSeconds(90));
        return notification;
    }

    private static WhatsAppNotification Claimed()
    {
        var notification = WhatsAppNotification.ClientCheckedIn(Visit.Arrive(Guid.NewGuid(), null, null, "Ana", "A", Now), Now);
        notification.Claim(Now, Now.AddMinutes(2));
        return notification;
    }
}

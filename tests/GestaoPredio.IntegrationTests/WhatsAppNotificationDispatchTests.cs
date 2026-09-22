using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Application.Whatsapp;
using GestaoPredio.Domain.Customers;
using GestaoPredio.Domain.Notifications;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Security;
using GestaoPredio.Domain.Visits;
using GestaoPredio.Domain.Whatsapp;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class WhatsAppNotificationDispatchTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";
    private const string ProfessionalPhone = "+5569981112222";
    private const string CustomerPhone = "+5569983334444";

    // ---- CLIENT_CHECKED_IN ------------------------------------------------------------------------------------

    [Fact]
    public async Task Check_in_notice_goes_to_the_professional_as_a_template_with_operational_data_only()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromMinutes(5));
        await AddAsync(db => Visit.Arrive(seed.ProfessionalId, seed.RoomId, seed.ReservationId, "Maria Clara Souza", "TOTEM", factory.UtcNow, seed.CustomerId),
            visit => WhatsAppNotification.ClientCheckedIn(visit, factory.UtcNow));
        var meta = new FakeWhatsAppService();
        using var host = factory.WithWhatsApp(meta);

        var summary = await ModulesApiFactory.DispatchAsync(host);

        Assert.Equal(1, summary.Accepted);
        var (phone, template) = Assert.Single(meta.Sent);
        Assert.Equal(ProfessionalPhone, phone);
        Assert.Equal("professional_client_checked_in", template.Name);
        Assert.Equal("pt_BR", template.LanguageCode);
        // Professional's name, the client's FIRST name only, and the appointment time in Porto Velho (UTC-4).
        Assert.Equal(["Dra. Helena Prado", "Maria", LocalTime(seed.StartAt)], template.BodyParameters);
        Assert.Null(template.UrlButtonParameter);
        var notice = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(WhatsAppNotificationStatus.Accepted, notice.Status);
        Assert.Equal("wamid.fake.1", notice.MessageId);
        Assert.Equal(1, notice.Attempts);
        // The only thing Meta echoes back for correlation is the notification id.
        Assert.Equal($"lumis-notification:{notice.Id:N}", Assert.Single(meta.CallbackData));
    }

    [Fact]
    public async Task A_second_dispatch_never_resends_an_accepted_notice()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromMinutes(5));
        await AddCheckInAsync(seed);
        var meta = new FakeWhatsAppService();
        using var host = factory.WithWhatsApp(meta);

        await ModulesApiFactory.DispatchAsync(host);
        await ModulesApiFactory.DispatchAsync(host);
        factory.AdvanceTime(TimeSpan.FromMinutes(5));
        await ModulesApiFactory.DispatchAsync(host);

        Assert.Single(meta.Sent);
    }

    [Fact]
    public async Task The_same_business_event_cannot_be_queued_twice()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromMinutes(5));
        var visit = await AddCheckInAsync(seed);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.WhatsAppNotifications.Add(WhatsAppNotification.ClientCheckedIn(visit, factory.UtcNow));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Single(await factory.NotificationsAsync());
    }

    [Fact]
    public async Task A_check_in_notice_for_a_client_already_in_service_is_skipped_as_obsolete()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromMinutes(5));
        var visit = await AddCheckInAsync(seed);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Visits.SingleAsync(x => x.Id == visit.Id)).StartService("prof", factory.UtcNow);
            await db.SaveChangesAsync();
        }
        var meta = new FakeWhatsAppService();
        using var host = factory.WithWhatsApp(meta);

        await ModulesApiFactory.DispatchAsync(host);

        Assert.Empty(meta.Sent);
        var notice = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(WhatsAppNotificationStatus.Skipped, notice.Status);
        Assert.Equal("OBSOLETE", notice.LastErrorCode);
    }

    // ---- recipient problems never break anything ---------------------------------------------------------------

    [Fact]
    public async Task An_invalid_customer_phone_fails_the_notice_without_retrying_and_leaves_the_reservation_alone()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromHours(3));
        await ExecuteAsync($"UPDATE \"Customers\" SET \"Phone\" = '123' WHERE \"Id\" = '{seed.CustomerId}'");
        await AddReservationNoticeAsync(seed, WhatsAppNotification.AppointmentConfirmed);
        var meta = new FakeWhatsAppService();
        using var host = factory.WithWhatsApp(meta);

        await ModulesApiFactory.DispatchAsync(host);
        factory.AdvanceTime(TimeSpan.FromHours(1));
        await ModulesApiFactory.DispatchAsync(host);

        Assert.Empty(meta.Sent);
        var notice = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(WhatsAppNotificationStatus.Failed, notice.Status);
        Assert.Equal("RECIPIENT_PHONE_INVALID", notice.LastErrorCode);
        Assert.Equal(1, notice.Attempts);
        Assert.Equal(ReservationStatus.Approved, (await ReservationAsync(seed.ReservationId)).Status);
    }

    [Fact]
    public async Task An_inactive_customer_is_reported_as_an_unavailable_recipient()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromHours(3));
        await ExecuteAsync($"UPDATE \"Customers\" SET \"IsActive\" = false WHERE \"Id\" = '{seed.CustomerId}'");
        await AddReservationNoticeAsync(seed, WhatsAppNotification.AppointmentConfirmed);
        using var host = factory.WithWhatsApp(new FakeWhatsAppService());

        await ModulesApiFactory.DispatchAsync(host);

        var notice = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(WhatsAppNotificationStatus.Failed, notice.Status);
        Assert.Equal("RECIPIENT_UNAVAILABLE", notice.LastErrorCode);
    }

    [Fact]
    public async Task A_type_without_a_configured_template_is_skipped_and_never_sent_as_free_text()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromHours(3));
        await AddReservationNoticeAsync(seed, WhatsAppNotification.AppointmentConfirmed);
        var meta = new FakeWhatsAppService();
        using var host = factory.WithWhatsApp(meta, ("Whatsapp:Templates:AppointmentConfirmed", ""));

        await ModulesApiFactory.DispatchAsync(host);

        Assert.Empty(meta.Sent);
        var notice = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(WhatsAppNotificationStatus.Skipped, notice.Status);
        Assert.Equal("TEMPLATE_NOT_CONFIGURED", notice.LastErrorCode);
    }

    [Fact]
    public async Task A_notice_older_than_its_useful_life_expires_instead_of_being_sent_late()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromHours(2));
        await AddCheckInAsync(seed);
        factory.AdvanceTime(TimeSpan.FromMinutes(31));
        var meta = new FakeWhatsAppService();
        using var host = factory.WithWhatsApp(meta);

        await ModulesApiFactory.DispatchAsync(host);

        Assert.Empty(meta.Sent);
        var notice = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(WhatsAppNotificationStatus.Skipped, notice.Status);
        Assert.Equal("EXPIRED", notice.LastErrorCode);
    }

    // ---- retry policy -----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_temporary_meta_error_is_retried_with_backoff_until_it_is_accepted()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromHours(3));
        await AddCheckInAsync(seed);
        var meta = new FakeWhatsAppService().Then(
            WhatsAppSendResult.Failed(WhatsAppFailureCodes.ProviderUnavailable, 131000),
            WhatsAppSendResult.Failed(WhatsAppFailureCodes.RateLimited, 130429));
        using var host = factory.WithWhatsApp(meta);
        var start = factory.UtcNow;

        await ModulesApiFactory.DispatchAsync(host);
        var first = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(WhatsAppNotificationStatus.Pending, first.Status);
        Assert.Equal(WhatsAppFailureCodes.ProviderUnavailable, first.LastErrorCode);
        Assert.Equal(start.AddSeconds(30), first.NextAttemptAt);

        await ModulesApiFactory.DispatchAsync(host);                 // not due yet: nothing happens
        Assert.Single(meta.Sent);

        factory.AdvanceTime(TimeSpan.FromSeconds(30));
        await ModulesApiFactory.DispatchAsync(host);                 // 2nd attempt: rate limited → wait 2 min
        Assert.Equal(factory.UtcNow.AddMinutes(2), Assert.Single(await factory.NotificationsAsync()).NextAttemptAt);

        factory.AdvanceTime(TimeSpan.FromMinutes(2));
        await ModulesApiFactory.DispatchAsync(host);                 // 3rd attempt: accepted

        Assert.Equal(3, meta.Sent.Count);
        var done = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(WhatsAppNotificationStatus.Accepted, done.Status);
        Assert.Equal(3, done.Attempts);
        Assert.Null(done.LastErrorCode);
    }

    [Fact]
    public async Task A_permanent_meta_error_fails_at_once_without_retry()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromHours(3));
        await AddCheckInAsync(seed);
        var meta = new FakeWhatsAppService().Then(WhatsAppSendResult.Failed(WhatsAppFailureCodes.RequestRejected, 132001));
        using var host = factory.WithWhatsApp(meta);

        await ModulesApiFactory.DispatchAsync(host);
        factory.AdvanceTime(TimeSpan.FromHours(1));
        await ModulesApiFactory.DispatchAsync(host);

        Assert.Single(meta.Sent);
        var notice = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(WhatsAppNotificationStatus.Failed, notice.Status);
        Assert.Equal(WhatsAppFailureCodes.RequestRejected, notice.LastErrorCode);
    }

    [Fact]
    public async Task Temporary_errors_stop_after_the_configured_number_of_attempts()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromHours(3));
        await AddCheckInAsync(seed);
        var unavailable = WhatsAppSendResult.Failed(WhatsAppFailureCodes.ProviderUnavailable);
        var meta = new FakeWhatsAppService().Then(unavailable, unavailable, unavailable, unavailable, unavailable);
        using var host = factory.WithWhatsApp(meta, ("Whatsapp:Notifications:MaxAttempts", "3"));

        for (var cycle = 0; cycle < 6; cycle++)
        {
            await ModulesApiFactory.DispatchAsync(host);
            factory.AdvanceTime(TimeSpan.FromMinutes(15));
        }

        Assert.Equal(3, meta.Sent.Count);
        var notice = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(WhatsAppNotificationStatus.Failed, notice.Status);
        Assert.Equal(3, notice.Attempts);
    }

    // ---- concurrency ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Two_dispatchers_running_at_once_send_each_notice_exactly_once()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromHours(3));
        await AddCheckInAsync(seed);
        for (var i = 0; i < 4; i++)
        {
            var extra = await SeedAsync(startIn: TimeSpan.FromHours(4 + i));
            await AddCheckInAsync(extra);
        }
        var meta = new FakeWhatsAppService { Delay = TimeSpan.FromMilliseconds(150) };
        using var host = factory.WithWhatsApp(meta);

        var results = await Task.WhenAll(ModulesApiFactory.DispatchAsync(host), ModulesApiFactory.DispatchAsync(host));

        Assert.Equal(5, results.Sum(x => x.Claimed));
        Assert.Equal(5, meta.Sent.Count);
        Assert.All(await factory.NotificationsAsync(), x => Assert.Equal(WhatsAppNotificationStatus.Accepted, x.Status));
    }

    [Fact]
    public async Task A_send_in_flight_is_never_repeated_by_another_dispatcher_even_after_its_lease_expired()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromHours(3));
        await AddCheckInAsync(seed);
        var atMeta = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = new FakeWhatsAppService
        {
            OnSend = async _ => { atMeta.SetResult(); await answer.Task; }
        }.Then(WhatsAppSendResult.Succeeded("wamid.slow"));
        var other = new FakeWhatsAppService();
        using var hostA = factory.WithWhatsApp(slow);
        using var hostB = factory.WithWhatsApp(other);

        // A is inside the Cloud API call; its request may already be at Meta.
        var dispatchA = ModulesApiFactory.DispatchAsync(hostA);
        await atMeta.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(WhatsAppNotificationStatus.Sending, await StatusAsync());

        // A stalls far beyond its lease. B takes over the expired lease but must not call Meta again.
        factory.AdvanceTime(TimeSpan.FromMinutes(5));
        var summaryB = await ModulesApiFactory.DispatchAsync(hostB);
        Assert.Empty(other.Sent);
        Assert.Equal(0, summaryB.Accepted);
        Assert.Equal(1, summaryB.Unconfirmed);
        var parked = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(WhatsAppNotificationStatus.Unconfirmed, parked.Status);
        Assert.Equal(1, parked.Attempts);

        // A's answer finally arrives: it still records the wamid of the one message that was sent.
        answer.SetResult();
        var summaryA = await dispatchA.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(1, summaryA.Accepted);
        Assert.Single(slow.Sent);
        var notice = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(WhatsAppNotificationStatus.Accepted, notice.Status);
        Assert.Equal("wamid.slow", notice.MessageId);
    }

    [Fact]
    public async Task A_dispatcher_that_lost_its_claim_can_no_longer_write_so_it_cannot_start_a_send()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromHours(3));
        await AddCheckInAsync(seed);
        var id = Assert.Single(await factory.NotificationsAsync()).Id;
        await MutateAsync(id, n => n.Claim(factory.UtcNow, factory.UtcNow.AddMinutes(2)));   // claim #1

        // Dispatcher #1 still holds its in-memory copy of claim #1 ...
        await using var staleScope = factory.Services.CreateAsyncScope();
        var staleDb = staleScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stale = await staleDb.WhatsAppNotifications.SingleAsync(x => x.Id == id);

        // ... while its lease expires and dispatcher #2 claims the notification again.
        factory.AdvanceTime(TimeSpan.FromMinutes(3));
        await MutateAsync(id, n => n.Claim(factory.UtcNow, factory.UtcNow.AddMinutes(2)));   // claim #2

        stale.BeginSend(factory.UtcNow, factory.UtcNow.AddSeconds(90));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleDb.SaveChangesAsync());
        var notice = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(WhatsAppNotificationStatus.Processing, notice.Status);
        Assert.Equal(2, notice.Attempts);
    }

    [Fact]
    public async Task Each_notice_is_claimed_only_when_its_turn_comes_so_a_long_batch_never_outlives_a_lease()
    {
        await factory.ResetAsync();
        for (var i = 0; i < 3; i++) await AddCheckInAsync(await SeedAsync(startIn: TimeSpan.FromHours(3 + i)));
        var observed = new List<(int InFlight, int Untouched)>();
        var meta = new FakeWhatsAppService();
        meta.OnSend = async _ =>
        {
            var rows = await factory.NotificationsAsync();
            observed.Add((rows.Count(x => x.Status is WhatsAppNotificationStatus.Processing or WhatsAppNotificationStatus.Sending),
                rows.Count(x => x.Status == WhatsAppNotificationStatus.Pending && x.Attempts == 0)));
        };
        using var host = factory.WithWhatsApp(meta);

        var summary = await ModulesApiFactory.DispatchAsync(host);

        Assert.Equal(3, summary.Accepted);
        // While one notice is at Meta, the others are not locked yet: no lease is running for them.
        Assert.Equal([(1, 2), (1, 1), (1, 0)], observed);
    }

    [Fact]
    public async Task A_crash_before_the_send_started_is_simply_retried()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromHours(3));
        await AddCheckInAsync(seed);
        var id = Assert.Single(await factory.NotificationsAsync()).Id;
        await MutateAsync(id, n => n.Claim(factory.UtcNow, factory.UtcNow.AddMinutes(2)));   // then the process died
        var meta = new FakeWhatsAppService();
        using var host = factory.WithWhatsApp(meta);

        factory.AdvanceTime(TimeSpan.FromMinutes(3));
        await ModulesApiFactory.DispatchAsync(host);

        Assert.Single(meta.Sent);
        var notice = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(WhatsAppNotificationStatus.Accepted, notice.Status);
        Assert.Equal(2, notice.Attempts);
    }

    [Fact]
    public async Task A_crash_during_the_send_is_never_resent_and_ends_as_an_explicit_unknown_outcome()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromHours(3));
        await AddCheckInAsync(seed);
        var id = Assert.Single(await factory.NotificationsAsync()).Id;
        await MutateAsync(id, n =>
        {
            n.Claim(factory.UtcNow, factory.UtcNow.AddMinutes(2));
            n.BeginSend(factory.UtcNow, factory.UtcNow.AddSeconds(90));                         // then the process died
        });
        var meta = new FakeWhatsAppService();
        using var host = factory.WithWhatsApp(meta);

        factory.AdvanceTime(TimeSpan.FromMinutes(2));
        await ModulesApiFactory.DispatchAsync(host);
        var parked = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(WhatsAppNotificationStatus.Unconfirmed, parked.Status);
        Assert.Equal("DISPATCH_INTERRUPTED", parked.LastErrorCode);

        factory.AdvanceTime(TimeSpan.FromMinutes(16));
        await ModulesApiFactory.DispatchAsync(host);

        Assert.Empty(meta.Sent);
        var notice = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(WhatsAppNotificationStatus.Failed, notice.Status);
        Assert.Equal(WhatsAppFailureCodes.OutcomeUnknown, notice.LastErrorCode);
    }

    // ---- unknown outcome: never a blind second message --------------------------------------------------------

    public static TheoryData<string> UnknownOutcomes => new()
    {
        WhatsAppFailureCodes.Timeout, WhatsAppFailureCodes.OutcomeUnknown, WhatsAppFailureCodes.InvalidResponse
    };

    [Theory]
    [MemberData(nameof(UnknownOutcomes))]
    public async Task An_unknown_outcome_is_never_retried_and_ends_failed_when_no_evidence_arrives(string code)
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromHours(3));
        await AddCheckInAsync(seed);
        var meta = new FakeWhatsAppService().Then(WhatsAppSendResult.Failed(code));
        using var host = factory.WithWhatsApp(meta);

        var first = await ModulesApiFactory.DispatchAsync(host);
        Assert.Equal(1, first.Unconfirmed);
        var parked = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(WhatsAppNotificationStatus.Unconfirmed, parked.Status);
        Assert.Equal(code, parked.LastErrorCode);
        Assert.Equal(factory.UtcNow.AddMinutes(15), parked.NextAttemptAt);

        for (var minute = 0; minute < 14; minute += 2)
        {
            factory.AdvanceTime(TimeSpan.FromMinutes(2));
            await ModulesApiFactory.DispatchAsync(host);
        }
        Assert.Equal(WhatsAppNotificationStatus.Unconfirmed, await StatusAsync());

        factory.AdvanceTime(TimeSpan.FromMinutes(2));
        await ModulesApiFactory.DispatchAsync(host);

        Assert.Single(meta.Sent);
        var notice = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(WhatsAppNotificationStatus.Failed, notice.Status);
        Assert.Equal(WhatsAppFailureCodes.OutcomeUnknown, notice.LastErrorCode);
        Assert.Equal(1, notice.Attempts);
    }

    [Fact]
    public async Task A_client_failure_during_the_call_is_an_unknown_outcome_not_a_retry()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromHours(3));
        await AddCheckInAsync(seed);
        var meta = new FakeWhatsAppService().ThenThrow(new IOException("connection reset by peer"));
        using var host = factory.WithWhatsApp(meta);

        await ModulesApiFactory.DispatchAsync(host);
        factory.AdvanceTime(TimeSpan.FromMinutes(5));
        await ModulesApiFactory.DispatchAsync(host);

        Assert.Single(meta.Sent);
        var notice = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(WhatsAppNotificationStatus.Unconfirmed, notice.Status);
        Assert.Equal("DISPATCH_ERROR", notice.LastErrorCode);
    }

    [Fact]
    public async Task A_network_error_before_the_request_left_is_retried_like_any_refused_send()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromHours(3));
        await AddCheckInAsync(seed);
        var meta = new FakeWhatsAppService().Then(WhatsAppSendResult.Failed(WhatsAppFailureCodes.NetworkError));
        using var host = factory.WithWhatsApp(meta);

        await ModulesApiFactory.DispatchAsync(host);
        Assert.Equal(WhatsAppNotificationStatus.Pending, await StatusAsync());
        factory.AdvanceTime(TimeSpan.FromSeconds(30));
        await ModulesApiFactory.DispatchAsync(host);

        Assert.Equal(2, meta.Sent.Count);
        Assert.Equal(WhatsAppNotificationStatus.Accepted, await StatusAsync());
    }

    [Fact]
    public async Task Webhook_evidence_resolves_an_unknown_outcome_through_the_echoed_callback_data()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromHours(3));
        await AddCheckInAsync(seed);
        var meta = new FakeWhatsAppService().Then(WhatsAppSendResult.Failed(WhatsAppFailureCodes.Timeout));
        using var host = factory.WithWhatsApp(meta);
        await ModulesApiFactory.DispatchAsync(host);
        var callback = Assert.Single(meta.CallbackData);

        // Meta did accept it: the "sent" status arrives with a wamid we never saw and our callback data.
        Assert.True(await ApplyAsync("wamid.lost-answer", WhatsAppDeliveryStatus.Sent, callbackData: callback));
        Assert.False(await ApplyAsync("wamid.lost-answer", WhatsAppDeliveryStatus.Sent, callbackData: callback));  // replay
        Assert.True(await ApplyAsync("wamid.lost-answer", WhatsAppDeliveryStatus.Delivered, callbackData: callback));

        factory.AdvanceTime(TimeSpan.FromMinutes(30));
        await ModulesApiFactory.DispatchAsync(host);

        Assert.Single(meta.Sent);
        var notice = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(WhatsAppNotificationStatus.Delivered, notice.Status);
        Assert.Equal("wamid.lost-answer", notice.MessageId);
        Assert.Null(notice.LastErrorCode);
    }

    [Fact]
    public async Task Callback_data_never_rewrites_a_known_wamid_nor_resolves_a_notice_that_was_not_sent()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromHours(3));
        await AddCheckInAsync(seed);
        var pending = Assert.Single(await factory.NotificationsAsync());

        // The status is still recorded for its message, but a notice that was never sent is not resolved by it.
        await ApplyAsync("wamid.forged", WhatsAppDeliveryStatus.Sent, callbackData: pending.CallbackData);
        var untouched = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(WhatsAppNotificationStatus.Pending, untouched.Status);
        Assert.Null(untouched.MessageId);

        using var host = factory.WithWhatsApp(new FakeWhatsAppService().Then(WhatsAppSendResult.Succeeded("wamid.real")));
        await ModulesApiFactory.DispatchAsync(host);
        await ApplyAsync("wamid.other", WhatsAppDeliveryStatus.Read, callbackData: pending.CallbackData);

        var notice = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal("wamid.real", notice.MessageId);
        Assert.Equal(WhatsAppNotificationStatus.Accepted, notice.Status);
    }

    // ---- PROFESSIONAL_DELAYED ---------------------------------------------------------------------------------

    [Fact]
    public async Task A_late_professional_triggers_one_notice_per_step_and_the_scheduler_never_spams()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromMinutes(-11));      // started 11 min ago
        await AddAsync(_ => Visit.Arrive(seed.ProfessionalId, seed.RoomId, seed.ReservationId, "Maria Clara", "TOTEM",
            factory.UtcNow.AddMinutes(-15), seed.CustomerId));
        var meta = new FakeWhatsAppService();
        using var host = factory.WithWhatsApp(meta);

        var first = await ModulesApiFactory.DispatchAsync(host);
        Assert.Equal(1, first.NoticesQueued);
        var (phone, template) = Assert.Single(meta.Sent);
        Assert.Equal(CustomerPhone, phone);
        Assert.Equal("client_professional_delayed", template.Name);
        Assert.Equal(["Maria", "Dra. Helena Prado", "11"], template.BodyParameters);

        for (var cycle = 0; cycle < 5; cycle++)                              // many cycles inside the repeat window
        {
            factory.AdvanceTime(TimeSpan.FromMinutes(2));
            await ModulesApiFactory.DispatchAsync(host);
        }
        Assert.Single(meta.Sent);

        factory.AdvanceTime(TimeSpan.FromMinutes(5));                        // 26 min late → step 1 is due
        await ModulesApiFactory.DispatchAsync(host);
        Assert.Equal(2, meta.Sent.Count);

        factory.AdvanceTime(TimeSpan.FromMinutes(20));                       // 46 min late → max (2) reached
        await ModulesApiFactory.DispatchAsync(host);
        await ModulesApiFactory.DispatchAsync(host);
        Assert.Equal(2, meta.Sent.Count);
        Assert.Equal([$"DELAY:{seed.ReservationId}:0", $"DELAY:{seed.ReservationId}:1"],
            (await factory.NotificationsAsync()).Select(x => x.IdempotencyKey));
    }

    [Fact]
    public async Task No_delay_notice_before_the_threshold_without_a_waiting_client_or_once_service_started()
    {
        await factory.ResetAsync();
        var early = await SeedAsync(startIn: TimeSpan.FromMinutes(-9));      // below the 10-minute threshold
        await AddAsync(_ => Visit.Arrive(early.ProfessionalId, early.RoomId, early.ReservationId, "Ana", "TOTEM", factory.UtcNow, early.CustomerId));
        await SeedAsync(startIn: TimeSpan.FromMinutes(-30));                  // late, but the client never arrived
        var started = await SeedAsync(startIn: TimeSpan.FromMinutes(-30));    // late, client already in service
        var visit = await AddAsync(_ => Visit.Arrive(started.ProfessionalId, started.RoomId, started.ReservationId, "Bia", "TOTEM", factory.UtcNow.AddMinutes(-35), started.CustomerId));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Visits.SingleAsync(x => x.Id == visit.Id)).StartService("prof", factory.UtcNow);
            await db.SaveChangesAsync();
        }
        var meta = new FakeWhatsAppService();
        using var host = factory.WithWhatsApp(meta);

        var summary = await ModulesApiFactory.DispatchAsync(host);

        Assert.Equal(0, summary.NoticesQueued);
        Assert.Empty(meta.Sent);
        Assert.Empty(await factory.NotificationsAsync());
    }

    // ---- APPOINTMENT_REMINDER ---------------------------------------------------------------------------------

    private static readonly (string Key, string Value)[] Reminders =
        [("Whatsapp:Templates:AppointmentReminder", "client_appointment_reminder"),
         ("Whatsapp:Notifications:ReminderLeadHours", "24")];

    [Fact]
    public async Task An_appointment_inside_the_lead_window_is_reminded_once_however_often_the_scheduler_runs()
    {
        await factory.ResetAsync();
        var far = await SeedAsync(startIn: TimeSpan.FromHours(30));          // still beyond the 24h window
        var seed = await SeedAsync(startIn: TimeSpan.FromHours(20));
        var meta = new FakeWhatsAppService();
        using var host = factory.WithWhatsApp(meta, Reminders);

        var first = await ModulesApiFactory.DispatchAsync(host);

        Assert.Equal(1, first.NoticesQueued);
        var (phone, template) = Assert.Single(meta.Sent);
        Assert.Equal(CustomerPhone, phone);
        Assert.Equal("client_appointment_reminder", template.Name);
        Assert.Equal("Maria", template.BodyParameters[0]);
        Assert.Equal("Dra. Helena Prado", template.BodyParameters[1]);

        for (var cycle = 0; cycle < 4; cycle++) await ModulesApiFactory.DispatchAsync(host);
        Assert.Single(meta.Sent);

        factory.AdvanceTime(TimeSpan.FromHours(7));                          // the far one is now 23h away
        await ModulesApiFactory.DispatchAsync(host);
        Assert.Equal(2, meta.Sent.Count);
        Assert.Equal([WhatsAppNotification.ReminderKey(seed.ReservationId), WhatsAppNotification.ReminderKey(far.ReservationId)],
            (await factory.NotificationsAsync()).Select(x => x.IdempotencyKey));
    }

    [Fact]
    public async Task No_reminder_without_the_template_and_none_for_an_appointment_cancelled_after_it_was_queued()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromHours(3));
        var meta = new FakeWhatsAppService();

        using (var bare = factory.WithWhatsApp(meta))                        // no AppointmentReminder template
        {
            Assert.Equal(0, (await ModulesApiFactory.DispatchAsync(bare)).NoticesQueued);
            Assert.Empty(await factory.NotificationsAsync());
        }

        using var host = factory.WithWhatsApp(meta, Reminders);
        // Queued without dispatching, so the cancellation lands in the window every real reminder has: between the
        // scan that queued it and the send.
        await using (var scope = host.Services.CreateAsyncScope())
            Assert.Equal(1, await scope.ServiceProvider
                .GetRequiredService<GestaoPredio.Infrastructure.Notifications.WhatsAppNotificationDispatcher>()
                .QueueRemindersAsync(CancellationToken.None));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Reservations.SingleAsync(x => x.Id == seed.ReservationId)).Cancel("recepção", factory.UtcNow);
            await db.SaveChangesAsync();
        }

        await ModulesApiFactory.DispatchAsync(host);

        Assert.Empty(meta.Sent);
        var notice = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(WhatsAppNotificationStatus.Skipped, notice.Status);
        Assert.Equal("OBSOLETE", notice.LastErrorCode);
    }

    // ---- cancellation / reschedule through the real admin endpoints -------------------------------------------

    [Fact]
    public async Task Admin_cancellation_notifies_the_customer_once_without_a_reschedule_link()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromHours(3));
        await LoginAdminAsync();
        var token = await ConcurrencyTokenAsync(seed.ReservationId);

        var cancel = await factory.PostWithCsrfAsync($"/api/admin/reservations/{seed.ReservationId}/cancel", new { concurrencyToken = token });
        var again = await factory.PostWithCsrfAsync($"/api/admin/reservations/{seed.ReservationId}/cancel", new { concurrencyToken = token });

        Assert.True(cancel.IsSuccessStatusCode, await cancel.Content.ReadAsStringAsync());
        Assert.False(again.IsSuccessStatusCode);
        var notice = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(WhatsAppNotificationType.AppointmentCancelled, notice.Type);
        Assert.Equal($"CANCEL:{seed.ReservationId}", notice.IdempotencyKey);

        var meta = new FakeWhatsAppService();
        using var host = factory.WithWhatsApp(meta);
        await ModulesApiFactory.DispatchAsync(host);
        var (phone, template) = Assert.Single(meta.Sent);
        Assert.Equal(CustomerPhone, phone);
        Assert.Equal("client_appointment_cancelled", template.Name);
        Assert.Equal(["Maria", "Dra. Helena Prado", LocalDate(seed.StartAt), LocalTime(seed.StartAt)], template.BodyParameters);
        Assert.Null(template.UrlButtonParameter);
    }

    [Fact]
    public async Task Admin_reschedule_notifies_the_customer_with_the_new_date_and_time()
    {
        await factory.ResetAsync();
        await factory.SeedDefaultOperatingHoursAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromHours(2));
        await LoginAdminAsync();
        var token = await ConcurrencyTokenAsync(seed.ReservationId);
        var newStart = seed.StartAt.AddHours(3);

        var response = await factory.PostWithCsrfAsync($"/api/admin/reservations/{seed.ReservationId}/reschedule",
            new { concurrencyToken = token, startAt = newStart, endAt = newStart.AddHours(1) });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var notice = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(WhatsAppNotificationType.AppointmentRescheduled, notice.Type);
        Assert.NotEqual(seed.ReservationId, notice.ReservationId);           // keyed by the replacement

        var meta = new FakeWhatsAppService();
        using var host = factory.WithWhatsApp(meta);
        await ModulesApiFactory.DispatchAsync(host);
        var (_, template) = Assert.Single(meta.Sent);
        Assert.Equal("client_appointment_rescheduled", template.Name);
        Assert.Equal(["Maria", "Dra. Helena Prado", LocalDate(newStart), LocalTime(newStart)], template.BodyParameters);
    }

    // ---- reschedule link: regenerated at send time, never persisted raw ---------------------------------------

    [Fact]
    public async Task The_link_is_issued_at_send_time_with_a_fresh_ttl_and_only_its_hash_is_stored()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromHours(3));
        var originalHash = await CancelForIncidentAsync(seed);
        factory.AdvanceTime(TimeSpan.FromMinutes(20));                        // the dispatcher runs later
        var meta = new FakeWhatsAppService();
        using var host = factory.WithWhatsApp(meta);

        await ModulesApiFactory.DispatchAsync(host);

        var raw = Assert.Single(meta.Sent).Template.UrlButtonParameter!;
        var token = await RescheduleTokenAsync(seed.ReservationId);
        Assert.Equal(System.Security.Cryptography.SHA256.HashData(Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(raw)), token.TokenHash);
        Assert.NotEqual(originalHash, token.TokenHash);
        Assert.Equal(factory.UtcNow.AddHours(48), token.ExpiresAt);         // validity counts from the send
        Assert.Null(token.UsedAt);
        Assert.Equal(HttpStatusCode.OK, (await factory.Client.PostAsJsonAsync("/api/reschedule/resolve", new { token = raw })).StatusCode);
        Assert.False(await AnyNotificationColumnContainsAsync(raw));
    }

    [Fact]
    public async Task Issuing_the_link_at_send_time_is_audited_without_the_token()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromHours(3));
        await CancelForIncidentAsync(seed);
        var meta = new FakeWhatsAppService();
        using var host = factory.WithWhatsApp(meta);

        await ModulesApiFactory.DispatchAsync(host);

        var raw = Assert.Single(meta.Sent).Template.UrlButtonParameter!;
        var notice = Assert.Single(await factory.NotificationsAsync());
        var token = await RescheduleTokenAsync(seed.ReservationId);
        await using var scope = factory.Services.CreateAsyncScope();
        var audit = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().AuditEntries.AsNoTracking()
            .SingleAsync(x => x.Action == "RESCHEDULE_LINK_ISSUED" && x.CorrelationId == $"whatsapp-notification:{notice.Id}");
        Assert.Equal("SUCCEEDED", audit.Result);
        Assert.Equal("RESCHEDULE_TOKEN", audit.TargetEntityType);
        Assert.Equal(token.Id, audit.TargetEntityId);
        Assert.DoesNotContain(raw, System.Text.Json.JsonSerializer.Serialize(audit));
    }

    [Fact]
    public async Task A_retry_after_meta_refused_the_send_issues_a_new_link_and_only_the_latest_one_works()
    {
        // Meta answered with an error, so the first link never reached anyone: replacing it costs nothing.
        await factory.ResetAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromHours(3));
        await CancelForIncidentAsync(seed);
        var meta = new FakeWhatsAppService().Then(WhatsAppSendResult.Failed(WhatsAppFailureCodes.ProviderUnavailable, 131000));
        using var host = factory.WithWhatsApp(meta);

        await ModulesApiFactory.DispatchAsync(host);
        factory.AdvanceTime(TimeSpan.FromSeconds(30));
        await ModulesApiFactory.DispatchAsync(host);

        var links = meta.Sent.Select(x => x.Template.UrlButtonParameter!).ToArray();
        Assert.Equal(2, links.Length);
        Assert.NotEqual(links[0], links[1]);
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.Client.PostAsJsonAsync("/api/reschedule/resolve", new { token = links[0] })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.Client.PostAsJsonAsync("/api/reschedule/resolve", new { token = links[1] })).StatusCode);
    }

    [Fact]
    public async Task An_unknown_outcome_keeps_the_link_that_may_have_been_delivered_valid_and_never_issues_another()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromHours(3));
        await CancelForIncidentAsync(seed);
        var meta = new FakeWhatsAppService().Then(WhatsAppSendResult.Failed(WhatsAppFailureCodes.Timeout));
        using var host = factory.WithWhatsApp(meta);
        var sentAt = factory.UtcNow;

        await ModulesApiFactory.DispatchAsync(host);
        var link = Assert.Single(meta.Sent).Template.UrlButtonParameter!;
        for (var minute = 0; minute < 20; minute += 5)
        {
            factory.AdvanceTime(TimeSpan.FromMinutes(5));
            await ModulesApiFactory.DispatchAsync(host);
        }

        // The client may hold this message: no second message, no rotation, so its link is the one that works.
        Assert.Single(meta.Sent);
        var token = await RescheduleTokenAsync(seed.ReservationId);
        Assert.Equal(System.Security.Cryptography.SHA256.HashData(Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(link)), token.TokenHash);
        Assert.Equal(sentAt.AddHours(48), token.ExpiresAt);                  // validity still counts from that send
        Assert.Null(token.RevokedAt);
        var notice = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(WhatsAppNotificationStatus.Failed, notice.Status);        // reported, not silently resent
        Assert.Equal(WhatsAppFailureCodes.OutcomeUnknown, notice.LastErrorCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.Client.PostAsJsonAsync("/api/reschedule/resolve", new { token = link })).StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_used_or_revoked_link_is_never_revived_and_the_notice_is_skipped(bool used)
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromHours(3));
        var originalHash = await CancelForIncidentAsync(seed);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var token = await db.RescheduleTokens.SingleAsync(x => x.ReservationId == seed.ReservationId);
            if (used) token.MarkUsed(factory.UtcNow); else token.Revoke(factory.UtcNow);
            await db.SaveChangesAsync();
        }
        var meta = new FakeWhatsAppService();
        using var host = factory.WithWhatsApp(meta);

        await ModulesApiFactory.DispatchAsync(host);

        Assert.Empty(meta.Sent);
        var notice = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(WhatsAppNotificationStatus.Skipped, notice.Status);
        Assert.Equal("OBSOLETE", notice.LastErrorCode);
        var after = await RescheduleTokenAsync(seed.ReservationId);
        Assert.Equal(originalHash, after.TokenHash);
        Assert.Equal(used, after.UsedAt is not null);
        Assert.Equal(!used, after.RevokedAt is not null);
    }

    // ---- delivery status through the existing webhook store ---------------------------------------------------

    [Fact]
    public async Task Webhook_statuses_move_the_notice_forward_idempotently_and_never_backwards()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromHours(3));
        await AddCheckInAsync(seed);
        var meta = new FakeWhatsAppService().Then(WhatsAppSendResult.Succeeded("wamid.flow"));
        using var host = factory.WithWhatsApp(meta);
        await ModulesApiFactory.DispatchAsync(host);
        await StoreAsync(store => store.RecordAcceptedAsync("wamid.flow", CustomerPhone, "pnid", WhatsAppMessageType.Template, factory.UtcNow, default));

        Assert.True(await ApplyAsync("wamid.flow", WhatsAppDeliveryStatus.Sent));
        Assert.Equal(WhatsAppNotificationStatus.Sent, await StatusAsync());
        Assert.True(await ApplyAsync("wamid.flow", WhatsAppDeliveryStatus.Delivered));
        Assert.False(await ApplyAsync("wamid.flow", WhatsAppDeliveryStatus.Delivered));  // duplicate
        Assert.False(await ApplyAsync("wamid.flow", WhatsAppDeliveryStatus.Sent));       // out of order
        Assert.Equal(WhatsAppNotificationStatus.Delivered, await StatusAsync());
        Assert.True(await ApplyAsync("wamid.flow", WhatsAppDeliveryStatus.Read));
        Assert.Equal(WhatsAppNotificationStatus.Read, await StatusAsync());
    }

    [Fact]
    public async Task A_webhook_failure_marks_the_notice_failed_with_the_meta_code()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromHours(3));
        await AddCheckInAsync(seed);
        using var host = factory.WithWhatsApp(new FakeWhatsAppService().Then(WhatsAppSendResult.Succeeded("wamid.fail")));
        await ModulesApiFactory.DispatchAsync(host);
        await StoreAsync(store => store.RecordAcceptedAsync("wamid.fail", CustomerPhone, "pnid", WhatsAppMessageType.Template, factory.UtcNow, default));

        Assert.True(await ApplyAsync("wamid.fail", WhatsAppDeliveryStatus.Failed, 131026));
        Assert.False(await ApplyAsync("wamid.fail", WhatsAppDeliveryStatus.Read));

        var notice = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(WhatsAppNotificationStatus.Failed, notice.Status);
        Assert.Equal("WHATSAPP_DELIVERY_FAILED:131026", notice.LastErrorCode);
    }

    [Fact]
    public async Task A_status_that_arrived_before_the_wamid_was_stored_is_caught_up_on_accept()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromHours(3));
        await AddCheckInAsync(seed);
        await StoreAsync(store => store.RecordAcceptedAsync("wamid.early", CustomerPhone, "pnid", WhatsAppMessageType.Template, factory.UtcNow, default));
        await ApplyAsync("wamid.early", WhatsAppDeliveryStatus.Delivered);
        using var host = factory.WithWhatsApp(new FakeWhatsAppService().Then(WhatsAppSendResult.Succeeded("wamid.early")));

        await ModulesApiFactory.DispatchAsync(host);

        Assert.Equal(WhatsAppNotificationStatus.Delivered, await StatusAsync());
    }

    // ---- observability without personal data -----------------------------------------------------------------

    [Fact]
    public async Task Dispatch_logs_identify_the_notice_and_outcome_but_never_phones_names_or_reschedule_tokens()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromHours(3));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var reservation = await db.Reservations.SingleAsync(x => x.Id == seed.ReservationId);
            reservation.Cancel("incident", factory.UtcNow, ReservationCancellationReason.ProfessionalUnavailable);
            db.WhatsAppNotifications.Add(WhatsAppNotification.ReservationCancelled(reservation, factory.UtcNow)!);
            await db.SaveChangesAsync();
        }
        var meta = new FakeWhatsAppService();
        using var host = factory.WithWhatsApp(meta);
        var logs = factory.CaptureLogs();

        await ModulesApiFactory.DispatchAsync(host);

        var (_, template) = Assert.Single(meta.Sent);
        var text = logs.Text;
        Assert.Contains("ACCEPTED", text);
        Assert.Contains(seed.ReservationId.ToString(), text);
        Assert.Contains("wamid.fake.1", text);
        Assert.DoesNotContain("983334444", text);
        Assert.DoesNotContain("981112222", text);
        Assert.DoesNotContain("Maria", text);
        Assert.DoesNotContain("Helena", text);
        Assert.DoesNotContain(template.UrlButtonParameter!, text);
    }

    // ---- admin read-only view --------------------------------------------------------------------------------

    [Fact]
    public async Task Admins_can_list_notifications_without_any_phone_or_secret_and_others_cannot()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromHours(3));
        await AddReservationNoticeAsync(seed, WhatsAppNotification.AppointmentConfirmed);

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.Client.GetAsync("/api/admin/whatsapp/notifications")).StatusCode);
        var manager = await factory.CreateUserAsync($"wa-notif-mgr-{Guid.NewGuid():N}@lumis.test", Password, [SystemRoles.Gerente]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(manager.Email!, Password)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.Client.GetAsync("/api/admin/whatsapp/notifications")).StatusCode);

        await LoginAdminAsync();
        var response = await factory.Client.GetAsync("/api/admin/whatsapp/notifications?status=PENDING&type=APPOINTMENT_CONFIRMED");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"APPOINTMENT_CONFIRMED\"", body);
        Assert.Contains("\"PENDING\"", body);
        Assert.DoesNotContain("983334444", body);
        Assert.DoesNotContain("981112222", body);
        Assert.DoesNotContain("Maria", body);
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.Client.GetAsync("/api/admin/whatsapp/notifications?status=NOPE")).StatusCode);
    }

    // ---- helpers ----------------------------------------------------------------------------------------------

    private sealed record Seed(Guid ReservationId, Guid ProfessionalId, Guid RoomId, Guid CustomerId, DateTimeOffset StartAt);

    private async Task<Seed> SeedAsync(TimeSpan startIn)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = factory.UtcNow;
        var professional = await db.Professionals.SingleOrDefaultAsync(x => x.WhatsApp == ProfessionalPhone)
            ?? Professional.Create("Dra. Helena Prado", "Psicologia", ProfessionalPhone, now);
        var customer = await db.Customers.SingleOrDefaultAsync(x => x.NormalizedPhone == CustomerPhone)
            ?? Customer.Create("Maria Clara Souza", CustomerPhone, now);
        var room = Room.Create($"Sala Notificação {Guid.NewGuid():N}"[..30], null, 10, 50, now);
        // Both recipients opted in explicitly: nothing is sent to anyone who did not (WhatsAppOptInApiTests).
        professional.GrantWhatsAppOptIn(WhatsAppOptInSource.ProfessionalPortal, now);
        customer.GrantWhatsAppOptIn(WhatsAppOptInSource.CustomerRegistration, now);
        if (db.Entry(professional).State == EntityState.Detached) db.Professionals.Add(professional);
        if (db.Entry(customer).State == EntityState.Detached) db.Customers.Add(customer);
        db.Rooms.Add(room);
        var start = now + startIn;
        var reservation = Reservation.CreateApproved(room.Id, professional.Id, start, start.AddHours(1), "seed", now.AddDays(-1), customer.Id);
        db.Reservations.Add(reservation);
        await db.SaveChangesAsync();
        return new Seed(reservation.Id, professional.Id, room.Id, customer.Id, reservation.StartAt);
    }

    private Task<Visit> AddCheckInAsync(Seed seed) =>
        AddAsync(_ => Visit.Arrive(seed.ProfessionalId, seed.RoomId, seed.ReservationId, "Maria Clara Souza", "TOTEM", factory.UtcNow, seed.CustomerId),
            visit => WhatsAppNotification.ClientCheckedIn(visit, factory.UtcNow));

    private async Task<Visit> AddAsync(Func<ApplicationDbContext, Visit> create, Func<Visit, WhatsAppNotification>? notice = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var visit = create(db);
        db.Visits.Add(visit);
        if (notice is not null) db.WhatsAppNotifications.Add(notice(visit));
        await db.SaveChangesAsync();
        return visit;
    }

    private async Task AddReservationNoticeAsync(Seed seed, Func<Reservation, DateTimeOffset, WhatsAppNotification?> create)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reservation = await db.Reservations.AsNoTracking().SingleAsync(x => x.Id == seed.ReservationId);
        db.WhatsAppNotifications.Add(create(reservation, factory.UtcNow)!);
        await db.SaveChangesAsync();
    }

    /// <summary>What the incident endpoint commits: the cancellation, a token row (hash only) and the queued notice.</summary>
    private async Task<byte[]> CancelForIncidentAsync(Seed seed)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reservation = await db.Reservations.SingleAsync(x => x.Id == seed.ReservationId);
        reservation.Cancel("PROFESSIONAL_INCIDENT", factory.UtcNow, ReservationCancellationReason.ProfessionalUnavailable);
        var hash = System.Security.Cryptography.SHA256.HashData(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        db.RescheduleTokens.Add(RescheduleToken.Create(reservation.Id, hash, factory.UtcNow, factory.UtcNow.AddHours(48)));
        db.WhatsAppNotifications.Add(WhatsAppNotification.ReservationCancelled(reservation, factory.UtcNow)!);
        await db.SaveChangesAsync();
        return hash;
    }

    private async Task<RescheduleToken> RescheduleTokenAsync(Guid reservationId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().RescheduleTokens.AsNoTracking()
            .SingleAsync(x => x.ReservationId == reservationId);
    }

    /// <summary>Scans every text column of the queue table for the value, so a raw token can never hide in it.</summary>
    private async Task<bool> AnyNotificationColumnContainsAsync(string value)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database
            .SqlQuery<string>($"SELECT CAST(n AS text) AS \"Value\" FROM \"WhatsAppNotifications\" AS n").ToListAsync();
        return rows.Any(row => row.Contains(value, StringComparison.Ordinal));
    }

    private async Task<Reservation> ReservationAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Reservations.AsNoTracking().SingleAsync(x => x.Id == id);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.ExecuteSqlRawAsync(sql);
    }

    private async Task StoreAsync(Func<IWhatsAppMessageStore, Task> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<IWhatsAppMessageStore>());
    }

    private async Task<bool> ApplyAsync(string messageId, WhatsAppDeliveryStatus status, int? errorCode = null,
        string? callbackData = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IWhatsAppMessageStore>().ApplyStatusAsync(
            new WhatsAppStatusUpdate(messageId, status, "5569983334444", factory.UtcNow, "pnid", "waba",
                errorCode, errorCode is null ? null : "Delivery failed", null, callbackData), default);
    }

    /// <summary>Applies domain transitions to a stored notification, as a (possibly crashed) dispatcher would have.</summary>
    private async Task MutateAsync(Guid id, Action<WhatsAppNotification> change)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        change(await db.WhatsAppNotifications.SingleAsync(x => x.Id == id));
        await db.SaveChangesAsync();
    }

    private async Task<WhatsAppNotificationStatus> StatusAsync() =>
        Assert.Single(await factory.NotificationsAsync()).Status;

    private async Task LoginAdminAsync()
    {
        var admin = await factory.CreateUserAsync($"wa-notif-admin-{Guid.NewGuid():N}@lumis.test", Password, [SystemRoles.Administrador]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(admin.Email!, Password)).StatusCode);
    }

    private async Task<string> ConcurrencyTokenAsync(Guid reservationId) =>
        (await factory.Client.GetFromJsonAsync<ReservationTokenPayload>($"/api/admin/reservations/{reservationId}"))!.ConcurrencyToken;

    private sealed record ReservationTokenPayload(string ConcurrencyToken);

    private static readonly TimeZoneInfo PortoVelho = TimeZoneInfo.FindSystemTimeZoneById("America/Porto_Velho");
    private static string LocalTime(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, PortoVelho).ToString("HH:mm");
    private static string LocalDate(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, PortoVelho).ToString("dd/MM/yyyy");
}

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

    // ---- skipped and expired notices ----------------------------------------------------------------------

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
            WhatsAppSendResult.Failed(WhatsAppFailureCodes.Timeout));
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
        await ModulesApiFactory.DispatchAsync(host);                 // 2nd attempt: timeout → wait 2 min
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
        Assert.Equal(1, first.DelayNoticesQueued);
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

        Assert.Equal(0, summary.DelayNoticesQueued);
        Assert.Empty(meta.Sent);
        Assert.Empty(await factory.NotificationsAsync());
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
    public async Task A_retry_issues_a_new_link_and_only_the_latest_one_works()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(startIn: TimeSpan.FromHours(3));
        await CancelForIncidentAsync(seed);
        var meta = new FakeWhatsAppService().Then(WhatsAppSendResult.Failed(WhatsAppFailureCodes.Timeout));
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


    private async Task StoreAsync(Func<IWhatsAppMessageStore, Task> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<IWhatsAppMessageStore>());
    }

    private async Task<bool> ApplyAsync(string messageId, WhatsAppDeliveryStatus status, int? errorCode = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IWhatsAppMessageStore>().ApplyStatusAsync(
            new WhatsAppStatusUpdate(messageId, status, "5569983334444", factory.UtcNow, "pnid", "waba",
                errorCode, errorCode is null ? null : "Delivery failed", null), default);
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

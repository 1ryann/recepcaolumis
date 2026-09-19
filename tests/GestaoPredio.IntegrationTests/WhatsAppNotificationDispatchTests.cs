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

    private static readonly TimeZoneInfo PortoVelho = TimeZoneInfo.FindSystemTimeZoneById("America/Porto_Velho");
    private static string LocalTime(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, PortoVelho).ToString("HH:mm");
}

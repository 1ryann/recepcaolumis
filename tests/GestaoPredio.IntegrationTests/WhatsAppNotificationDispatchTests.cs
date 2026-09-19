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

    // ---- infrastructure: queue, idempotency key, template configuration, expiry, admin view ---------------

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

    private async Task LoginAdminAsync()
    {
        var admin = await factory.CreateUserAsync($"wa-notif-admin-{Guid.NewGuid():N}@lumis.test", Password, [SystemRoles.Administrador]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(admin.Email!, Password)).StatusCode);
    }
}

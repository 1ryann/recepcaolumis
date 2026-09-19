using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Security;
using GestaoPredio.Domain.Visits;
using GestaoPredio.Domain.Notifications;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class ProfessionalIncidentApiTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";

    [Fact]
    public async Task Next_appointment_incident_cancels_only_the_earliest_and_keeps_presence()
    {
        await factory.ResetAsync();
        await factory.SeedDefaultOperatingHoursAsync();
        var seed = await SeedAsync();

        var response = await factory.PostWithCsrfAsync("/api/professional/incidents", new { type = "NEXT_APPOINTMENT" });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<IncidentPayload>();

        Assert.Single(payload!.AffectedReservationIds);
        Assert.Equal(seed.Soon.Id, payload.AffectedReservationIds[0]);
        Assert.Equal("PRESENT", payload.Presence);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(ReservationStatus.Cancelled,
            (await db.Reservations.AsNoTracking().SingleAsync(x => x.Id == seed.Soon.Id)).Status);
        Assert.Equal(ReservationCancellationReason.ProfessionalUnavailable,
            (await db.Reservations.AsNoTracking().SingleAsync(x => x.Id == seed.Soon.Id)).CancellationReason);
        Assert.Equal(ReservationStatus.Approved,
            (await db.Reservations.AsNoTracking().SingleAsync(x => x.Id == seed.Later.Id)).Status);
        Assert.True(await db.ProfessionalAvailabilityExceptions.AsNoTracking()
            .AnyAsync(x => x.ProfessionalId == seed.Professional.Id && x.Origin == ProfessionalAvailabilityExceptionOrigin.Incident));
        Assert.True(await db.RescheduleTokens.AsNoTracking().AnyAsync(x => x.ReservationId == seed.Soon.Id));
        // Only the cancelled appointment's customer is notified, and the notice committed with the cancellation.
        var notice = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(WhatsAppNotificationType.ProfessionalCancelled, notice.Type);
        Assert.Equal(WhatsAppNotificationRecipient.Customer, notice.Recipient);
        Assert.Equal($"CANCEL:{seed.Soon.Id}", notice.IdempotencyKey);
    }

    [Fact]
    public async Task Rest_of_day_incident_cancels_all_upcoming_and_ends_presence()
    {
        await factory.ResetAsync();
        await factory.SeedDefaultOperatingHoursAsync();
        var seed = await SeedAsync();

        var response = await factory.PostWithCsrfAsync("/api/professional/incidents", new { type = "REST_OF_DAY" });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<IncidentPayload>();

        Assert.Equal(2, payload!.AffectedReservationIds.Count);
        Assert.Equal("ABSENT", payload.Presence);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await db.Reservations.CountAsync(x =>
            (x.Id == seed.Soon.Id || x.Id == seed.Later.Id) && x.Status == ReservationStatus.Approved));
        Assert.False((await db.ProfessionalPresences.AsNoTracking()
            .SingleAsync(x => x.Id == seed.PresenceId)).EndedAt == null);
    }

    [Fact]
    public async Task Until_time_incident_reports_type_only_and_never_the_reason()
    {
        await factory.ResetAsync();
        await factory.SeedDefaultOperatingHoursAsync();
        var seed = await SeedAsync();

        var response = await factory.PostWithCsrfAsync("/api/professional/incidents",
            new { type = "UNTIL_TIME", untilTime = "23:59", reason = "motivo pessoal sensível" });
        response.EnsureSuccessStatusCode();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await db.AuditEntries.AnyAsync(x => x.Action == "PROFESSIONAL_INCIDENT_REPORTED_UNTIL_TIME"));
        Assert.False(await db.AuditEntries.AnyAsync(x =>
            x.ChangedFields != null && x.ChangedFields.Contains("motivo")));
        var exception = await db.ProfessionalAvailabilityExceptions.AsNoTracking()
            .SingleAsync(x => x.ProfessionalId == seed.Professional.Id && x.Origin == ProfessionalAvailabilityExceptionOrigin.Incident);
        Assert.Equal("motivo pessoal sensível", exception.Reason);
    }

    [Fact]
    public async Task Incident_does_not_touch_open_visits_but_raises_an_alert()
    {
        await factory.ResetAsync();
        await factory.SeedDefaultOperatingHoursAsync();
        var seed = await SeedAsync();
        Guid visitId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var visit = Visit.Arrive(seed.Professional.Id, seed.Room.Id, seed.Soon.Id, "Visitante", "seed",
                factory.UtcNow.AddMinutes(-5), seed.CustomerId);
            db.Visits.Add(visit);
            await db.SaveChangesAsync();
            visitId = visit.Id;
        }

        (await factory.PostWithCsrfAsync("/api/professional/incidents", new { type = "REST_OF_DAY" }))
            .EnsureSuccessStatusCode();

        await using var verify = factory.Services.CreateAsyncScope();
        var vdb = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(VisitStatus.Waiting, (await vdb.Visits.AsNoTracking().SingleAsync(x => x.Id == visitId)).Status);

        await LoginManagerAsync();
        var alerts = await factory.Client.GetFromJsonAsync<AlertPage>(
            "/api/admin/operational-alerts?type=OPEN_VISIT_AFFECTED_BY_INCIDENT");
        Assert.Contains(alerts!.Items, x => x.VisitId == visitId);
    }

    [Fact]
    public async Task Next_appointment_incident_with_no_upcoming_reservation_is_a_noop()
    {
        await factory.ResetAsync();
        await factory.SeedDefaultOperatingHoursAsync();
        var (email, professional) = await SeedLinkedProfessionalAsync();
        await MakePresentAsync(professional.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(email, Password)).StatusCode);

        var response = await factory.PostWithCsrfAsync("/api/professional/incidents", new { type = "NEXT_APPOINTMENT" });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<IncidentPayload>();

        Assert.Empty(payload!.AffectedReservationIds);
        Assert.Null(payload.ExceptionId);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.ProfessionalAvailabilityExceptions.AnyAsync(x => x.ProfessionalId == professional.Id));
    }

    [Fact]
    public async Task End_to_end_incident_link_can_be_resolved_and_confirmed()
    {
        await factory.ResetAsync();
        await factory.SeedDefaultOperatingHoursAsync();
        var seed = await SeedAsync();
        var meta = new FakeWhatsAppService();
        using var host = factory.WithWhatsApp(meta);

        (await factory.PostWithCsrfAsync("/api/professional/incidents", new { type = "NEXT_APPOINTMENT" }))
            .EnsureSuccessStatusCode();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tokenRow = await db.RescheduleTokens.AsNoTracking().SingleAsync(x => x.ReservationId == seed.Soon.Id);
            Assert.Equal(32, tokenRow.TokenHash.Length);
        }

        // The raw token exists only inside the WhatsApp message: the template's URL button carries it, and that
        // exact value opens the reschedule flow.
        Assert.Equal(1, (await ModulesApiFactory.DispatchAsync(host)).Accepted);
        var (_, template) = Assert.Single(meta.Sent);
        Assert.Equal("client_professional_cancelled_reschedule", template.Name);
        Assert.False(string.IsNullOrWhiteSpace(template.UrlButtonParameter));
        var resolve = await factory.Client.PostAsJsonAsync("/api/reschedule/resolve", new { token = template.UrlButtonParameter });
        Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);
        Assert.Equal(WhatsAppNotificationStatus.Accepted, Assert.Single(await factory.NotificationsAsync()).Status);
    }

    private async Task<Seed> SeedAsync()
    {
        var email = $"incident-{Guid.NewGuid():N}@lumis.test";
        var user = await factory.CreateUserAsync(email, Password, [SystemRoles.Profissional]);
        var now = factory.UtcNow;
        var professional = Professional.Create("Imprevisto API", "Fisioterapia",
            $"699{Random.Shared.Next(10000000, 99999999)}", now);
        professional.LinkUser(user.Id, now);
        var room = Room.Create($"Sala imprevisto {Guid.NewGuid():N}", null, 4, 90m, now);
        var customer = GestaoPredio.Domain.Customers.Customer.Create("Cliente Imprevisto", "69999990010", now);
        customer.GrantWhatsAppOptIn(WhatsAppOptInSource.CustomerRegistration, now);   // receives the reschedule link
        var soon = Reservation.CreateApproved(room.Id, professional.Id, now.AddMinutes(45), now.AddMinutes(105),
            "seed", now, customer.Id);
        var later = Reservation.CreateApproved(room.Id, professional.Id, now.AddMinutes(150), now.AddMinutes(210),
            "seed", now, customer.Id);
        var presence = ProfessionalPresence.StartByQr(professional.Id, now.AddMinutes(-30));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.AddRange(professional, room, customer, soon, later);
        db.ProfessionalPresences.Add(presence);
        await db.SaveChangesAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(email, Password)).StatusCode);
        return new Seed(professional, room, customer.Id, soon, later, presence.Id);
    }

    private async Task<(string Email, Professional Professional)> SeedLinkedProfessionalAsync()
    {
        var email = $"incident-noop-{Guid.NewGuid():N}@lumis.test";
        var user = await factory.CreateUserAsync(email, Password, [SystemRoles.Profissional]);
        var professional = Professional.Create("Imprevisto Vazio", "Psicologia",
            $"699{Random.Shared.Next(10000000, 99999999)}", factory.UtcNow);
        professional.LinkUser(user.Id, factory.UtcNow);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Professionals.Add(professional);
        await db.SaveChangesAsync();
        return (email, professional);
    }

    private async Task MakePresentAsync(Guid professionalId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.ProfessionalPresences.Add(ProfessionalPresence.StartByQr(professionalId, factory.UtcNow.AddMinutes(-10)));
        await db.SaveChangesAsync();
    }

    private async Task LoginManagerAsync()
    {
        var email = $"incident-mgr-{Guid.NewGuid():N}@lumis.test";
        await factory.CreateUserAsync(email, Password, [SystemRoles.Gerente]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(email, Password)).StatusCode);
    }

    private sealed record Seed(Professional Professional, Room Room, Guid CustomerId,
        Reservation Soon, Reservation Later, Guid PresenceId);
    private sealed record IncidentPayload(Guid? ExceptionId, IReadOnlyList<Guid> AffectedReservationIds, string Presence);
    private sealed record AlertPage(IReadOnlyList<AlertItem> Items);
    private sealed record AlertItem(Guid? VisitId);
}

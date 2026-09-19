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
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class ReceptionApiTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";

    [Fact]
    public async Task Reception_is_available_only_to_operations_roles()
    {
        await factory.ResetAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.Client.GetAsync("/api/reception/overview")).StatusCode);
        var professional = await factory.CreateUserAsync($"reception-prof-{Guid.NewGuid():N}@lumis.test", Password, [SystemRoles.Profissional]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(professional.Email!, Password)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.Client.GetAsync("/api/reception/overview")).StatusCode);
        var customer = await factory.CreateUserAsync($"reception-customer-{Guid.NewGuid():N}@lumis.test", Password, [SystemRoles.Customer]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(customer.Email!, Password)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.Client.GetAsync("/api/reception/overview")).StatusCode);
        var manager = await factory.CreateUserAsync($"reception-manager-{Guid.NewGuid():N}@lumis.test", Password, [SystemRoles.Gerente]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(manager.Email!, Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.Client.GetAsync("/api/reception/overview")).StatusCode);
    }

    [Fact]
    public async Task Overview_agenda_professionals_and_rooms_reflect_current_operations()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(withVisit: true);
        await LoginAsync(seed.Manager);
        var overview = (await (await factory.Client.GetAsync("/api/reception/overview")).Content.ReadFromJsonAsync<OverviewPayload>())!;
        Assert.Equal(1, overview.VisitorsWaiting);
        Assert.Equal(1, overview.ReservationsToday);
        Assert.NotEmpty(overview.UpcomingReservations);
        Assert.NotEmpty(overview.WaitingVisits);
        var professionals = (await (await factory.Client.GetAsync("/api/reception/professionals?status=WAITING_VISITOR")).Content.ReadFromJsonAsync<ProfessionalPayload[]>())!;
        Assert.Contains(professionals, x => x.ProfessionalId == seed.ProfessionalId && x.WaitingVisitorsCount == 1);
        var rooms = (await (await factory.Client.GetAsync("/api/reception/rooms")).Content.ReadFromJsonAsync<RoomPayload[]>())!;
        Assert.Contains(rooms, x => x.Id == seed.RoomId && x.OperationalStatus == "OCCUPIED");
        var agenda = (await (await factory.Client.GetAsync("/api/reception/agenda")).Content.ReadFromJsonAsync<AgendaPayload[]>())!;
        Assert.Contains(agenda, x => x.ReservationId == seed.ReservationId && x.VisitStatus == "WAITING");
    }

    [Fact]
    public async Task Manual_checkin_is_idempotent_and_preserves_customer_snapshot()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(withVisit: false, withCustomer: true);
        await LoginAsync(seed.Manager);
        var reservation = (await (await factory.Client.GetAsync($"/api/admin/reservations/{seed.ReservationId}")).Content.ReadFromJsonAsync<ReservationPayload>())!;
        var first = await factory.PostWithCsrfAsync($"/api/reception/reservations/{seed.ReservationId}/check-in",
            new { concurrencyToken = reservation.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstVisit = (await first.Content.ReadFromJsonAsync<ReceptionVisitPayload>())!;
        Assert.Equal("WAITING", firstVisit.Status);
        Assert.Equal(seed.CustomerId, firstVisit.CustomerId);
        Assert.Equal(seed.CustomerName, firstVisit.VisitorName);
        var second = await factory.PostWithCsrfAsync($"/api/reception/reservations/{seed.ReservationId}/check-in",
            new { concurrencyToken = reservation.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(firstVisit.Id, (await second.Content.ReadFromJsonAsync<ReceptionVisitPayload>())!.Id);
        // The second check-in returns the same visit: still exactly one arrival notice, keyed by that visit.
        var notice = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(WhatsAppNotificationType.ClientCheckedIn, notice.Type);
        Assert.Equal(seed.ProfessionalId, notice.ProfessionalId);
        Assert.Equal($"CHECKIN:{firstVisit.Id}", notice.IdempotencyKey);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Visits.CountAsync(x => x.ReservationId == seed.ReservationId));
    }

    [Fact]
    public async Task Reception_can_assist_booking_and_reuse_customer_by_canonical_phone()
    {
        await factory.ResetAsync();
        // Pin the clock to mid-morning so the [start, start+1h] booking window never straddles
        // civil midnight in America/Porto_Velho (which would make the professional "unavailable").
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Porto_Velho");
        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(factory.UtcNow, zone).DateTime);
        factory.FreezeTime(new DateTimeOffset(
            TimeZoneInfo.ConvertTimeToUtc(localToday.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Unspecified), zone),
            TimeSpan.Zero));
        var seed = await SeedAsync(withVisit: false, withCustomer: true);
        await LoginAsync(seed.Manager);
        var start = factory.UtcNow.AddHours(3);
        var response = await factory.PostWithCsrfAsync("/api/reception/reservations", new
        {
            name = "Nome que não substitui o cadastro", phone = "(69) 99999-9999", professionalId = seed.ProfessionalId,
            startAt = start, endAt = start.AddHours(1)
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<AssistedReservationPayload>())!;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reservation = await db.Reservations.SingleAsync(x => x.Id == created.ReservationId);
        Assert.Equal(seed.CustomerId, reservation.CustomerId);
        Assert.Equal(1, await db.Customers.CountAsync(x => x.NormalizedPhone == seed.CustomerPhone));
        var confirmation = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(WhatsAppNotificationType.AppointmentConfirmed, confirmation.Type);
        Assert.Equal($"CONFIRM:{created.ReservationId}", confirmation.IdempotencyKey);
        Assert.Equal(seed.CustomerId, confirmation.CustomerId);
    }

    [Fact]
    public async Task Meta_being_unavailable_never_blocks_or_rolls_back_the_manual_checkin()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(withVisit: false, withCustomer: true);
        var unavailable = WhatsAppSendResult.Failed(WhatsAppFailureCodes.ProviderUnavailable, 131000);
        var meta = new FakeWhatsAppService().Then(unavailable, unavailable);
        using var host = factory.WithWhatsApp(meta);
        await LoginAsync(seed.Manager);
        var reservation = (await (await factory.Client.GetAsync($"/api/admin/reservations/{seed.ReservationId}"))
            .Content.ReadFromJsonAsync<ReservationPayload>())!;

        var response = await factory.PostWithCsrfAsync($"/api/reception/reservations/{seed.ReservationId}/check-in",
            new { concurrencyToken = reservation.ConcurrencyToken });

        // The check-in committed without calling Meta at all…
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Empty(meta.Sent);
        Assert.Equal(WhatsAppNotificationStatus.Pending, Assert.Single(await factory.NotificationsAsync()).Status);

        // …and a failing send later only reschedules the notification; the visit is untouched. (The seeded
        // appointment is already late with the client waiting, so the same cycle may also queue a delay notice.)
        await ModulesApiFactory.DispatchAsync(host);
        var notice = Assert.Single(await factory.NotificationsAsync(), x => x.Type == WhatsAppNotificationType.ClientCheckedIn);
        Assert.Equal(WhatsAppNotificationStatus.Pending, notice.Status);
        Assert.Equal(WhatsAppFailureCodes.ProviderUnavailable, notice.LastErrorCode);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Visits
            .CountAsync(x => x.ReservationId == seed.ReservationId && x.Status == VisitStatus.Waiting));
    }

    [Fact]
    public async Task Reception_reuses_visit_transition_rules_and_concurrency()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(withVisit: true);
        await LoginAsync(seed.Manager);
        var visit = (await (await factory.Client.GetAsync($"/api/reception/visits/{seed.VisitId}")).Content.ReadFromJsonAsync<ReceptionVisitPayload>())!;
        var started = await factory.PostWithCsrfAsync($"/api/reception/visits/{seed.VisitId}/start", new { concurrencyToken = visit.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        var current = (await started.Content.ReadFromJsonAsync<ReceptionVisitPayload>())!;
        var stale = await factory.PostWithCsrfAsync($"/api/reception/visits/{seed.VisitId}/end", new { concurrencyToken = visit.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("RESOURCE_MODIFIED", (await stale.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
        var ended = await factory.PostWithCsrfAsync($"/api/reception/visits/{seed.VisitId}/end", new { concurrencyToken = current.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.OK, ended.StatusCode);
        Assert.Equal("ENDED", (await ended.Content.ReadFromJsonAsync<ReceptionVisitPayload>())!.Status);
    }

    [Fact]
    public async Task Manager_can_mark_a_professional_present_then_absent()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(withVisit: false);
        await LoginAsync(seed.Manager);

        var present = await factory.PostWithCsrfAsync("/api/reception/presence",
            new { professionalId = seed.ProfessionalId, state = "PRESENT" });
        Assert.Equal(HttpStatusCode.NoContent, present.StatusCode);
        Assert.Equal("PRESENT", await GetPresenceAsync(seed.ProfessionalId));

        var absent = await factory.PostWithCsrfAsync("/api/reception/presence",
            new { professionalId = seed.ProfessionalId, state = "ABSENT" });
        Assert.Equal(HttpStatusCode.NoContent, absent.StatusCode);
        Assert.Equal("ABSENT", await GetPresenceAsync(seed.ProfessionalId));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await db.AuditEntries.AnyAsync(x => x.Action == "PROFESSIONAL_PRESENCE_STARTED_BY_OPERATIONS"));
        Assert.True(await db.AuditEntries.AnyAsync(x => x.Action == "PROFESSIONAL_PRESENCE_ENDED_BY_OPERATIONS"));
    }

    [Fact]
    public async Task Reception_presence_is_operations_only()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(withVisit: false);
        var professional = await factory.CreateUserAsync($"reception-presence-prof-{Guid.NewGuid():N}@lumis.test",
            Password, [SystemRoles.Profissional]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(professional.Email!, Password)).StatusCode);

        var response = await factory.PostWithCsrfAsync("/api/reception/presence",
            new { professionalId = seed.ProfessionalId, state = "PRESENT" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Reception_presence_projection_exposes_absent_until_for_an_incident()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(withVisit: false);
        var now = factory.UtcNow;
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,
            TimeZoneInfo.FindSystemTimeZoneById("America/Porto_Velho")).DateTime);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ProfessionalAvailabilityExceptions.Add(ProfessionalAvailabilityException.Create(
                seed.ProfessionalId, localDate, false, new TimeOnly(0, 0), new TimeOnly(23, 59, 59),
                "imprevisto", now, ProfessionalAvailabilityExceptionOrigin.Incident));
            await db.SaveChangesAsync();
        }
        await LoginAsync(seed.Manager);

        var row = await GetPresenceRowAsync(seed.ProfessionalId);
        Assert.Equal("ABSENT", row.Presence);
        Assert.NotNull(row.AbsentUntil);
    }

    [Fact]
    public async Task Concurrent_absent_writes_resolve_to_one_success()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(withVisit: false);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ProfessionalPresences.Add(ProfessionalPresence.StartByQr(seed.ProfessionalId, factory.UtcNow.AddMinutes(-10)));
            await db.SaveChangesAsync();
        }
        await LoginAsync(seed.Manager);

        var first = factory.PostWithCsrfAsync("/api/reception/presence",
            new { professionalId = seed.ProfessionalId, state = "ABSENT" });
        var second = factory.PostWithCsrfAsync("/api/reception/presence",
            new { professionalId = seed.ProfessionalId, state = "ABSENT" });
        var responses = await Task.WhenAll(first, second);

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.NoContent));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    private async Task<string> GetPresenceAsync(Guid professionalId) =>
        (await GetPresenceRowAsync(professionalId)).Presence;

    private async Task<PresenceProfessionalPayload> GetPresenceRowAsync(Guid professionalId)
    {
        var rows = (await (await factory.Client.GetAsync("/api/reception/professionals"))
            .Content.ReadFromJsonAsync<PresenceProfessionalPayload[]>())!;
        return rows.Single(x => x.ProfessionalId == professionalId);
    }

    private async Task<Seed> SeedAsync(bool withVisit, bool withCustomer = false)
    {
        await factory.SeedDefaultOperatingHoursAsync();
        var manager = await factory.CreateUserAsync($"reception-manager-{Guid.NewGuid():N}@lumis.test", Password, [SystemRoles.Gerente]);
        var now = factory.UtcNow;
        var room = Room.Create($"Sala Recepção {Guid.NewGuid():N}", null, 10, 50, now);
        var professional = Professional.Create("Profissional Recepção", "Fisioterapia", $"659{Random.Shared.Next(10000000, 99999999)}", now);
        Customer? customer = withCustomer ? Customer.Create("Cliente Recepção", "+5569999999999", now) : null;
        // Both opted in, so the dispatch tests here exercise Meta's answers rather than the opt-in gate.
        professional.GrantWhatsAppOptIn(WhatsAppOptInSource.ProfessionalPortal, now);
        customer?.GrantWhatsAppOptIn(WhatsAppOptInSource.CustomerRegistration, now);
        var reservation = Reservation.CreateApproved(room.Id, professional.Id, now.AddMinutes(-10), now.AddHours(1), manager.Id, now, customer?.Id);
        Visit? visit = withVisit ? Visit.Arrive(professional.Id, room.Id, reservation.Id, customer?.Name ?? "Visitante Recepção", manager.Id, now, customer?.Id) : null;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.AddRange(room, professional, reservation);
        if (customer is not null) db.Customers.Add(customer);
        if (visit is not null) { db.Visits.Add(visit); db.VisitTransitions.Add(VisitTransition.Record(visit.Id, null, VisitStatus.Waiting, manager.Id, now)); }
        await db.SaveChangesAsync();
        return new Seed(manager, room.Id, professional.Id, customer?.Id, customer?.Name, customer?.NormalizedPhone,
            reservation.Id, visit?.Id ?? Guid.Empty);
    }

    private async Task LoginAsync(GestaoPredio.Infrastructure.Identity.ApplicationUser user) =>
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(user.Email!, Password)).StatusCode);

    private sealed record Seed(GestaoPredio.Infrastructure.Identity.ApplicationUser Manager, Guid RoomId,
        Guid ProfessionalId, Guid? CustomerId, string? CustomerName, string? CustomerPhone, Guid ReservationId, Guid VisitId);
    private sealed record OverviewPayload(int VisitorsWaiting, int VisitsInService, int ReservationsToday,
        IReadOnlyList<AgendaPayload> UpcomingReservations, IReadOnlyList<ReceptionVisitPayload> WaitingVisits);
    private sealed record ProfessionalPayload(Guid ProfessionalId, int WaitingVisitorsCount);
    private sealed record PresenceProfessionalPayload(Guid ProfessionalId, string Presence, DateTimeOffset? AbsentUntil);
    private sealed record RoomPayload(Guid Id, string OperationalStatus);
    private sealed record AgendaPayload(Guid ReservationId, string? VisitStatus);
    private sealed record ReservationPayload(string ConcurrencyToken);
    private sealed record ReceptionVisitPayload(Guid Id, Guid? CustomerId, string VisitorName, string Status, string ConcurrencyToken);
    private sealed record AssistedReservationPayload(Guid ReservationId);
    private sealed record ErrorPayload(string Code, string Message);
}

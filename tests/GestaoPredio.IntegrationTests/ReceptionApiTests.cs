using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Application.Notifications;
using GestaoPredio.Domain.Customers;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Security;
using GestaoPredio.Domain.Visits;
using GestaoPredio.Infrastructure.Persistence;
using GestaoPredio.Infrastructure.Notifications;
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
        var recorder = factory.Services.GetRequiredService<DemoNotificationRecorder>();
        recorder.Clear();
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
        var attempt = Assert.Single(recorder.Attempts);
        Assert.Equal(seed.ProfessionalId, attempt.ProfessionalId);
        Assert.Equal(NotificationEventTypes.ProfessionalVisitWaiting, attempt.EventType);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Visits.CountAsync(x => x.ReservationId == seed.ReservationId));
    }

    [Fact]
    public async Task Reception_can_assist_booking_and_reuse_customer_by_canonical_phone()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(withVisit: false, withCustomer: true);
        await LoginAsync(seed.Manager);
        var start = DateTimeOffset.UtcNow.AddHours(3);
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
    }

    [Fact]
    public async Task Notification_failure_does_not_rollback_manual_checkin()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync(withVisit: false, withCustomer: true);
        var recorder = factory.Services.GetRequiredService<DemoNotificationRecorder>();
        recorder.Clear();
        recorder.ForceFailure = true;
        await LoginAsync(seed.Manager);
        var reservation = (await (await factory.Client.GetAsync($"/api/admin/reservations/{seed.ReservationId}"))
            .Content.ReadFromJsonAsync<ReservationPayload>())!;

        var response = await factory.PostWithCsrfAsync($"/api/reception/reservations/{seed.ReservationId}/check-in",
            new { concurrencyToken = reservation.ConcurrencyToken });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False(Assert.Single(recorder.Attempts).Success);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Visits
            .CountAsync(x => x.ReservationId == seed.ReservationId));
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

    private async Task<Seed> SeedAsync(bool withVisit, bool withCustomer = false)
    {
        var manager = await factory.CreateUserAsync($"reception-manager-{Guid.NewGuid():N}@lumis.test", Password, [SystemRoles.Gerente]);
        var now = DateTimeOffset.UtcNow;
        var room = Room.Create($"Sala Recepção {Guid.NewGuid():N}", null, 10, 50, now);
        var professional = Professional.Create("Profissional Recepção", "Fisioterapia", $"659{Random.Shared.Next(10000000, 99999999)}", now);
        Customer? customer = withCustomer ? Customer.Create("Cliente Recepção", "+5569999999999", now) : null;
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
    private sealed record RoomPayload(Guid Id, string OperationalStatus);
    private sealed record AgendaPayload(Guid ReservationId, string? VisitStatus);
    private sealed record ReservationPayload(string ConcurrencyToken);
    private sealed record ReceptionVisitPayload(Guid Id, Guid? CustomerId, string VisitorName, string Status, string ConcurrencyToken);
    private sealed record AssistedReservationPayload(Guid ReservationId);
    private sealed record ErrorPayload(string Code, string Message);
}

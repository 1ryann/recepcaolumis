using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Leases;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Security;
using GestaoPredio.Domain.Tenants;
using GestaoPredio.Domain.Visits;
using GestaoPredio.Infrastructure.Identity;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class OperationalAlertApiTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";

    [Theory]
    [InlineData(false, "RESERVATION_ENDED_VISIT_WAITING", "WARNING")]
    [InlineData(true, "RESERVATION_ENDED_VISIT_IN_SERVICE", "CRITICAL")]
    public async Task Ended_reservation_with_open_visit_produces_the_expected_alert(
        bool startService, string expectedType, string expectedSeverity)
    {
        await factory.ResetAsync();
        var seed = await SeedAsync();
        var (reservation, visit) = await AddEndedReservationVisitAsync(seed, startService);
        await LoginAsync(seed.Manager);

        var response = await factory.Client.GetFromJsonAsync<AlertPage>(
            $"/api/admin/operational-alerts?type={expectedType}");

        var alert = Assert.Single(response!.Items);
        Assert.Equal(expectedType, alert.Type);
        Assert.Equal(expectedSeverity, alert.Severity);
        Assert.Equal(reservation.Id, alert.ReservationId);
        Assert.Equal(visit.Id, alert.VisitId);
        Assert.Equal(seed.Room.Id, alert.RoomId);
        Assert.Equal(seed.Professional.Id, alert.ProfessionalId);
    }

    [Fact]
    public async Task Upcoming_and_started_reservations_produce_soon_and_conflict_alerts()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync();
        var now = DateTimeOffset.UtcNow;
        var visit = Visit.Arrive(seed.Professional.Id, seed.Room.Id, null, "Visitante atual",
            seed.Manager.Id, now.AddHours(-2));
        visit.StartService(seed.Manager.Id, now.AddHours(-2).AddMinutes(5));
        var upcoming = Reservation.CreateApproved(seed.Room.Id, seed.OtherProfessional.Id,
            now.AddMinutes(30), now.AddHours(2), seed.Manager.Id, now.AddHours(-2));
        var started = Reservation.CreateApproved(seed.Room.Id, seed.OtherProfessional.Id,
            now.AddMinutes(-15), now.AddMinutes(45), seed.Manager.Id, now.AddHours(-2));
        await AddAsync(visit, upcoming, started);
        await LoginAsync(seed.Manager);

        var page = await factory.Client.GetFromJsonAsync<AlertPage>("/api/admin/operational-alerts");

        Assert.Contains(page!.Items, item => item.Type == "NEXT_RESERVATION_SOON" &&
            item.ReservationId == upcoming.Id && item.Severity == "WARNING");
        Assert.Contains(page.Items, item => item.Type == "NEXT_RESERVATION_CONFLICT" &&
            item.ReservationId == started.Id && item.Severity == "CRITICAL");
    }

    [Fact]
    public async Task Current_lease_occurrence_can_trigger_a_next_reservation_alert()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync();
        var now = DateTimeOffset.UtcNow;
        var tenant = Tenant.Create("Clínica ocorrência", TenantKind.LegalEntity, now.AddDays(-2));
        var lease = Lease.Create(tenant.Id, seed.Professional.Id, seed.Room.Id, LeaseMode.Hourly,
            100, now.AddDays(-1), null, now.AddHours(-1), now.AddHours(2), null, now.AddDays(-1));
        var occurrence = LeaseOccurrence.Create(lease.Id, now.AddMinutes(-30), now.AddMinutes(30),
            now.AddDays(-1));
        var upcoming = Reservation.CreateApproved(seed.Room.Id, seed.OtherProfessional.Id,
            now.AddMinutes(45), now.AddHours(2), seed.Manager.Id, now.AddHours(-2));
        await AddAsync(tenant, lease, occurrence, upcoming);
        await LoginAsync(seed.Manager);

        var page = await factory.Client.GetFromJsonAsync<AlertPage>(
            "/api/admin/operational-alerts?type=NEXT_RESERVATION_SOON");

        var alert = Assert.Single(page!.Items);
        Assert.Equal(upcoming.Id, alert.ReservationId);
        Assert.Equal(lease.Id, alert.LeaseId);
        Assert.Null(alert.VisitId);
    }

    [Fact]
    public async Task Ending_lease_with_an_open_visit_produces_an_alert()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync();
        var now = DateTimeOffset.UtcNow;
        var tenant = Tenant.Create("Clínica Alfa", TenantKind.LegalEntity, now.AddDays(-10));
        var lease = Lease.Create(tenant.Id, seed.Professional.Id, seed.Room.Id, LeaseMode.Hourly,
            100, now.AddDays(-2), null, now.AddHours(-2), now.AddMinutes(-5), null, now.AddDays(-2));
        var visit = Visit.Arrive(seed.Professional.Id, seed.Room.Id, null, "Visitante",
            seed.Manager.Id, now.AddHours(-1));
        await AddAsync(tenant, lease, visit);
        await LoginAsync(seed.Manager);

        var page = await factory.Client.GetFromJsonAsync<AlertPage>(
            "/api/admin/operational-alerts?type=LEASE_ENDING_WITH_ACTIVE_VISIT");

        var alert = Assert.Single(page!.Items);
        Assert.Equal("CRITICAL", alert.Severity);
        Assert.Equal(lease.Id, alert.LeaseId);
        Assert.Equal(visit.Id, alert.VisitId);
    }

    [Fact]
    public async Task Alert_disappears_after_the_operational_condition_is_resolved()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync();
        var (_, visit) = await AddEndedReservationVisitAsync(seed, startService: true);
        await LoginAsync(seed.Manager);
        Assert.Single((await factory.Client.GetFromJsonAsync<AlertPage>(
            "/api/admin/operational-alerts?type=RESERVATION_ENDED_VISIT_IN_SERVICE"))!.Items);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stored = await db.Visits.SingleAsync(item => item.Id == visit.Id);
            stored.End(seed.Manager.Id, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        Assert.Empty((await factory.Client.GetFromJsonAsync<AlertPage>(
            "/api/admin/operational-alerts?type=RESERVATION_ENDED_VISIT_IN_SERVICE"))!.Items);
    }

    [Fact]
    public async Task Feed_filters_and_summary_use_the_same_derived_alerts_without_mutating_sources()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync();
        var (reservation, visit) = await AddEndedReservationVisitAsync(seed, startService: true);
        var originalVisitUpdatedAt = visit.UpdatedAt;
        var originalReservationUpdatedAt = reservation.UpdatedAt;
        await LoginAsync(seed.Manager);

        var query = $"severity=CRITICAL&type=RESERVATION_ENDED_VISIT_IN_SERVICE&roomId={seed.Room.Id}&professionalId={seed.Professional.Id}";
        var page = await factory.Client.GetFromJsonAsync<AlertPage>($"/api/admin/operational-alerts?{query}&page=1&pageSize=10");
        var summary = await factory.Client.GetFromJsonAsync<AlertSummary>($"/api/admin/operational-alerts/summary?{query}");

        Assert.Single(page!.Items);
        Assert.Equal(1, summary!.Total);
        Assert.Equal(0, summary.Info);
        Assert.Equal(0, summary.Warning);
        Assert.Equal(1, summary.Critical);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(originalVisitUpdatedAt, (await db.Visits.AsNoTracking().SingleAsync(x => x.Id == visit.Id)).UpdatedAt);
        Assert.Equal(originalReservationUpdatedAt,
            (await db.Reservations.AsNoTracking().SingleAsync(x => x.Id == reservation.Id)).UpdatedAt);
    }

    [Fact]
    public async Task Global_operational_alerts_require_operations_policy()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.Client.GetAsync("/api/admin/operational-alerts")).StatusCode);
        await LoginAsync(seed.Owner);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await factory.Client.GetAsync("/api/admin/operational-alerts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await factory.Client.GetAsync("/api/admin/operational-alerts/summary")).StatusCode);
    }

    private async Task<Seed> SeedAsync()
    {
        var owner = await factory.CreateUserAsync($"alert-owner-{Guid.NewGuid():N}@lumis.test", Password,
            [SystemRoles.Profissional]);
        var manager = await factory.CreateUserAsync($"alert-manager-{Guid.NewGuid():N}@lumis.test", Password,
            [SystemRoles.Gerente]);
        var now = DateTimeOffset.UtcNow;
        var room = Room.Create($"Sala alerta {Guid.NewGuid():N}", null, 10, 50, now);
        var professional = Professional.Create("Profissional alertas", "Fisioterapia", "+5565999999999", now);
        professional.LinkUser(owner.Id, now);
        var otherProfessional = Professional.Create("Outro profissional", "Psicologia", "+5565988888888", now);
        await AddAsync(room, professional, otherProfessional);
        return new Seed(owner, manager, professional, otherProfessional, room);
    }

    private async Task<(Reservation Reservation, Visit Visit)> AddEndedReservationVisitAsync(
        Seed seed, bool startService)
    {
        var now = DateTimeOffset.UtcNow;
        var reservation = Reservation.CreateApproved(seed.Room.Id, seed.Professional.Id,
            now.AddHours(-2), now.AddMinutes(-5), seed.Manager.Id, now.AddHours(-3));
        var visit = Visit.Arrive(seed.Professional.Id, seed.Room.Id, reservation.Id, "Visitante",
            seed.Manager.Id, now.AddHours(-2));
        if (startService) visit.StartService(seed.Manager.Id, now.AddHours(-1));
        await AddAsync(reservation, visit);
        return (reservation, visit);
    }

    private async Task AddAsync(params object[] entities)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    private async Task LoginAsync(ApplicationUser user) =>
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(user.Email!, Password)).StatusCode);

    private sealed record Seed(ApplicationUser Owner, ApplicationUser Manager, Professional Professional,
        Professional OtherProfessional, Room Room);
    private sealed record AlertPage(IReadOnlyList<AlertPayload> Items, int Page, int PageSize, int TotalCount);
    private sealed record AlertPayload(string Id, string Type, string Severity, Guid? RoomId,
        Guid? ProfessionalId, Guid? ReservationId, Guid? VisitId, Guid? LeaseId);
    private sealed record AlertSummary(int Total, int Info, int Warning, int Critical);
}

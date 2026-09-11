using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Finance;
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
public sealed class DashboardApiTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";

    [Fact]
    public async Task Dashboard_aggregates_real_operational_and_financial_data()
    {
        await factory.ResetAsync();
        var admin = await factory.CreateUserAsync($"dashboard-admin-{Guid.NewGuid():N}@lumis.test", Password,
            [SystemRoles.Administrador]);
        var now = DateTimeOffset.UtcNow;
        var room = Room.Create($"Sala dashboard {Guid.NewGuid():N}", null, 50m, 200m, now);
        var professional = Professional.Create("Profissional dashboard", "Fisioterapia", "+5565999999999", now);
        var tenant = Tenant.Create("Cliente dashboard", TenantKind.Individual, now.AddDays(-10));
        var lease = Lease.Create(tenant.Id, professional.Id, room.Id, LeaseMode.Hourly, 50m,
            now.AddDays(-1), DateTimeOffset.UtcNow.Day, now.AddHours(-2), now.AddHours(2), null, now.AddDays(-1));
        var reservation = Reservation.CreateApproved(room.Id, professional.Id, now.AddMinutes(30),
            now.AddHours(1), admin.Id, now.AddMinutes(-5));
        var visit = Visit.Arrive(professional.Id, room.Id, reservation.Id, "Visitante dashboard", admin.Id,
            now.AddMinutes(-20));
        var operationalDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,
            TimeZoneInfo.FindSystemTimeZoneById("America/Porto_Velho")).DateTime);
        var charge = FinancialCharge.Create(lease.Id, professional.Id, tenant.Id, now.AddDays(-1), now,
            operationalDate, 50m, "{\"mode\":\"HOURLY\",\"minutes\":60}", now);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AddRange(room, professional, tenant, lease, reservation, visit, charge);
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(admin.Email!, Password)).StatusCode);
        var response = await factory.Client.GetAsync("/api/admin/dashboard");
        response.EnsureSuccessStatusCode();
        var dashboard = await response.Content.ReadFromJsonAsync<DashboardPayload>();

        Assert.NotNull(dashboard);
        Assert.Equal(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById("America/Porto_Velho")).DateTime), dashboard!.OperationalDate);
        Assert.Equal(1, dashboard.Counts.ActiveProfessionals);
        Assert.Equal(1, dashboard.Counts.ActiveRooms);
        Assert.Equal(1, dashboard.Counts.OccupiedRooms);
        Assert.Equal(1, dashboard.Counts.ActiveLeases);
        Assert.Equal(1, dashboard.Counts.TodayReservations);
        Assert.Equal(1, dashboard.Counts.WaitingVisits);
        Assert.Single(dashboard.Agenda);
        Assert.Single(dashboard.CurrentVisits);
        Assert.Equal(50m, dashboard.Financial.PendingAmount);
        Assert.Equal(1, dashboard.Financial.PendingCount);
        Assert.Contains(dashboard.Rooms, item => item.RoomId == room.Id && item.Status == "OCCUPIED");
    }

    [Fact]
    public async Task Dashboard_reports_todays_check_in_count_exactly()
    {
        await factory.ResetAsync();
        // The dashboard counts check-ins against the operational-timezone civil day (America/Porto_
        // Velho, UTC-4, so civil midnight == 04:00 UTC). Seeding relative to the real wall clock made
        // this test flaky whenever it happened to run between 04:00-04:30 UTC (the earlier-seeded
        // visit would fall into the previous civil day). Freeze the clock to a fixed instant
        // comfortably mid-day in the operational timezone before seeding, so it is deterministic
        // regardless of when the suite actually runs.
        factory.FreezeTime(new DateTimeOffset(2026, 1, 15, 16, 0, 0, TimeSpan.Zero)); // 12:00 in America/Porto_Velho
        var admin = await factory.CreateUserAsync($"dashboard-checkins-{Guid.NewGuid():N}@lumis.test", Password,
            [SystemRoles.Administrador]);
        var now = factory.UtcNow;
        var room = Room.Create($"Sala checkins {Guid.NewGuid():N}", null, 50m, 200m, now);
        var professional = Professional.Create("Profissional checkins", "Fisioterapia", "+5565999999999", now);
        var todayVisitA = Visit.Arrive(professional.Id, room.Id, null, "Visitante hoje A", admin.Id, now.AddMinutes(-30));
        var todayVisitB = Visit.Arrive(professional.Id, room.Id, null, "Visitante hoje B", admin.Id, now.AddMinutes(-10));
        var yesterdayVisit = Visit.Arrive(professional.Id, room.Id, null, "Visitante ontem", admin.Id, now.AddDays(-1));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AddRange(room, professional, todayVisitA, todayVisitB, yesterdayVisit);
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(admin.Email!, Password)).StatusCode);
        var response = await factory.Client.GetAsync("/api/admin/dashboard");
        response.EnsureSuccessStatusCode();
        var dashboard = await response.Content.ReadFromJsonAsync<DashboardPayload>();

        Assert.NotNull(dashboard);
        Assert.Equal(2, dashboard!.Counts.TodayCheckIns);
    }

    [Fact]
    public async Task Dashboard_is_global_operations_only()
    {
        await factory.ResetAsync();
        var professionalUser = await factory.CreateUserAsync($"dashboard-prof-{Guid.NewGuid():N}@lumis.test", Password,
            [SystemRoles.Profissional]);

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.Client.GetAsync("/api/admin/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await factory.LoginAsync(professionalUser.Email!, Password)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.Client.GetAsync("/api/admin/dashboard")).StatusCode);
    }

    private sealed record DashboardPayload(DateOnly OperationalDate, CountsPayload Counts,
        FinancialPayload Financial, IReadOnlyList<AgendaPayload> Agenda,
        IReadOnlyList<CurrentVisitPayload> CurrentVisits, IReadOnlyList<RoomPayload> Rooms);
    private sealed record CountsPayload(int ActiveProfessionals, int ActiveRooms, int OccupiedRooms,
        int ReservedRooms, int ActiveLeases, int ScheduledLeases, int PendingReservations,
        int TodayReservations, int WaitingVisits, int InServiceVisits, int TodayCheckIns);
    private sealed record FinancialPayload(decimal PendingAmount, decimal OverdueAmount, decimal PaidAmount,
        int PendingCount, int OverdueCount, int PaidCount);
    private sealed record AgendaPayload(Guid ReservationId);
    private sealed record CurrentVisitPayload(Guid VisitId);
    private sealed record RoomPayload(Guid RoomId, string Status);
}

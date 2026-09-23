using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Notifications;
using GestaoPredio.Domain.Security;
using GestaoPredio.Domain.Visits;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using recepcaototem.Features.Common;

namespace GestaoPredio.IntegrationTests;

/// <summary>
/// The kiosk, the reception desk and the back office all confirm arrivals through one core, so these
/// tests assert the parts that used to differ: every route now records a transition, every route audits
/// under its own action, and no two routes can produce two arrivals for the same reservation.
/// </summary>
[Collection(ModulesDatabaseCollection.Name)]
public sealed class CheckInUnificationTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";

    // ---------------------------------------------------------------- one core, three routes

    [Fact]
    public async Task Kiosk_arrival_records_a_transition_an_audit_entry_and_one_notice()
    {
        await factory.ResetAsync();
        var ctx = await factory.SeedEligibleReservationAsync();
        var issue = await ctx.IssueAsync();

        var confirm = await factory.Client.PostAsJsonAsync("/api/totem/check-in/confirm", new { token = issue.Token });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        var visitId = (await confirm.Content.ReadFromJsonAsync<ArrivalPayload>())!.VisitId;

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // The kiosk used to skip this entirely, so a kiosk visit began with no history at all.
        var transition = Assert.Single(await db.VisitTransitions.Where(x => x.VisitId == visitId).ToListAsync());
        Assert.Null(transition.PreviousStatus);
        Assert.Equal(VisitStatus.Waiting, transition.NewStatus);
        Assert.Equal("TOTEM", transition.ActorUserId);
        Assert.False(transition.IsCorrection);

        var audit = Assert.Single(await db.AuditEntries
            .Where(x => x.TargetEntityId == visitId && x.Action == AuditActions.VisitCheckedIn).ToListAsync());
        Assert.Equal("SUCCEEDED", audit.Result);
        Assert.Equal(AuditTargetTypes.Visit, audit.TargetEntityType);
        Assert.Equal("TOTEM", audit.ActorUserId);

        var notice = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal(WhatsAppNotificationType.ClientCheckedIn, notice.Type);
        Assert.Equal($"CHECKIN:{visitId}", notice.IdempotencyKey);
    }

    [Fact]
    public async Task Reception_arrival_records_a_transition_under_its_own_audit_action()
    {
        await factory.ResetAsync();
        var ctx = await factory.SeedEligibleReservationAsync();
        var manager = await LoginStaffAsync(SystemRoles.Gerente);

        var response = await CheckInAtReceptionAsync(ctx.ReservationId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var visitId = (await response.Content.ReadFromJsonAsync<ReceptionVisitPayload>())!.Id;

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var transition = Assert.Single(await db.VisitTransitions.Where(x => x.VisitId == visitId).ToListAsync());
        Assert.Equal(VisitStatus.Waiting, transition.NewStatus);
        Assert.Equal(manager.Id, transition.ActorUserId);
        Assert.Single(await db.AuditEntries
            .Where(x => x.TargetEntityId == visitId && x.Action == AuditActions.VisitCheckedInManual).ToListAsync());
        Assert.Single(await factory.NotificationsAsync());
    }

    [Fact]
    public async Task Back_office_arrival_keeps_its_own_audit_action_and_links_the_reservation_customer()
    {
        await factory.ResetAsync();
        var ctx = await factory.SeedEligibleReservationAsync();
        await LoginStaffAsync(SystemRoles.Administrador);
        var professionalId = await ProfessionalOfAsync(ctx.ReservationId);

        var response = await factory.PostWithCsrfAsync("/api/admin/visits", new
        {
            professionalId,
            reservationId = ctx.ReservationId,
            visitorName = "Nome informado"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var visitId = (await response.Content.ReadFromJsonAsync<AdminVisitPayload>())!.Id;

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var visit = await db.Visits.SingleAsync(x => x.Id == visitId);
        // The reservation's customer names the visit and is linked to it, exactly as at the desk.
        Assert.Equal("Cliente Check-in", visit.VisitorName);
        Assert.NotNull(visit.CustomerId);
        Assert.Single(await db.VisitTransitions.Where(x => x.VisitId == visitId).ToListAsync());
        Assert.Single(await db.AuditEntries
            .Where(x => x.TargetEntityId == visitId && x.Action == AuditActions.VisitArrived).ToListAsync());
    }

    // ---------------------------------------------------------------- concurrency

    [Fact]
    public async Task Two_simultaneous_kiosk_confirms_produce_one_arrival_and_never_a_500()
    {
        await factory.ResetAsync();
        var ctx = await factory.SeedEligibleReservationAsync();
        var issue = await ctx.IssueAsync();

        var responses = await Task.WhenAll(
            factory.Client.PostAsJsonAsync("/api/totem/check-in/confirm", new { token = issue.Token }),
            factory.Client.PostAsJsonAsync("/api/totem/check-in/confirm", new { token = issue.Token }));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var ids = await Task.WhenAll(responses.Select(async r =>
            (await r.Content.ReadFromJsonAsync<ArrivalPayload>())!.VisitId));
        Assert.Single(ids.Distinct());
        await AssertExactlyOneArrivalAsync(ctx.ReservationId);
    }

    [Fact]
    public async Task A_kiosk_confirm_racing_the_reception_desk_produces_one_arrival()
    {
        await factory.ResetAsync();
        var ctx = await factory.SeedEligibleReservationAsync();
        var issue = await ctx.IssueAsync();
        await LoginStaffAsync(SystemRoles.Gerente);
        var token = await ReservationTokenAsync(ctx.ReservationId);

        var kiosk = factory.Client.PostAsJsonAsync("/api/totem/check-in/confirm", new { token = issue.Token });
        var desk = factory.PostWithCsrfAsync($"/api/reception/reservations/{ctx.ReservationId}/check-in",
            new { concurrencyToken = token });
        var results = await Task.WhenAll(kiosk, desk);

        Assert.Equal(HttpStatusCode.OK, results[0].StatusCode);
        Assert.Contains(results[1].StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
        await AssertExactlyOneArrivalAsync(ctx.ReservationId);
    }

    [Fact]
    public async Task The_reception_desk_racing_the_back_office_produces_one_arrival()
    {
        await factory.ResetAsync();
        var ctx = await factory.SeedEligibleReservationAsync();
        await LoginStaffAsync(SystemRoles.Administrador);
        var professionalId = await ProfessionalOfAsync(ctx.ReservationId);
        var token = await ReservationTokenAsync(ctx.ReservationId);

        var desk = factory.PostWithCsrfAsync($"/api/reception/reservations/{ctx.ReservationId}/check-in",
            new { concurrencyToken = token });
        var backOffice = factory.PostWithCsrfAsync("/api/admin/visits", new
        {
            professionalId, reservationId = ctx.ReservationId, visitorName = "Visitante"
        });
        var results = await Task.WhenAll(desk, backOffice);

        Assert.All(results, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK }));
        await AssertExactlyOneArrivalAsync(ctx.ReservationId);
    }

    /// <summary>
    /// The resource lock serialises the routes, so the index rarely fires in practice. It is still the only
    /// thing that holds if a future caller — a door controller, say — skips the lock, so it is asserted
    /// directly against the database.
    /// </summary>
    [Fact]
    public async Task The_database_refuses_a_second_open_visit_for_one_reservation()
    {
        await factory.ResetAsync();
        var ctx = await factory.SeedEligibleReservationAsync();
        var issue = await ctx.IssueAsync();
        Assert.Equal(HttpStatusCode.OK,
            (await factory.Client.PostAsJsonAsync("/api/totem/check-in/confirm", new { token = issue.Token })).StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var first = await db.Visits.AsNoTracking().SingleAsync(x => x.ReservationId == ctx.ReservationId);
        db.Visits.Add(Visit.Arrive(first.ProfessionalId, first.RoomId, ctx.ReservationId,
            "Segundo visitante", "TEST", factory.UtcNow));

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgres = Assert.IsType<PostgresException>(failure.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
        Assert.Equal("UX_Visits_OpenReservation", postgres.ConstraintName);
    }

    [Fact]
    public async Task A_closed_visit_frees_the_reservation_for_a_later_arrival()
    {
        await factory.ResetAsync();
        var ctx = await factory.SeedEligibleReservationAsync();
        await LoginStaffAsync(SystemRoles.Gerente);
        Assert.Equal(HttpStatusCode.Created, (await CheckInAtReceptionAsync(ctx.ReservationId)).StatusCode);
        await CloseVisitAsync(ctx.ReservationId, VisitStatus.Ended);

        // The index only covers WAITING and IN_SERVICE, so the desk can register a genuine second arrival.
        Assert.Equal(HttpStatusCode.Created, (await CheckInAtReceptionAsync(ctx.ReservationId)).StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await db.Visits.CountAsync(x => x.ReservationId == ctx.ReservationId));
        // Two real arrivals, two notices — each keyed to its own visit, never two for one visit.
        Assert.Equal(2, (await factory.NotificationsAsync()).Count);
    }

    // ---------------------------------------------------------------- the credential

    [Fact]
    public async Task A_desk_arrival_spends_the_credential_so_the_kiosk_cannot_reopen_the_visit_later()
    {
        await factory.ResetAsync();
        var ctx = await factory.SeedEligibleReservationAsync();
        var issue = await ctx.IssueAsync();
        await LoginStaffAsync(SystemRoles.Gerente);
        Assert.Equal(HttpStatusCode.Created, (await CheckInAtReceptionAsync(ctx.ReservationId)).StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var credential = await db.CheckInTokens.SingleAsync(x => x.ReservationId == ctx.ReservationId);
            Assert.NotNull(credential.UsedAt);
            Assert.Null(credential.ManualCodeHash);
        }

        // While the visit is open the kiosk still answers with it — presenting the QR again is not an error.
        var whileOpen = await factory.Client.PostAsJsonAsync("/api/totem/check-in/confirm", new { token = issue.Token });
        Assert.Equal(HttpStatusCode.OK, whileOpen.StatusCode);

        await CloseVisitAsync(ctx.ReservationId, VisitStatus.Ended);

        // Once the appointment is over the spent credential must not open a second visit.
        var afterEnd = await factory.Client.PostAsJsonAsync("/api/totem/check-in/confirm", new { token = issue.Token });
        Assert.Equal(HttpStatusCode.BadRequest, afterEnd.StatusCode);
        await using var verify = factory.Services.CreateAsyncScope();
        Assert.Equal(1, await verify.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .Visits.CountAsync(x => x.ReservationId == ctx.ReservationId));
    }

    [Fact]
    public async Task A_cancelled_visit_also_leaves_the_credential_spent()
    {
        await factory.ResetAsync();
        var ctx = await factory.SeedEligibleReservationAsync();
        var issue = await ctx.IssueAsync();
        Assert.Equal(HttpStatusCode.OK,
            (await factory.Client.PostAsJsonAsync("/api/totem/check-in/confirm", new { token = issue.Token })).StatusCode);
        await CloseVisitAsync(ctx.ReservationId, VisitStatus.Cancelled);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await factory.Client.PostAsJsonAsync("/api/totem/check-in/confirm", new { token = issue.Token })).StatusCode);
    }

    // ---------------------------------------------------------------- eligibility

    [Fact]
    public async Task The_kiosk_refuses_an_arrival_outside_the_window()
    {
        await factory.ResetAsync();
        var ctx = await factory.SeedEligibleReservationAsync();
        var issue = await ctx.IssueAsync();
        var reservationEnd = await ReservationEndAsync(ctx.ReservationId);
        factory.FreezeTime(reservationEnd.AddMinutes(1));
        try
        {
            Assert.Equal(HttpStatusCode.BadRequest,
                (await factory.Client.PostAsJsonAsync("/api/totem/check-in/confirm", new { token = issue.Token })).StatusCode);
            await AssertNoArrivalAsync(ctx.ReservationId);
        }
        finally { factory.UnfreezeTime(); }
    }

    [Fact]
    public async Task The_desk_refuses_a_reservation_that_is_no_longer_approved()
    {
        await factory.ResetAsync();
        var ctx = await factory.SeedEligibleReservationAsync();
        await LoginStaffAsync(SystemRoles.Gerente);
        var token = await ReservationTokenAsync(ctx.ReservationId);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var reservation = await db.Reservations.SingleAsync(x => x.Id == ctx.ReservationId);
            reservation.Cancel(ReservationCancellationActor, factory.UtcNow);
            await db.SaveChangesAsync();
        }

        var response = await factory.PostWithCsrfAsync(
            $"/api/reception/reservations/{ctx.ReservationId}/check-in", new { concurrencyToken = token });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertNoArrivalAsync(ctx.ReservationId);
    }

    [Fact]
    public async Task The_desk_refuses_an_inactive_customer()
    {
        await factory.ResetAsync();
        var ctx = await factory.SeedEligibleReservationAsync();
        await LoginStaffAsync(SystemRoles.Gerente);
        var token = await ReservationTokenAsync(ctx.ReservationId);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var reservation = await db.Reservations.AsNoTracking().SingleAsync(x => x.Id == ctx.ReservationId);
            await db.Customers.Where(x => x.Id == reservation.CustomerId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        }

        var response = await factory.PostWithCsrfAsync(
            $"/api/reception/reservations/{ctx.ReservationId}/check-in", new { concurrencyToken = token });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNoArrivalAsync(ctx.ReservationId);
    }

    [Fact]
    public async Task A_stale_reservation_version_is_a_conflict_not_a_server_error()
    {
        await factory.ResetAsync();
        var ctx = await factory.SeedEligibleReservationAsync();
        await LoginStaffAsync(SystemRoles.Gerente);

        var response = await factory.PostWithCsrfAsync(
            $"/api/reception/reservations/{ctx.ReservationId}/check-in",
            new { concurrencyToken = ConcurrencyToken.Encode(uint.MaxValue - 1) });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertNoArrivalAsync(ctx.ReservationId);
    }

    [Fact]
    public async Task An_unknown_reservation_is_a_404_at_the_desk()
    {
        await factory.ResetAsync();
        await LoginStaffAsync(SystemRoles.Gerente);
        var response = await factory.PostWithCsrfAsync(
            $"/api/reception/reservations/{Guid.NewGuid()}/check-in",
            new { concurrencyToken = ConcurrencyToken.Encode(1) });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------- helpers

    private const string ReservationCancellationActor = "TEST";

    private async Task<Microsoft.AspNetCore.Identity.IdentityUser> LoginStaffAsync(string role)
    {
        var user = await factory.CreateUserAsync($"checkin-staff-{Guid.NewGuid():N}@lumis.test", Password, [role]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(user.Email!, Password)).StatusCode);
        return user;
    }

    private Task<HttpResponseMessage> CheckInAtReceptionAsync(Guid reservationId) =>
        ReservationTokenAsync(reservationId).ContinueWith(task =>
            factory.PostWithCsrfAsync($"/api/reception/reservations/{reservationId}/check-in",
                new { concurrencyToken = task.Result })).Unwrap();

    private async Task<string> ReservationTokenAsync(Guid reservationId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return ConcurrencyToken.Encode((await db.Reservations.AsNoTracking()
            .SingleAsync(x => x.Id == reservationId)).Version);
    }

    private async Task<Guid> ProfessionalOfAsync(Guid reservationId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await db.Reservations.AsNoTracking().SingleAsync(x => x.Id == reservationId)).ProfessionalId;
    }

    private async Task<DateTimeOffset> ReservationEndAsync(Guid reservationId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await db.Reservations.AsNoTracking().SingleAsync(x => x.Id == reservationId)).EndAt;
    }

    private async Task CloseVisitAsync(Guid reservationId, VisitStatus status)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var visit = await db.Visits.SingleAsync(x => x.ReservationId == reservationId && (
            x.Status == VisitStatus.Waiting || x.Status == VisitStatus.InService));
        visit.Correct(status, "encerramento de teste", "TEST", factory.UtcNow);
        await db.SaveChangesAsync();
    }

    private async Task AssertExactlyOneArrivalAsync(Guid reservationId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var visits = await db.Visits.AsNoTracking().Where(x => x.ReservationId == reservationId).ToListAsync();
        var visit = Assert.Single(visits);
        Assert.Single(await db.VisitTransitions.AsNoTracking().Where(x => x.VisitId == visit.Id).ToListAsync());
        var notice = Assert.Single(await factory.NotificationsAsync());
        Assert.Equal($"CHECKIN:{visit.Id}", notice.IdempotencyKey);
    }

    private async Task AssertNoArrivalAsync(Guid reservationId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await db.Visits.CountAsync(x => x.ReservationId == reservationId));
        Assert.Empty(await factory.NotificationsAsync());
    }

    private sealed record ArrivalPayload(Guid VisitId, string Status);
    private sealed record ReceptionVisitPayload(Guid Id, string Status, string ConcurrencyToken);
    private sealed record AdminVisitPayload(Guid Id, string VisitorName, string Status);
}

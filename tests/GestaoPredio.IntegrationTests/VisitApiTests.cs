using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Reservations;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Security;
using GestaoPredio.Domain.Visits;
using GestaoPredio.Infrastructure.Identity;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class VisitApiTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";

    [Fact]
    public async Task Operations_register_arrival_from_a_compatible_reservation()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync();
        await LoginAsync(seed.Manager);

        var response = await factory.PostWithCsrfAsync("/api/admin/visits", new
        {
            professionalId = seed.OwnerProfessionalId,
            reservationId = seed.ReservationId,
            visitorName = "  Maria da Silva  "
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var visit = (await response.Content.ReadFromJsonAsync<VisitPayload>())!;
        Assert.Equal("Maria da Silva", visit.VisitorName);
        Assert.Equal("WAITING", visit.Status);
        Assert.Equal(seed.ReservationRoomId, visit.RoomId);
        Assert.Equal(seed.ReservationId, visit.ReservationId);
        Assert.Single(visit.History);
        Assert.Null(visit.History[0].PreviousStatus);
        Assert.Equal("WAITING", visit.History[0].NewStatus);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.AuditEntries.CountAsync(entry =>
            entry.Action == AuditActions.VisitArrived && entry.TargetEntityId == visit.Id));
    }

    [Fact]
    public async Task Arrival_rejects_reservation_with_a_different_professional_or_room()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync();
        await LoginAsync(seed.Admin);

        foreach (var body in new object[]
                 {
                     new { professionalId = seed.OtherProfessionalId, reservationId = seed.ReservationId,
                         visitorName = "Visitante" },
                     new { professionalId = seed.OwnerProfessionalId, roomId = seed.OtherRoomId,
                         reservationId = seed.ReservationId, visitorName = "Visitante" }
                 })
        {
            var response = await factory.PostWithCsrfAsync("/api/admin/visits", body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("INVALID_VISIT_RESOURCE", (await response.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
        }
    }

    [Fact]
    public async Task Waiting_must_start_before_end_and_current_token_is_required()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync();
        await LoginAsync(seed.Manager);
        var visit = await CreateVisitAsync(seed);

        var directEnd = await factory.PostWithCsrfAsync($"/api/admin/visits/{visit.Id}/end",
            new { concurrencyToken = visit.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.Conflict, directEnd.StatusCode);
        Assert.Equal("INVALID_VISIT_TRANSITION", (await directEnd.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
        var invalidToken = await factory.PostWithCsrfAsync($"/api/admin/visits/{visit.Id}/start",
            new { concurrencyToken = "invalid" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidToken.StatusCode);
        Assert.Equal("INVALID_CONCURRENCY_TOKEN",
            (await invalidToken.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);

        var startedResponse = await factory.PostWithCsrfAsync($"/api/admin/visits/{visit.Id}/start",
            new { concurrencyToken = visit.ConcurrencyToken });
        var started = (await startedResponse.Content.ReadFromJsonAsync<VisitPayload>())!;
        Assert.Equal("IN_SERVICE", started.Status);
        Assert.NotNull(started.ServiceStartedAt);
        Assert.NotEqual(visit.ConcurrencyToken, started.ConcurrencyToken);

        var stale = await factory.PostWithCsrfAsync($"/api/admin/visits/{visit.Id}/cancel",
            new { concurrencyToken = visit.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("RESOURCE_MODIFIED", (await stale.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);

        var ended = await factory.PostWithCsrfAsync($"/api/admin/visits/{visit.Id}/end",
            new { concurrencyToken = started.ConcurrencyToken });
        Assert.Equal("ENDED", (await ended.Content.ReadFromJsonAsync<VisitPayload>())!.Status);
    }

    [Fact]
    public async Task Waiting_and_in_service_visits_can_be_cancelled_but_terminal_visits_cannot_reopen()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync();
        await LoginAsync(seed.Admin);
        var visit = await CreateVisitAsync(seed);
        var cancelledResponse = await factory.PostWithCsrfAsync($"/api/admin/visits/{visit.Id}/cancel",
            new { concurrencyToken = visit.ConcurrencyToken });
        var cancelled = (await cancelledResponse.Content.ReadFromJsonAsync<VisitPayload>())!;
        Assert.Equal("CANCELLED", cancelled.Status);
        Assert.NotNull(cancelled.CancelledAt);

        var reopen = await factory.PostWithCsrfAsync($"/api/admin/visits/{visit.Id}/start",
            new { concurrencyToken = cancelled.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.Conflict, reopen.StatusCode);
    }

    [Fact]
    public async Task Professional_sees_and_changes_only_owned_visits()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync();
        await LoginAsync(seed.Admin);
        var owned = await CreateVisitAsync(seed);
        var foreign = await CreateVisitAsync(seed, seed.OtherProfessionalId, seed.OtherRoomId, null);

        await LoginAsync(seed.Owner);
        var page = (await (await factory.Client.GetAsync("/api/professional/visits?status=WAITING"))
            .Content.ReadFromJsonAsync<VisitPage>())!;
        Assert.Single(page.Items);
        Assert.Equal(owned.Id, page.Items[0].Id);
        Assert.Equal(HttpStatusCode.NotFound,
            (await factory.Client.GetAsync($"/api/professional/visits/{foreign.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await factory.PostWithCsrfAsync($"/api/professional/visits/{foreign.Id}/start",
                new { concurrencyToken = foreign.ConcurrencyToken })).StatusCode);

        var started = await factory.PostWithCsrfAsync($"/api/professional/visits/{owned.Id}/start",
            new { concurrencyToken = owned.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
    }

    [Fact]
    public async Task Operations_list_filters_in_the_database_and_returns_history_on_detail()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync();
        await LoginAsync(seed.Manager);
        var matching = await CreateVisitAsync(seed);
        await CreateVisitAsync(seed, seed.OtherProfessionalId, seed.OtherRoomId, null);

        var page = (await (await factory.Client.GetAsync(
            $"/api/admin/visits?status=WAITING&professionalId={seed.OwnerProfessionalId}&roomId={seed.ReservationRoomId}&page=1&pageSize=20"))
            .Content.ReadFromJsonAsync<VisitPage>())!;
        Assert.Single(page.Items);
        Assert.Equal(matching.Id, page.Items[0].Id);
        var detail = (await (await factory.Client.GetAsync($"/api/admin/visits/{matching.Id}"))
            .Content.ReadFromJsonAsync<VisitPayload>())!;
        Assert.Single(detail.History);

        var invalid = await factory.Client.GetAsync("/api/admin/visits?status=UNKNOWN");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("INVALID_STATUS", (await invalid.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
    }

    [Fact]
    public async Task Administrative_correction_requires_reason_and_records_immutable_history_and_audit()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync();
        await LoginAsync(seed.Admin);
        var waiting = await CreateVisitAsync(seed);
        var started = await TransitionAsync(waiting, "start");
        var ended = await TransitionAsync(started, "end");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await factory.PostWithCsrfAsync($"/api/admin/visits/{ended.Id}/correct",
                new { status = "IN_SERVICE", reason = " ", concurrencyToken = ended.ConcurrencyToken })).StatusCode);

        var response = await factory.PostWithCsrfAsync($"/api/admin/visits/{ended.Id}/correct", new
        {
            status = "IN_SERVICE", reason = "Encerramento lançado por engano", concurrencyToken = ended.ConcurrencyToken
        });
        var corrected = (await response.Content.ReadFromJsonAsync<VisitPayload>())!;
        Assert.Equal("IN_SERVICE", corrected.Status);
        Assert.Equal(4, corrected.History.Count);
        Assert.True(corrected.History[^1].IsCorrection);
        Assert.Equal("ENDED", corrected.History[^1].PreviousStatus);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.AuditEntries.CountAsync(entry =>
            entry.Action == AuditActions.VisitCorrected && entry.TargetEntityId == ended.Id));
    }

    [Fact]
    public async Task Visit_routes_enforce_role_policies_and_antiforgery()
    {
        await factory.ResetAsync();
        var seed = await SeedAsync();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.Client.GetAsync("/api/admin/visits")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.Client.GetAsync("/api/professional/visits")).StatusCode);

        await LoginAsync(seed.Owner);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await factory.PostWithCsrfAsync("/api/admin/visits", new
            {
                professionalId = seed.OwnerProfessionalId, visitorName = "Visitante"
            })).StatusCode);

        await factory.ResetAsync();
        seed = await SeedAsync();
        await LoginAsync(seed.Manager);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await factory.Client.PostAsJsonAsync("/api/admin/visits", new
            {
                professionalId = seed.OwnerProfessionalId, visitorName = "Visitante"
            })).StatusCode);
    }

    private async Task<VisitPayload> CreateVisitAsync(Seed seed, Guid? professionalId = null,
        Guid? roomId = null, Guid? reservationId = default)
    {
        var response = await factory.PostWithCsrfAsync("/api/admin/visits", new
        {
            professionalId = professionalId ?? seed.OwnerProfessionalId,
            roomId = roomId ?? seed.ReservationRoomId,
            reservationId,
            visitorName = $"Visitante {Guid.NewGuid():N}"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<VisitPayload>())!;
    }

    private async Task<VisitPayload> TransitionAsync(VisitPayload visit, string action)
    {
        var response = await factory.PostWithCsrfAsync($"/api/admin/visits/{visit.Id}/{action}",
            new { concurrencyToken = visit.ConcurrencyToken });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<VisitPayload>())!;
    }

    private async Task<Seed> SeedAsync()
    {
        var owner = await factory.CreateUserAsync($"owner-{Guid.NewGuid():N}@lumis.test", Password,
            [SystemRoles.Profissional]);
        var other = await factory.CreateUserAsync($"other-{Guid.NewGuid():N}@lumis.test", Password,
            [SystemRoles.Profissional]);
        var admin = await factory.CreateUserAsync($"admin-{Guid.NewGuid():N}@lumis.test", Password,
            [SystemRoles.Administrador]);
        var manager = await factory.CreateUserAsync($"manager-{Guid.NewGuid():N}@lumis.test", Password,
            [SystemRoles.Gerente]);
        var now = DateTimeOffset.UtcNow;
        var room = Room.Create($"Sala {Guid.NewGuid():N}", null, 10, 50, now);
        var otherRoom = Room.Create($"Sala {Guid.NewGuid():N}", null, 10, 50, now);
        var professional = Professional.Create("Profissional dono", "Fisioterapia", "+5565999999999", now);
        professional.LinkUser(owner.Id, now);
        var otherProfessional = Professional.Create("Outro profissional", "Psicologia", "+5565988888888", now);
        otherProfessional.LinkUser(other.Id, now);
        var reservation = Reservation.CreateApproved(room.Id, professional.Id,
            now.AddHours(-1), now.AddHours(1), admin.Id, now.AddHours(-2));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.AddRange(room, otherRoom, professional, otherProfessional, reservation);
        await db.SaveChangesAsync();
        return new Seed(owner, admin, manager, professional.Id, otherProfessional.Id,
            room.Id, otherRoom.Id, reservation.Id);
    }

    private async Task LoginAsync(ApplicationUser user) =>
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(user.Email!, Password)).StatusCode);

    private sealed record Seed(ApplicationUser Owner, ApplicationUser Admin, ApplicationUser Manager,
        Guid OwnerProfessionalId, Guid OtherProfessionalId, Guid ReservationRoomId,
        Guid OtherRoomId, Guid ReservationId);
    private sealed record VisitPage(IReadOnlyList<VisitPayload> Items, int Page, int PageSize, int TotalCount);
    private sealed record VisitPayload(Guid Id, Guid ProfessionalId, Guid? RoomId, Guid? ReservationId,
        string VisitorName, string Status, DateTimeOffset? ServiceStartedAt, DateTimeOffset? CancelledAt,
        string ConcurrencyToken, IReadOnlyList<TransitionPayload> History);
    private sealed record TransitionPayload(string? PreviousStatus, string NewStatus, bool IsCorrection);
    private sealed record ErrorPayload(string Code, string Message);
}

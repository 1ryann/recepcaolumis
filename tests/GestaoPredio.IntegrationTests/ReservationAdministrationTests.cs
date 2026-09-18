using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Auditing;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class ReservationAdministrationTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";

    [Fact]
    public async Task Operations_create_and_query_an_approved_reservation_with_audit()
    {
        await factory.ResetAsync();
        var resources = await SeedResourcesAsync();
        await LoginAsync(SystemRoles.Gerente);
        var start = factory.UtcNow.AddDays(2);

        var response = await factory.PostWithCsrfAsync("/api/admin/reservations",
            new { resources.RoomId, resources.ProfessionalId, startAt = start, endAt = start.AddHours(1) });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ReservationPayload>())!;
        Assert.Equal("NEW", created.Kind);
        Assert.Equal("APPROVED", created.Status);
        Assert.NotEmpty(created.ConcurrencyToken);
        var page = (await (await factory.Client.GetAsync("/api/admin/reservations?status=APPROVED"))
            .Content.ReadFromJsonAsync<ReservationPage>())!;
        Assert.Single(page.Items);
        Assert.Equal(created.Id, page.Items[0].Id);
        Assert.Equal(HttpStatusCode.OK,
            (await factory.Client.GetAsync($"/api/admin/reservations/{created.Id}")).StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.AuditEntries.CountAsync(entry =>
            entry.Action == AuditActions.ReservationCreated &&
            entry.TargetEntityType == AuditTargetTypes.Reservation &&
            entry.TargetEntityId == created.Id));
    }

    [Fact]
    public async Task Creation_revalidates_resource_availability_and_returns_stable_conflict()
    {
        await factory.ResetAsync();
        var resources = await SeedResourcesAsync();
        await LoginAsync(SystemRoles.Administrador);
        var start = factory.UtcNow.AddDays(2);
        var body = new { resources.RoomId, resources.ProfessionalId, startAt = start, endAt = start.AddHours(1) };
        (await factory.PostWithCsrfAsync("/api/admin/reservations", body)).EnsureSuccessStatusCode();

        var conflict = await factory.PostWithCsrfAsync("/api/admin/reservations", body);

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("RESERVATION_RESOURCE_CONFLICT",
            (await conflict.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.AuditEntries.CountAsync(entry => entry.Action == AuditActions.ReservationCreated));
    }

    [Fact]
    public async Task Administrative_mutation_requires_operations_and_antiforgery()
    {
        await factory.ResetAsync();
        var resources = await SeedResourcesAsync();
        var start = factory.UtcNow.AddDays(2);
        var body = new { resources.RoomId, resources.ProfessionalId, startAt = start, endAt = start.AddHours(1) };
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.PostWithCsrfAsync("/api/admin/reservations", body)).StatusCode);

        var professional = await factory.CreateUserAsync($"professional-{Guid.NewGuid():N}@lumis.test", Password,
            [SystemRoles.Profissional]);
        Assert.Equal(HttpStatusCode.NoContent,
            (await factory.LoginAsync(professional.Email!, Password)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await factory.PostWithCsrfAsync("/api/admin/reservations", body)).StatusCode);

        await factory.ResetAsync();
        resources = await SeedResourcesAsync();
        await LoginAsync(SystemRoles.Gerente);
        body = new { resources.RoomId, resources.ProfessionalId, startAt = start, endAt = start.AddHours(1) };
        Assert.Equal(HttpStatusCode.BadRequest,
            (await factory.Client.PostAsJsonAsync("/api/admin/reservations", body)).StatusCode);
    }

    private async Task<(Guid RoomId, Guid ProfessionalId)> SeedResourcesAsync()
    {
        await factory.SeedDefaultOperatingHoursAsync();
        var now = factory.UtcNow;
        var room = Room.Create($"Sala {Guid.NewGuid():N}", null, 10, 50, now);
        var professional = Professional.Create("Profissional Reserva", "Fisioterapia", "+5565999999999", now);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.AddRange(room, professional);
        await db.SaveChangesAsync();
        return (room.Id, professional.Id);
    }

    private async Task LoginAsync(string role)
    {
        var user = await factory.CreateUserAsync($"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@lumis.test",
            Password, [role]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(user.Email!, Password)).StatusCode);
    }

    private sealed record ReservationPage(IReadOnlyList<ReservationPayload> Items, int Page, int PageSize, int TotalCount);
    private sealed record ReservationPayload(Guid Id, string Kind, string Status, string ConcurrencyToken);
    private sealed record ErrorPayload(string Code, string Message);
}

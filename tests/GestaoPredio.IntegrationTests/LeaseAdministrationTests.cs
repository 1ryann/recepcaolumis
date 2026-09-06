using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Security;
using GestaoPredio.Domain.Tenants;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class LeaseAdministrationTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";

    [Fact]
    public async Task Operations_can_create_and_query_a_lease_with_occurrence_and_audit()
    {
        await factory.ResetAsync();
        var resources = await SeedResourcesAsync();
        await LoginAsync(SystemRoles.Gerente);
        var response = await factory.PostWithCsrfAsync("/api/admin/leases", Body(resources));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<LeasePayload>())!;
        Assert.Equal("AGENDADA", created.Status);
        Assert.NotEmpty(created.ConcurrencyToken);
        var page = (await (await factory.Client.GetAsync("/api/admin/leases?status=AGENDADA"))
            .Content.ReadFromJsonAsync<LeasePage>())!;
        Assert.Single(page.Items);
        Assert.Equal(HttpStatusCode.OK,
            (await factory.Client.GetAsync($"/api/admin/leases/{created.Id}")).StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(await db.LeaseOccurrences.ToListAsync());
        Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "LEASE_CREATED"));
    }

    [Fact]
    public async Task Overlapping_room_or_professional_is_rejected_without_success_audit()
    {
        await factory.ResetAsync();
        var resources = await SeedResourcesAsync();
        await LoginAsync(SystemRoles.Administrador);
        (await factory.PostWithCsrfAsync("/api/admin/leases", Body(resources))).EnsureSuccessStatusCode();

        var conflict = await factory.PostWithCsrfAsync("/api/admin/leases", Body(resources));

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("LEASE_RESOURCE_CONFLICT", (await conflict.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "LEASE_CREATED"));
    }

    [Fact]
    public async Task Creation_requires_operations_authorization_csrf_and_strict_json()
    {
        await factory.ResetAsync();
        var resources = await SeedResourcesAsync();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.PostWithCsrfAsync("/api/admin/leases", Body(resources))).StatusCode);

        await LoginAsync(SystemRoles.Profissional);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await factory.PostWithCsrfAsync("/api/admin/leases", Body(resources))).StatusCode);

        await factory.ResetAsync();
        resources = await SeedResourcesAsync();
        await LoginAsync(SystemRoles.Gerente);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await factory.Client.PostAsJsonAsync("/api/admin/leases", Body(resources))).StatusCode);

        var body = Body(resources);
        var extra = new
        {
            body.TenantId, body.ProfessionalId, body.RoomId, body.Mode, body.ContractedRate,
            body.BillingStartAt, body.BillingDueDay, body.OccupancyStartAt, body.OccupancyEndAt,
            lifecycleState = "ENDED"
        };
        Assert.Equal(HttpStatusCode.BadRequest,
            (await factory.PostWithCsrfAsync("/api/admin/leases", extra)).StatusCode);
    }

    [Fact]
    public async Task Inactive_resource_is_rejected_and_search_filters_in_the_database()
    {
        await factory.ResetAsync();
        var resources = await SeedResourcesAsync();
        await LoginAsync(SystemRoles.Administrador);
        (await factory.PostWithCsrfAsync("/api/admin/leases", Body(resources))).EnsureSuccessStatusCode();

        var matching = (await (await factory.Client.GetAsync("/api/admin/leases?search=profissional%20teste&roomId=" + resources.Room.Id))
            .Content.ReadFromJsonAsync<LeasePage>())!;
        Assert.Single(matching.Items);
        var missing = (await (await factory.Client.GetAsync("/api/admin/leases?search=inexistente"))
            .Content.ReadFromJsonAsync<LeasePage>())!;
        Assert.Empty(missing.Items);

        await factory.ResetAsync();
        resources = await SeedResourcesAsync();
        await LoginAsync(SystemRoles.Administrador);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var room = await db.Rooms.SingleAsync(x => x.Id == resources.Room.Id);
        room.Deactivate(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await factory.PostWithCsrfAsync("/api/admin/leases", Body(resources))).StatusCode);
    }

    private static CreateLeaseBody Body((Tenant Tenant, Professional Professional, Room Room) value) => new(
        value.Tenant.Id, value.Professional.Id, value.Room.Id, "HOURLY", 150.50m,
        DateTimeOffset.UtcNow.AddDays(-1), 10, DateTimeOffset.UtcNow.AddDays(1),
        DateTimeOffset.UtcNow.AddDays(1).AddHours(2));

    private async Task<(Tenant Tenant, Professional Professional, Room Room)> SeedResourcesAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var tenant = Tenant.Create("Locatário Teste", TenantKind.Individual, now);
        var professional = Professional.Create("Profissional Teste", "Teste", "65999990002", now);
        var room = Room.Create($"Sala {Guid.NewGuid():N}", null, 100m, 500m, now);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.AddRange(tenant, professional, room);
        await db.SaveChangesAsync();
        return (tenant, professional, room);
    }

    private async Task LoginAsync(string role)
    {
        var email = $"lease-{Guid.NewGuid():N}@lumis.test";
        await factory.CreateUserAsync(email, Password, [role]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(email, Password)).StatusCode);
    }

    private sealed record LeasePage(IReadOnlyList<LeasePayload> Items, int Page, int PageSize, int TotalCount);
    private sealed record LeasePayload(Guid Id, string Status, string ConcurrencyToken);
    private sealed record ErrorPayload(string Code, string Message);
    private sealed record CreateLeaseBody(Guid TenantId, Guid ProfessionalId, Guid RoomId, string Mode,
        decimal ContractedRate, DateTimeOffset BillingStartAt, int? BillingDueDay,
        DateTimeOffset OccupancyStartAt, DateTimeOffset? OccupancyEndAt);
}

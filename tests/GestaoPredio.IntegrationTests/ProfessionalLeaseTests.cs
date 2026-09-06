using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Leases;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Security;
using GestaoPredio.Domain.Tenants;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class ProfessionalLeaseTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";

    [Fact]
    public async Task Linked_professional_reads_only_owned_leases_and_idor_returns_not_found()
    {
        await factory.ResetAsync();
        var owner = await factory.CreateUserAsync($"owner-{Guid.NewGuid():N}@lumis.test", Password, [SystemRoles.Profissional]);
        var other = await factory.CreateUserAsync($"other-{Guid.NewGuid():N}@lumis.test", Password, [SystemRoles.Profissional]);
        var (ownedLease, foreignLease) = await SeedLeasesAsync(owner.Id, other.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(owner.Email!, Password)).StatusCode);
        var page = (await (await factory.Client.GetAsync("/api/professional/leases"))
            .Content.ReadFromJsonAsync<ProfessionalLeasePage>())!;
        Assert.Single(page.Items);
        Assert.Equal(ownedLease, page.Items[0].Id);
        Assert.Equal(HttpStatusCode.OK, (await factory.Client.GetAsync($"/api/professional/leases/{ownedLease}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.Client.GetAsync($"/api/professional/leases/{foreignLease}")).StatusCode);
    }

    [Fact]
    public async Task Professional_endpoints_reject_anonymous_and_non_professional_and_unlinked_gets_empty_page()
    {
        await factory.ResetAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.Client.GetAsync("/api/professional/leases")).StatusCode);

        var manager = await factory.CreateUserAsync($"manager-{Guid.NewGuid():N}@lumis.test", Password, [SystemRoles.Gerente]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(manager.Email!, Password)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.Client.GetAsync("/api/professional/leases")).StatusCode);

        await factory.ResetAsync();
        var professional = await factory.CreateUserAsync($"unlinked-{Guid.NewGuid():N}@lumis.test", Password, [SystemRoles.Profissional]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(professional.Email!, Password)).StatusCode);
        var page = (await (await factory.Client.GetAsync("/api/professional/leases"))
            .Content.ReadFromJsonAsync<ProfessionalLeasePage>())!;
        Assert.Empty(page.Items);
    }

    private async Task<(Guid Owned, Guid Foreign)> SeedLeasesAsync(string ownerUserId, string otherUserId)
    {
        var now = DateTimeOffset.UtcNow;
        var tenant = Tenant.Create("Locatário", TenantKind.Individual, now);
        var room1 = Room.Create($"Sala {Guid.NewGuid():N}", null, 10m, 100m, now);
        var room2 = Room.Create($"Sala {Guid.NewGuid():N}", null, 10m, 100m, now);
        var owner = Professional.Create("Profissional dono", "Médico", "65999990010", now);
        owner.LinkUser(ownerUserId, now);
        var other = Professional.Create("Outro profissional", "Médico", "65999990011", now);
        other.LinkUser(otherUserId, now);
        var owned = Lease.Create(tenant.Id, owner.Id, room1.Id, LeaseMode.Hourly, 10m,
            now, null, now.AddHours(1), now.AddHours(2), null, now);
        var foreign = Lease.Create(tenant.Id, other.Id, room2.Id, LeaseMode.Hourly, 10m,
            now, null, now.AddHours(1), now.AddHours(2), null, now);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.AddRange(tenant, room1, room2, owner, other, owned, foreign);
        await db.SaveChangesAsync();
        return (owned.Id, foreign.Id);
    }

    private sealed record ProfessionalLeasePage(IReadOnlyList<ProfessionalLeasePayload> Items, int Page, int PageSize, int TotalCount);
    private sealed record ProfessionalLeasePayload(Guid Id, string TenantName, string RoomName, string Status, string ConcurrencyToken);
}

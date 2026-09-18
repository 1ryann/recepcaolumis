using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class RoomQueryTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";

    [Fact]
    public async Task Room_queries_require_operations_policy()
    {
        await factory.ResetAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.Client.GetAsync("/api/admin/rooms")).StatusCode);
        await LoginAsAsync(SystemRoles.Profissional, "room-query-prof@lumis.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.Client.GetAsync("/api/admin/rooms")).StatusCode);
    }

    [Theory]
    [InlineData(SystemRoles.Administrador)]
    [InlineData(SystemRoles.Gerente)]
    public async Task Operations_roles_receive_default_page(string role)
    {
        await factory.ResetAsync();
        await LoginAsAsync(role, $"room-query-{role.ToLowerInvariant()}@lumis.test");
        var response = await factory.Client.GetAsync("/api/admin/rooms");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = (await response.Content.ReadFromJsonAsync<RoomPage>())!;
        Assert.Empty(page.Items);
        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task Search_status_paging_and_normalized_order_are_stable()
    {
        await factory.ResetAsync();
        var alpha = Room.Create("Sála Alfa", null, 10m, 20m, factory.UtcNow);
        var beta = Room.Create("sala Beta", null, 10m, 20m, factory.UtcNow);
        var inactive = Room.Create("Sala Inativa", null, 10m, 20m, factory.UtcNow);
        inactive.Deactivate(factory.UtcNow);
        await SeedAsync(alpha, beta, inactive, Room.Create("Consultório", null, 0m, 0m, factory.UtcNow));
        await LoginAsAsync(SystemRoles.Gerente, "room-query-search@lumis.test");

        var active = (await (await factory.Client.GetAsync(
            "/api/admin/rooms?search=%20s%C3%A1la%20&status=active&page=1&pageSize=100"))
            .Content.ReadFromJsonAsync<RoomPage>())!;
        Assert.Equal(2, active.TotalCount);
        Assert.Equal(["Sála Alfa", "sala Beta"], active.Items.Select(x => x.Name));

        var second = (await (await factory.Client.GetAsync(
            "/api/admin/rooms?search=sala&status=all&page=2&pageSize=2"))
            .Content.ReadFromJsonAsync<RoomPage>())!;
        Assert.Single(second.Items);
        Assert.Equal("Sala Inativa", second.Items[0].Name);
        Assert.Equal(3, second.TotalCount);
    }

    [Theory]
    [InlineData("page=0", "INVALID_PAGE")]
    [InlineData("pageSize=101", "INVALID_PAGE_SIZE")]
    [InlineData("status=removed", "INVALID_STATUS")]
    public async Task Invalid_room_query_uses_stable_error(string query, string code)
    {
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Administrador, $"room-query-invalid-{Guid.NewGuid():N}@lumis.test");
        var response = await factory.Client.GetAsync($"/api/admin/rooms?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
    }

    [Fact]
    public async Task Detail_returns_only_approved_fields_and_missing_is_not_found()
    {
        await factory.ResetAsync();
        var room = Room.Create("Sala 1", "Clara", 100.50m, 800m, factory.UtcNow);
        await SeedAsync(room);
        await LoginAsAsync(SystemRoles.Administrador, "room-query-detail@lumis.test");
        var response = await factory.Client.GetAsync($"/api/admin/rooms/{room.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"concurrencyToken\"", json);
        Assert.DoesNotContain("normalizedName", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("professional", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound,
            (await factory.Client.GetAsync($"/api/admin/rooms/{Guid.NewGuid()}")).StatusCode);
    }

    private async Task SeedAsync(params Room[] rooms)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Rooms.AddRange(rooms);
        await db.SaveChangesAsync();
    }

    private async Task LoginAsAsync(string role, string email)
    {
        await factory.CreateUserAsync(email, Password, [role]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(email, Password)).StatusCode);
    }

    private sealed record RoomPage(IReadOnlyList<RoomItem> Items, int Page, int PageSize, int TotalCount);
    private sealed record RoomItem(Guid Id, string Name, bool IsActive, string ConcurrencyToken);
    private sealed record ErrorPayload(string Code, string Message);
}

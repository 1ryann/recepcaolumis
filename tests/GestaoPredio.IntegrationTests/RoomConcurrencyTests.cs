using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class RoomConcurrencyTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";

    [Fact]
    public async Task Two_reads_allow_first_edit_and_reject_second_without_success_audit()
    {
        await PrepareAsync();
        var created = await CreateAsync("Sala Concorrente");
        var firstRead = await GetAsync(created.Id);
        var secondRead = await GetAsync(created.Id);
        Assert.Equal(firstRead.ConcurrencyToken, secondRead.ConcurrencyToken);

        var first = await factory.PutWithCsrfAsync($"/api/admin/rooms/{created.Id}", new
        {
            name = "Sala A", description = (string?)null, hourlyRate = 1m, dailyRate = 2m,
            concurrencyToken = firstRead.ConcurrencyToken
        });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.NotEqual(firstRead.ConcurrencyToken, (await first.Content.ReadFromJsonAsync<RoomPayload>())!.ConcurrencyToken);

        var second = await factory.PutWithCsrfAsync($"/api/admin/rooms/{created.Id}", new
        {
            name = "Sala B", description = (string?)null, hourlyRate = 1m, dailyRate = 2m,
            concurrencyToken = secondRead.ConcurrencyToken
        });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("RESOURCE_MODIFIED", (await second.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "ROOM_UPDATED"));
    }

    [Fact]
    public async Task Status_conflict_is_distinct_from_name_conflict_and_creates_no_false_audit()
    {
        await PrepareAsync();
        var created = await CreateAsync("Sala Status");
        var first = await factory.PostWithCsrfAsync($"/api/admin/rooms/{created.Id}/deactivate",
            new { concurrencyToken = created.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var stale = await factory.PostWithCsrfAsync($"/api/admin/rooms/{created.Id}/deactivate",
            new { concurrencyToken = created.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("RESOURCE_MODIFIED", (await stale.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "ROOM_DEACTIVATED"));
    }

    private async Task PrepareAsync()
    {
        await factory.ResetAsync();
        await factory.CreateUserAsync("room-concurrency@lumis.test", Password, [SystemRoles.Administrador]);
        Assert.Equal(HttpStatusCode.NoContent,
            (await factory.LoginAsync("room-concurrency@lumis.test", Password)).StatusCode);
    }

    private async Task<RoomPayload> CreateAsync(string name)
    {
        var response = await factory.PostWithCsrfAsync("/api/admin/rooms", new
            { name, description = (string?)null, hourlyRate = 1m, dailyRate = 2m });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RoomPayload>())!;
    }

    private async Task<RoomPayload> GetAsync(Guid id) =>
        (await (await factory.Client.GetAsync($"/api/admin/rooms/{id}"))
            .Content.ReadFromJsonAsync<RoomPayload>())!;

    private sealed record RoomPayload(Guid Id, string ConcurrencyToken);
    private sealed record ErrorPayload(string Code, string Message);
}

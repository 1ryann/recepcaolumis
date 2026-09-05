using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class RoomMutationTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";

    [Fact]
    public async Task Room_mutations_require_operations_and_antiforgery()
    {
        await factory.ResetAsync();
        var body = ValidBody("Sala 1");
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.Client.PostAsJsonAsync("/api/admin/rooms", body)).StatusCode);
        await LoginAsAsync(SystemRoles.Profissional, "room-mutation-prof@lumis.test");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await factory.PostWithCsrfAsync("/api/admin/rooms", body)).StatusCode);
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Gerente, "room-mutation-csrf@lumis.test");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await factory.Client.PostAsJsonAsync("/api/admin/rooms", body)).StatusCode);
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("100", "100.5")]
    [InlineData("100.50", "100.99")]
    [InlineData("9999999999999.99", "9999999999999.99")]
    public async Task Approved_rates_are_persisted_exactly(string hourlyText, string dailyText)
    {
        var hourly = decimal.Parse(hourlyText, System.Globalization.CultureInfo.InvariantCulture);
        var daily = decimal.Parse(dailyText, System.Globalization.CultureInfo.InvariantCulture);
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Administrador, $"room-rate-valid-{Guid.NewGuid():N}@lumis.test");
        var response = await factory.PostWithCsrfAsync("/api/admin/rooms", new
        {
            name = $"Sala {Guid.NewGuid():N}", description = "Descrição", hourlyRate = hourly, dailyRate = daily
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<RoomPayload>())!;
        Assert.Equal(hourly, created.HourlyRate);
        Assert.Equal(daily, created.DailyRate);
        Assert.True(created.IsActive);
        Assert.NotEmpty(created.ConcurrencyToken);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.Rooms.SingleAsync(x => x.Id == created.Id);
        Assert.Equal(hourly, stored.HourlyRate);
        Assert.Equal(daily, stored.DailyRate);
        Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "ROOM_CREATED"));
    }

    [Theory]
    [InlineData("-1", "10")]
    [InlineData("10.555", "10")]
    [InlineData("10000000000000", "10")]
    public async Task Invalid_rates_are_rejected_before_persistence(string hourlyText, string dailyText)
    {
        var hourly = decimal.Parse(hourlyText, System.Globalization.CultureInfo.InvariantCulture);
        var daily = decimal.Parse(dailyText, System.Globalization.CultureInfo.InvariantCulture);
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Gerente, $"room-rate-invalid-{Guid.NewGuid():N}@lumis.test");
        var response = await factory.PostWithCsrfAsync("/api/admin/rooms", new
        {
            name = "Sala 1", description = (string?)null, hourlyRate = hourly, dailyRate = daily
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_ROOM_RATE", (await response.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
    }

    [Fact]
    public async Task Missing_fields_lengths_currency_string_and_status_overposting_are_rejected()
    {
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Administrador, "room-invalid-contract@lumis.test");
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.PostWithCsrfAsync("/api/admin/rooms", new
            { name = "", hourlyRate = 1m, dailyRate = 2m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.PostWithCsrfAsync("/api/admin/rooms", new
            { name = new string('N', 101), hourlyRate = 1m, dailyRate = 2m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.PostWithCsrfAsync("/api/admin/rooms", new
            { name = "Sala", description = new string('D', 1001), hourlyRate = 1m, dailyRate = 2m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.PostWithCsrfAsync("/api/admin/rooms", new
            { name = "Sala", hourlyRate = "R$ 100,00", dailyRate = 2m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.PostWithCsrfAsync("/api/admin/rooms", new
            { name = "Sala", hourlyRate = 1m, dailyRate = 2m, isActive = false })).StatusCode);
        var missingRate = await factory.PostWithCsrfAsync("/api/admin/rooms", new
            { name = "Sala", hourlyRate = 1m });
        Assert.Equal(HttpStatusCode.BadRequest, missingRate.StatusCode);
        Assert.Equal("INVALID_ROOM_RATE", (await missingRate.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
    }

    [Fact]
    public async Task Equivalent_name_is_blocked_even_when_existing_room_is_inactive()
    {
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Administrador, "room-duplicate@lumis.test");
        var created = await CreateAsync("Sala 01");
        var disabled = await factory.PostWithCsrfAsync($"/api/admin/rooms/{created.Id}/deactivate",
            new { concurrencyToken = created.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);

        var duplicate = await factory.PostWithCsrfAsync("/api/admin/rooms", ValidBody("  SÁLA   01 "));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("ROOM_NAME_ALREADY_EXISTS", (await duplicate.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
    }

    [Fact]
    public async Task Update_can_keep_own_name_is_noop_and_audits_only_effective_fields()
    {
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Gerente, "room-update@lumis.test");
        var created = await CreateAsync("Sala 1");
        var noopBody = new
        {
            name = "Sala 1", description = (string?)null, hourlyRate = 100m, dailyRate = 500m,
            concurrencyToken = created.ConcurrencyToken
        };
        var noop = await factory.PutWithCsrfAsync($"/api/admin/rooms/{created.Id}", noopBody);
        Assert.Equal(HttpStatusCode.OK, noop.StatusCode);
        Assert.Equal(created.ConcurrencyToken, (await noop.Content.ReadFromJsonAsync<RoomPayload>())!.ConcurrencyToken);

        var changed = await factory.PutWithCsrfAsync($"/api/admin/rooms/{created.Id}", new
        {
            name = "Sala Azul", description = "Nova", hourlyRate = 100.50m, dailyRate = 600m,
            concurrencyToken = created.ConcurrencyToken
        });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var updated = (await changed.Content.ReadFromJsonAsync<RoomPayload>())!;
        Assert.NotEqual(created.ConcurrencyToken, updated.ConcurrencyToken);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal("SALA AZUL", (await db.Rooms.SingleAsync(x => x.Id == created.Id)).NormalizedName);
        var audit = await db.AuditEntries.SingleAsync(x => x.Action == "ROOM_UPDATED");
        Assert.Equal("DailyRate,Description,HourlyRate,Name", audit.ChangedFields);
    }

    [Fact]
    public async Task Rename_to_another_equivalent_name_conflicts()
    {
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Administrador, "room-rename-conflict@lumis.test");
        await CreateAsync("Sala Única");
        var other = await CreateAsync("Sala Outra");
        var response = await factory.PutWithCsrfAsync($"/api/admin/rooms/{other.Id}", new
        {
            name = "sala unica", description = (string?)null, hourlyRate = 100m, dailyRate = 500m,
            concurrencyToken = other.ConcurrencyToken
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ROOM_NAME_ALREADY_EXISTS", (await response.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
    }

    [Fact]
    public async Task Status_operations_audit_real_changes_and_keep_current_state_as_noop()
    {
        await factory.ResetAsync();
        await LoginAsAsync(SystemRoles.Gerente, "room-status@lumis.test");
        var created = await CreateAsync("Sala Status");

        var noop = await factory.PostWithCsrfAsync($"/api/admin/rooms/{created.Id}/activate",
            new { concurrencyToken = created.ConcurrencyToken });
        Assert.Equal(created.ConcurrencyToken,
            (await noop.Content.ReadFromJsonAsync<RoomPayload>())!.ConcurrencyToken);

        var deactivatedResponse = await factory.PostWithCsrfAsync($"/api/admin/rooms/{created.Id}/deactivate",
            new { concurrencyToken = created.ConcurrencyToken });
        var deactivated = (await deactivatedResponse.Content.ReadFromJsonAsync<RoomPayload>())!;
        Assert.False(deactivated.IsActive);

        var activatedResponse = await factory.PostWithCsrfAsync($"/api/admin/rooms/{created.Id}/activate",
            new { concurrencyToken = deactivated.ConcurrencyToken });
        var activated = (await activatedResponse.Content.ReadFromJsonAsync<RoomPayload>())!;
        Assert.True(activated.IsActive);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "ROOM_DEACTIVATED"));
        Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Action == "ROOM_ACTIVATED"));
    }

    private async Task<RoomPayload> CreateAsync(string name)
    {
        var response = await factory.PostWithCsrfAsync("/api/admin/rooms", ValidBody(name));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RoomPayload>())!;
    }

    private static object ValidBody(string name) => new
        { name, description = (string?)null, hourlyRate = 100m, dailyRate = 500m };

    private async Task LoginAsAsync(string role, string email)
    {
        await factory.CreateUserAsync(email, Password, [role]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(email, Password)).StatusCode);
    }

    private sealed record RoomPayload(Guid Id, decimal HourlyRate, decimal DailyRate, bool IsActive,
        DateTimeOffset UpdatedAt, string ConcurrencyToken);
    private sealed record ErrorPayload(string Code, string Message);
}

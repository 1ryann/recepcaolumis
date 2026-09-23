using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using GestaoPredio.Domain.AccessControl;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

/// <summary>
/// The endpoint an Intelbras Bio-T controller reports to, while it is still in capture mode: it must
/// authenticate the device, record only the structure of what arrived, collapse replays, and never grant
/// access.
/// </summary>
[Collection(ModulesDatabaseCollection.Name)]
public sealed class IntelbrasAccessCaptureTests(ModulesApiFactory factory)
{
    private const string Secret = "b7f3c1a95e2d40a8b6c4f80d13e75a92";   // 32 chars, as the device path carries

    [Fact]
    public async Task An_unknown_path_secret_is_indistinguishable_from_a_wrong_path()
    {
        await factory.ResetAsync();
        var response = await PostAsync("0123456789abcdef0123456789abcdef", new { evento = "teste" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertEventCountAsync(0);
    }

    [Fact]
    public async Task A_deactivated_device_stops_being_accepted()
    {
        await factory.ResetAsync();
        await SeedDeviceAsync(active: false);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(Secret, new { evento = "teste" })).StatusCode);
        await AssertEventCountAsync(0);
    }

    [Fact]
    public async Task A_known_device_is_recorded_and_refused_access()
    {
        await factory.ResetAsync();
        await SeedDeviceAsync();

        var response = await PostAsync(Secret, new { id = "42", UserID = "10293847", CardNo = "A1B2C3D4" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<AuthReply>())!;

        // Capture mode never opens the door.
        Assert.False(body.Auth);
        Assert.Equal("42", body.Id);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var recorded = await db.AccessEvents.SingleAsync();
        Assert.Equal(AccessEventDisposition.Captured, recorded.Disposition);
        Assert.NotNull(recorded.DeviceId);
    }

    [Fact]
    public async Task The_recorded_shape_keeps_the_field_names_and_drops_every_value()
    {
        await factory.ResetAsync();
        await SeedDeviceAsync();
        // Raw JSON, not PostAsJsonAsync: the serializer would camel-case the names, and the whole point
        // is that the shape preserves whatever casing the device actually sends.
        using (var content = new StringContent(
                   """{"UserID":"10293847","CardNo":"SEGREDO-DO-CARTAO","Photo":"AAAABBBBCCCC"}""",
                   Encoding.UTF8, "application/json"))
        {
            var posted = await factory.Client.PostAsync($"/api/integrations/intelbras/access/{Secret}", content);
            Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var shape = (await db.AccessEvents.SingleAsync()).PayloadShape!;

        // The names are what the normalizer needs…
        Assert.Contains("UserID", shape, StringComparison.Ordinal);
        Assert.Contains("CardNo", shape, StringComparison.Ordinal);
        Assert.Contains("Photo", shape, StringComparison.Ordinal);
        Assert.Contains("string(", shape, StringComparison.Ordinal);
        // …and the values are exactly what must never be written down. A payload can carry an access
        // photo, a face template or a live credential.
        Assert.DoesNotContain("10293847", shape, StringComparison.Ordinal);
        Assert.DoesNotContain("SEGREDO-DO-CARTAO", shape, StringComparison.Ordinal);
        Assert.DoesNotContain("AAAABBBBCCCC", shape, StringComparison.Ordinal);
    }

    /// <summary>
    /// "Transmissão continuada" replays everything a device queued while offline. Without this the first
    /// network outage would produce a second arrival and a second WhatsApp notice per queued event.
    /// </summary>
    [Fact]
    public async Task An_identical_replay_is_recorded_once()
    {
        await factory.ResetAsync();
        await SeedDeviceAsync();
        var payload = new { id = "7", UserID = "555", Time = "2026-09-23T02:00:00" };

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(Secret, payload)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(Secret, payload)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(Secret, payload)).StatusCode);

        await AssertEventCountAsync(1);
    }

    [Fact]
    public async Task Concurrent_replays_of_one_event_still_record_once()
    {
        await factory.ResetAsync();
        await SeedDeviceAsync();
        var payload = new { id = "9", UserID = "777" };

        var responses = await Task.WhenAll(
            PostAsync(Secret, payload), PostAsync(Secret, payload), PostAsync(Secret, payload));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        await AssertEventCountAsync(1);
    }

    [Fact]
    public async Task A_different_event_is_recorded_separately()
    {
        await factory.ResetAsync();
        await SeedDeviceAsync();
        await PostAsync(Secret, new { id = "1", UserID = "111" });
        await PostAsync(Secret, new { id = "2", UserID = "222" });
        await AssertEventCountAsync(2);
    }

    [Fact]
    public async Task A_body_that_is_not_json_is_recorded_without_its_contents()
    {
        await factory.ResetAsync();
        await SeedDeviceAsync();

        using var content = new StringContent("UserID=123&CardNo=SEGREDO", Encoding.UTF8, "application/x-www-form-urlencoded");
        var response = await factory.Client.PostAsync($"/api/integrations/intelbras/access/{Secret}", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var shape = (await db.AccessEvents.SingleAsync()).PayloadShape!;
        Assert.StartsWith("non-json", shape, StringComparison.Ordinal);
        Assert.DoesNotContain("SEGREDO", shape, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Keep_alive_answers_the_device_and_records_the_contact()
    {
        await factory.ResetAsync();
        var deviceId = await SeedDeviceAsync();

        var response = await factory.Client.GetAsync($"/api/integrations/intelbras/access/{Secret}/keep-alive");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.NotNull((await db.AccessDevices.SingleAsync(x => x.Id == deviceId)).LastSeenAt);
        // Keep-alive is not an access event.
        await AssertEventCountAsync(0);
    }

    [Fact]
    public async Task Keep_alive_from_an_unknown_device_is_refused()
    {
        await factory.ResetAsync();
        var response = await factory.Client.GetAsync(
            "/api/integrations/intelbras/access/ffffffffffffffffffffffffffffffff/keep-alive");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_oversized_body_is_refused_without_being_recorded()
    {
        await factory.ResetAsync();
        await SeedDeviceAsync();

        using var content = new StringContent(new string('x', 300 * 1024), Encoding.UTF8, "application/json");
        var response = await factory.Client.PostAsync($"/api/integrations/intelbras/access/{Secret}", content);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        await AssertEventCountAsync(0);
    }

    [Fact]
    public async Task Capture_mode_never_creates_a_visit_or_a_notification()
    {
        await factory.ResetAsync();
        await SeedDeviceAsync();
        await PostAsync(Secret, new { id = "1", UserID = "111", CardNo = "ABC" });

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await db.Visits.CountAsync());
        Assert.Equal(0, await db.VisitTransitions.CountAsync());
        Assert.Empty(await factory.NotificationsAsync());
    }

    // ---------------------------------------------------------------- helpers

    private Task<HttpResponseMessage> PostAsync(string secret, object payload) =>
        factory.Client.PostAsJsonAsync($"/api/integrations/intelbras/access/{secret}", payload);

    private async Task<Guid> SeedDeviceAsync(bool active = true)
    {
        var device = AccessDevice.Register("Entrada principal", $"BXS{Random.Shared.Next(100000, 999999)}",
            SHA256.HashData(Encoding.UTF8.GetBytes(Secret)), factory.UtcNow);
        if (!active) device.Deactivate();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.AccessDevices.Add(device);
        await db.SaveChangesAsync();
        return device.Id;
    }

    private async Task AssertEventCountAsync(int expected)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(expected, await db.AccessEvents.CountAsync());
    }

    private sealed record AuthReply(string? Id, bool Auth, string Message);
}

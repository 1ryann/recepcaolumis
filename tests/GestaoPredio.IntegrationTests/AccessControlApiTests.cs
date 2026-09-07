using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.AccessControl;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class AccessControlApiTests(ModulesApiFactory factory)
{
    private const string Password = "Valid-Password-123!";

    [Fact]
    public async Task Anonymous_and_non_operations_users_cannot_release_the_door()
    {
        await factory.ResetAsync();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.Client.PostAsJsonAsync("/api/reception/access/door/release", new { })).StatusCode);

        var professional = await factory.CreateUserAsync($"access-prof-{Guid.NewGuid():N}@lumis.test", Password,
            [SystemRoles.Profissional]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(professional.Email!, Password)).StatusCode);
        var forbidden = await factory.PostWithCsrfAsync("/api/reception/access/door/release", new { });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var customer = await factory.CreateUserAsync($"access-customer-{Guid.NewGuid():N}@lumis.test", Password,
            [SystemRoles.Customer]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(customer.Email!, Password)).StatusCode);
        forbidden = await factory.PostWithCsrfAsync("/api/reception/access/door/release", new { });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Admin_and_manager_can_release_with_csrf_and_audit_without_creating_visit()
    {
        await factory.ResetAsync();
        var recorder = factory.Services.GetRequiredService<AccessControlDemoRecorder>();
        recorder.Clear();
        factory.Services.GetRequiredService<AccessControlCooldown>().Clear();
        var admin = await factory.CreateUserAsync($"access-admin-{Guid.NewGuid():N}@lumis.test", Password,
            [SystemRoles.Administrador]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(admin.Email!, Password)).StatusCode);

        var first = await factory.PostWithCsrfAsync("/api/reception/access/door/release", new { });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var payload = (await first.Content.ReadFromJsonAsync<ReleasePayload>())!;
        Assert.True(payload.Success);
        Assert.True(payload.CommandAccepted);
        Assert.Equal("Demo", payload.Provider);
        Assert.Single(recorder.Attempts);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(0, await db.Visits.CountAsync());
            var audits = await db.AuditEntries.Where(x => x.Action.StartsWith("DOOR_RELEASE_")).ToListAsync();
            Assert.Contains(audits, x => x.Action == "DOOR_RELEASE_REQUESTED" && x.Result == "SUCCEEDED");
            Assert.Contains(audits, x => x.Action == "DOOR_RELEASE_SUCCEEDED" && x.Result == "SUCCEEDED");
            Assert.All(audits, x => Assert.DoesNotContain("token", x.CorrelationId, StringComparison.OrdinalIgnoreCase));
        }

        await factory.ResetAsync();
        factory.Services.GetRequiredService<AccessControlCooldown>().Clear();
        var manager = await factory.CreateUserAsync($"access-manager-{Guid.NewGuid():N}@lumis.test", Password,
            [SystemRoles.Gerente]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(manager.Email!, Password)).StatusCode);
        var managerResponse = await factory.PostWithCsrfAsync("/api/reception/access/door/release", new { });
        Assert.Equal(HttpStatusCode.OK, managerResponse.StatusCode);
    }

    [Fact]
    public async Task Missing_csrf_is_rejected_and_client_cannot_choose_door_or_credentials()
    {
        await factory.ResetAsync();
        var admin = await factory.CreateUserAsync($"access-strict-{Guid.NewGuid():N}@lumis.test", Password,
            [SystemRoles.Administrador]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(admin.Email!, Password)).StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/reception/access/door/release")
        {
            Content = JsonContent.Create(new { })
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.Client.SendAsync(request)).StatusCode);

        var unknownProperty = await factory.PostWithCsrfAsync("/api/reception/access/door/release",
            new { doorId = "ATTACKER_CONTROLLED", ip = "https://example.invalid", token = "secret" });
        Assert.Equal(HttpStatusCode.BadRequest, unknownProperty.StatusCode);
    }

    [Fact]
    public async Task Cooldown_returns_controlled_429_and_does_not_call_provider_twice()
    {
        await factory.ResetAsync();
        var recorder = factory.Services.GetRequiredService<AccessControlDemoRecorder>();
        recorder.Clear();
        factory.Services.GetRequiredService<AccessControlCooldown>().Clear();
        var manager = await factory.CreateUserAsync($"access-cooldown-{Guid.NewGuid():N}@lumis.test", Password,
            [SystemRoles.Gerente]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(manager.Email!, Password)).StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await factory.PostWithCsrfAsync("/api/reception/access/door/release", new { })).StatusCode);
        var second = await factory.PostWithCsrfAsync("/api/reception/access/door/release", new { });
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal("ACCESS_CONTROL_COOLDOWN", (await second.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);
        Assert.Single(recorder.Attempts);
    }

    [Fact]
    public async Task Demo_provider_failure_does_not_mutate_visit_and_is_audited_safely()
    {
        await factory.ResetAsync();
        var recorder = factory.Services.GetRequiredService<AccessControlDemoRecorder>();
        recorder.Clear();
        factory.Services.GetRequiredService<AccessControlCooldown>().Clear();
        recorder.ForceFailure = true;
        var admin = await factory.CreateUserAsync($"access-failure-{Guid.NewGuid():N}@lumis.test", Password,
            [SystemRoles.Administrador]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(admin.Email!, Password)).StatusCode);

        var response = await factory.PostWithCsrfAsync("/api/reception/access/door/release", new { });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var payload = (await response.Content.ReadFromJsonAsync<ReleasePayload>())!;
        Assert.False(payload.Success);
        Assert.Equal("DEMO_ACCESS_FAILURE", payload.FailureCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await db.Visits.CountAsync());
        Assert.Contains(await db.AuditEntries.ToListAsync(), x => x.Action == "DOOR_RELEASE_FAILED" && x.Result == "FAILED");
    }

    [Fact]
    public async Task Unknown_visit_context_returns_404_without_attempting_release()
    {
        await factory.ResetAsync();
        var recorder = factory.Services.GetRequiredService<AccessControlDemoRecorder>();
        recorder.Clear();
        factory.Services.GetRequiredService<AccessControlCooldown>().Clear();
        var admin = await factory.CreateUserAsync($"access-context-{Guid.NewGuid():N}@lumis.test", Password,
            [SystemRoles.Administrador]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(admin.Email!, Password)).StatusCode);

        var response = await factory.PostWithCsrfAsync("/api/reception/access/door/release",
            new { visitId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(recorder.Attempts);
    }

    private sealed record ReleasePayload(bool Success, bool CommandAccepted, string Provider, string? FailureCode);
    private sealed record ErrorPayload(string Code, string Message);
}

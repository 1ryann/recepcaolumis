using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Security;

namespace GestaoPredio.IntegrationTests;

[Collection(AuthDatabaseCollection.Name)]
public sealed class AntiforgeryTests(AuthApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => factory.ResetAsync();

    [Fact]
    public async Task Csrf_endpoint_returns_token_and_secure_cookie()
    {
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var response = await client.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace((await response.Content.ReadFromJsonAsync<TokenPayload>())!.Token));
        var cookie = response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Host-Lumis.Csrf="));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_without_csrf_is_rejected()
    {
        var response = await factory.Client.PostAsJsonAsync("/api/auth/login", new { email = "a@b.test", password = "Not-Secret-123!" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record TokenPayload(string Token);
}

[Collection(ModulesDatabaseCollection.Name)]
public sealed class RoomPhotoAntiforgeryTests(ModulesApiFactory factory)
{
    [Theory]
    [InlineData("upload", false)]
    [InlineData("delete", false)]
    [InlineData("reorder", false)]
    [InlineData("cover", false)]
    [InlineData("upload", true)]
    [InlineData("delete", true)]
    [InlineData("reorder", true)]
    [InlineData("cover", true)]
    public async Task Photo_mutations_require_valid_csrf(string operation, bool invalid)
    {
        await factory.ResetAsync();
        await factory.CreateUserAsync("photo-csrf@lumis.test", "Valid-Password-123!", [SystemRoles.Gerente]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync("photo-csrf@lumis.test", "Valid-Password-123!")).StatusCode);
        using var request = RoomPhotoHttpRequests.Create(operation, Guid.NewGuid(), Guid.NewGuid());
        if (invalid) request.Headers.Add("X-CSRF-TOKEN", "invalid");
        var response = await factory.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("INVALID_CSRF", await response.Content.ReadAsStringAsync());
    }
}

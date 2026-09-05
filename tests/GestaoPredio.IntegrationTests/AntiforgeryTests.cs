using System.Net;
using System.Net.Http.Json;

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

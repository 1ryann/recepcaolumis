using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace GestaoPredio.IntegrationTests;

public class FoundationTests
{
    private static WebApplicationFactory<recepcaototem.Pages.IndexModel> CreateApi(int limit = 120) =>
        new WebApplicationFactory<recepcaototem.Pages.IndexModel>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:DefaultConnection", "");
            builder.UseSetting("RateLimiting:PermitLimit", limit.ToString());
            builder.UseSetting("AllowedHosts", "localhost");
            builder.UseSetting("Security:DataProtectionPath", Path.Combine(Path.GetTempPath(), "Lumis-Test-Keys"));
            builder.UseSetting("Storage:PrivateFilesPath", Path.GetTempPath());
        });

    [Fact]
    public async Task Liveness_starts_without_database_and_returns_no_infrastructure_details()
    {
        await using var api = CreateApi();
        using var client = api.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"status\":\"Healthy\"}", await response.Content.ReadAsStringAsync());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task Readiness_without_connection_is_generic_service_unavailable()
    {
        await using var api = CreateApi();
        var response = await api.CreateClient().GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("{\"status\":\"Unhealthy\"}", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/api/auth/session")]
    [InlineData("/api/admin/future")]
    [InlineData("/swagger/index.html")]
    [InlineData("/openapi/v1.json")]
    public async Task Nonpublic_routes_never_redirect_or_return_content_to_anonymous(string path)
    {
        await using var api = CreateApi();
        using var client = api.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Unapproved_origin_does_not_receive_cors_permission()
    {
        await using var api = CreateApi();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/health");
        request.Headers.Add("Origin", "https://untrusted.invalid");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        var response = await api.CreateClient().SendAsync(request);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Excess_requests_return_429()
    {
        await using var api = CreateApi(2);
        using var client = api.CreateClient();
        await client.GetAsync("/health/ready");
        await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task Content_security_policy_allows_data_fonts_and_blob_workers_without_relaxing_scripts()
    {
        await using var api = CreateApi();
        using var client = api.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var response = await client.GetAsync("/health");

        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        var directives = csp.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        // The two directives the Totem QR scanner needs: a data: font fallback and a blob: Web Worker.
        Assert.Contains("font-src 'self' data:", directives);
        Assert.Contains("worker-src 'self' blob:", directives);

        // Scripts stay locked to same-origin — no unsafe-eval, no blob:, no wildcard anywhere.
        Assert.Contains("script-src 'self'", directives);
        Assert.DoesNotContain("unsafe-eval", csp);
        Assert.DoesNotContain("*", csp);
        Assert.DoesNotContain("blob:", csp.Split(';').Single(d => d.TrimStart().StartsWith("script-src")));

        // Framing / object / base URI stay restrictive.
        Assert.Contains("frame-ancestors 'none'", directives);
        Assert.Contains("object-src 'none'", directives);
        Assert.Contains("base-uri 'self'", directives);

        // Full contract — any future relaxation shows up as a diff here.
        Assert.Equal(
            "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self' data:; " +
            "img-src 'self' data: blob:; connect-src 'self'; media-src 'self' blob:; worker-src 'self' blob:; " +
            "object-src 'none'; base-uri 'self'; frame-ancestors 'none'",
            csp);
    }
}

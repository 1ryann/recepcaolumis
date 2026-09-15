using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using GestaoPredio.Application.Abstractions;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Identity;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using recepcaototem.Api.Middleware;

namespace GestaoPredio.IntegrationTests;

public class SecurityTests
{
    private sealed class CapturingLogger : ILogger<GlobalExceptionMiddleware>
    {
        public Exception? LoggedException { get; private set; }
        public LogLevel? LoggedLevel { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            LoggedLevel = logLevel;
            LoggedException = exception;
        }
    }

    private static WebApplicationFactory<recepcaototem.Pages.IndexModel> Api(bool databaseOnline) =>
        new WebApplicationFactory<recepcaototem.Pages.IndexModel>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Security:DataProtectionPath", Path.Combine(Path.GetTempPath(), "Lumis-Test-Keys"));
            builder.UseSetting("Storage:PrivateFilesPath", Path.GetTempPath());
            builder.UseSetting("Whatsapp:FinanceiroPhoneNumber", "+5569999999999");
            builder.UseSetting("ConnectionStrings:DefaultConnection", "");
            builder.ConfigureServices(services => services.AddScoped<IDatabaseProbe>(_ => new Probe(databaseOnline)));
        });

    private sealed class Probe(bool connected) : IDatabaseProbe
    {
        public Task<bool> CanConnectAsync(CancellationToken cancellationToken) => Task.FromResult(connected);
    }

    [Fact]
    public async Task Readiness_uses_database_probe_result_without_details()
    {
        await using var api = Api(true);
        var result = await api.CreateClient().GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("{\"status\":\"Healthy\"}", await result.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Plain_http_health_is_available_but_session_requires_https()
    {
        await using var api = Api(false);
        using var client = api.CreateClient(new() { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        var result = await client.GetAsync("/api/auth/session");
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("HTTPS_REQUIRED", await result.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(SystemRoles.Administrador, "Administration", true)]
    [InlineData(SystemRoles.Gerente, "Administration", false)]
    [InlineData(SystemRoles.Gerente, "Operations", true)]
    [InlineData(SystemRoles.Profissional, "Operations", false)]
    public async Task Policies_enforce_role_boundaries(string role, string policy, bool permitted)
    {
        await using var api = Api(false);
        using var scope = api.Services.CreateScope();
        var user = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Role, role),
            new Claim("lumis:active", "true"),
            new Claim("lumis:must_change_password", "false")
        ], "test"));
        var result = await scope.ServiceProvider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, null, policy);
        Assert.Equal(permitted, result.Succeeded);
    }

    [Fact]
    public async Task Unhandled_exception_never_exposes_its_message_or_stack()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.RequestServices = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider();
        var exception = new InvalidOperationException("sensitive-database-connection");
        var logger = new CapturingLogger();
        var middleware = new GlobalExceptionMiddleware(_ => throw exception, logger);
        await middleware.InvokeAsync(context);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Equal(500, context.Response.StatusCode);
        Assert.Contains("INTERNAL_ERROR", body);
        Assert.DoesNotContain("sensitive-database-connection", body);
        Assert.DoesNotContain("InvalidOperationException", body);
        Assert.Equal(LogLevel.Error, logger.LoggedLevel);
        Assert.Same(exception, logger.LoggedException);
    }

    [Fact]
    public void PostgreSql_model_preserves_identity_key_lengths_and_contains_audit()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
        Assert.NotNull(db.Model.FindEntityType(typeof(ApplicationUser)));
        var token = db.Model.FindEntityType(typeof(Microsoft.AspNetCore.Identity.IdentityUserToken<string>))!;
        Assert.Equal(128, token.FindProperty("Name")!.GetMaxLength());
        Assert.Contains("AuditEntries", db.Database.GenerateCreateScript());
    }
}

[Collection(ModulesDatabaseCollection.Name)]
public sealed class RoomPhotoSecurityTests(ModulesApiFactory factory)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Every_room_photo_route_requires_operations(bool professional)
    {
        await factory.ResetAsync();
        if (professional)
        {
            await factory.CreateUserAsync("photo-security@lumis.test", "Valid-Password-123!", [SystemRoles.Profissional]);
            Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync("photo-security@lumis.test", "Valid-Password-123!")).StatusCode);
        }
        foreach (var operation in RoomPhotoHttpRequests.Operations)
        {
            using var request = RoomPhotoHttpRequests.Create(operation, Guid.NewGuid(), Guid.NewGuid());
            request.Headers.Add("X-CSRF-TOKEN", await factory.GetCsrfTokenAsync());
            var response = await factory.Client.SendAsync(request);
            Assert.Equal(professional ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}

internal static class RoomPhotoHttpRequests
{
    internal static readonly string[] Operations = ["list", "bytes", "upload", "delete", "reorder", "cover"];

    internal static HttpRequestMessage Create(string operation, Guid roomId, Guid photoId)
    {
        var path = $"/api/admin/rooms/{roomId}/photos";
        var request = operation switch
        {
            "list" => new HttpRequestMessage(HttpMethod.Get, path),
            "bytes" => new HttpRequestMessage(HttpMethod.Get, $"{path}/{photoId}"),
            "upload" => new HttpRequestMessage(HttpMethod.Post, path),
            "delete" => new HttpRequestMessage(HttpMethod.Delete, $"{path}/{photoId}"),
            "reorder" => new HttpRequestMessage(HttpMethod.Put, $"{path}/reorder") { Content = JsonContent.Create(new { orderedPhotoIds = new[] { photoId } }) },
            "cover" => new HttpRequestMessage(HttpMethod.Post, $"{path}/{photoId}/cover"),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        if (operation == "upload")
        {
            var multipart = new MultipartFormDataContent();
            var file = new ByteArrayContent(TestImageData.Png());
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            multipart.Add(file, "file", "room.png");
            request.Content = multipart;
        }
        return request;
    }
}

[Collection(ModulesDatabaseCollection.Name)]
public sealed class PublicRoomCatalogSecurityTests(ModulesApiFactory factory)
{
    [Fact]
    public async Task Public_room_catalog_routes_are_anonymous_and_do_not_fall_through_to_the_authenticated_api_catch_all()
    {
        await factory.ResetAsync();

        Assert.Equal(HttpStatusCode.OK, (await factory.Client.GetAsync("/api/totem/rooms")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await factory.Client.GetAsync($"/api/totem/rooms/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await factory.Client.GetAsync($"/api/totem/rooms/{Guid.NewGuid()}/photos/{Guid.NewGuid()}")).StatusCode);
    }
}

[Collection(ModulesDatabaseCollection.Name)]
public sealed class RoomRentalInquirySecurityTests(ModulesApiFactory factory)
{
    [Fact]
    public async Task Rental_inquiry_route_is_anonymous_and_does_not_fall_through_to_the_authenticated_api_catch_all()
    {
        await factory.ResetAsync();

        // A route that fell through to `app.Map("/api/{**path}", ...).RequireAuthorization()` would answer
        // 401 for an anonymous caller; a properly mapped, AllowAnonymous route instead reaches the handler
        // and reports 404 for a room that does not exist.
        var response = await factory.Client.PostAsJsonAsync($"/api/totem/rooms/{Guid.NewGuid()}/rental-inquiries",
            new { fullName = "Ana Souza", whatsApp = "+5569999999999", professionOrCompany = "Clínica A", note = (string?)null });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

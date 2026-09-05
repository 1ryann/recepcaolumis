using System.Net.Http.Json;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Identity;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GestaoPredio.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AuthDatabaseCollection : ICollectionFixture<AuthApiFactory>
{
    public const string Name = "AuthDatabase";
}

public sealed class AuthApiFactory : WebApplicationFactory<recepcaototem.Pages.IndexModel>, IAsyncLifetime
{
    public const string TestConnection =
        "Server=localhost\\SQLEXPRESS;Database=GestaoPredioAuthTests;Integrated Security=True;Encrypt=True;TrustServerCertificate=True";

    public HttpClient Client { get; private set; } = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ValidateTestConnection(TestConnection);
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", TestConnection);
        builder.UseSetting("AllowedHosts", "localhost");
        builder.UseSetting("Security:DataProtectionPath", Path.Combine(Path.GetTempPath(), "Lumis-Auth-Test-Keys"));
        builder.UseSetting("Storage:PrivateFilesPath", Path.Combine(Path.GetTempPath(), "Lumis-Auth-Test-PrivateFiles"));
        builder.UseSetting("RateLimiting:LoginPermitLimit", "20");
        builder.UseSetting("RateLimiting:LoginIdentifierPermitLimit", "20");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(TestConnection));
        });
    }

    public static void ValidateTestConnection(string connection)
    {
        var parsed = new SqlConnectionStringBuilder(connection);
        if (string.Equals(parsed.InitialCatalog, "GestaoPredioDB", StringComparison.OrdinalIgnoreCase) ||
            !parsed.InitialCatalog.StartsWith("GestaoPredioAuthTests", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Authentication tests require a GestaoPredioAuthTests database.");
    }

    public async Task InitializeAsync()
    {
        Client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
        await ResetAsync(db);
        await EnsureRolesAsync(scope.ServiceProvider);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        Client.Dispose();
        await base.DisposeAsync();
    }

    public async Task ResetAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await ResetAsync(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        Client.Dispose();
        Client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
        });
    }

    private static async Task ResetAsync(ApplicationDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [AuditEntries]");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [AspNetUserRoles]");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [AspNetUsers]");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [AspNetRoles]");
    }

    private static async Task EnsureRolesAsync(IServiceProvider services)
    {
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in SystemRoles.All)
            if (!await roles.RoleExistsAsync(role))
                Assert.True((await roles.CreateAsync(new IdentityRole(role))).Succeeded);
    }

    public async Task<ApplicationUser> CreateUserAsync(
        string email,
        string password,
        string role = SystemRoles.Administrador,
        bool isActive = true,
        bool mustChangePassword = false)
        => await CreateUserAsync(email, password, [role], isActive, mustChangePassword);

    public async Task<ApplicationUser> CreateUserAsync(
        string email,
        string password,
        IReadOnlyCollection<string> roles,
        bool isActive = true,
        bool mustChangePassword = false,
        string displayName = "Test User")
    {
        await using var scope = Services.CreateAsyncScope();
        await EnsureRolesAsync(scope.ServiceProvider);

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = displayName,
            EmailConfirmed = true,
            IsActive = isActive,
            MustChangePassword = mustChangePassword
        };
        Assert.True((await userManager.CreateAsync(user, password)).Succeeded);
        foreach (var role in roles)
            Assert.True((await userManager.AddToRoleAsync(user, role)).Succeeded);
        return user;
    }

    public async Task<string> GetCsrfTokenAsync()
    {
        var response = await Client.GetAsync("/api/auth/csrf");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CsrfPayload>())!.Token;
    }

    public async Task<HttpResponseMessage> LoginAsync(string email, string password, string? csrf = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password })
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf ?? await GetCsrfTokenAsync());
        return await Client.SendAsync(request);
    }

    public async Task<HttpResponseMessage> PostWithCsrfAsync(string path, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", await GetCsrfTokenAsync());
        return await Client.SendAsync(request);
    }

    public Task<HttpResponseMessage> PutWithCsrfAsync(string path, object body) =>
        SendWithCsrfAsync(HttpMethod.Put, path, body);

    public Task<HttpResponseMessage> DeleteWithCsrfAsync(string path, object body) =>
        SendWithCsrfAsync(HttpMethod.Delete, path, body);

    private async Task<HttpResponseMessage> SendWithCsrfAsync(HttpMethod method, string path, object body)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", await GetCsrfTokenAsync());
        return await Client.SendAsync(request);
    }

    private sealed record CsrfPayload(string Token);
}

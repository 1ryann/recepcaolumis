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
public sealed class ModulesDatabaseCollection : ICollectionFixture<ModulesApiFactory>
{
    public const string Name = "ModulesDatabase";
}

public sealed class ModulesApiFactory : WebApplicationFactory<recepcaototem.Pages.IndexModel>, IAsyncLifetime
{
    public const string DatabasePrefix = "GestaoPredioModulesTests";
    private static readonly string StorageBase = Path.Combine(Path.GetTempPath(), "Lumis-ModulesTests");
    private bool _databaseInitialized;

    public ModulesApiFactory()
    {
        var suffix = Guid.NewGuid().ToString("N");
        DatabaseName = $"{DatabasePrefix}_{suffix}";
        ConnectionString = CreateTestConnection(DatabaseName);
        PrivateFilesRoot = Path.Combine(StorageBase, suffix);
        ValidateTestConfiguration("Testing", ConnectionString);
    }

    public string DatabaseName { get; }
    public string ConnectionString { get; }
    public string PrivateFilesRoot { get; }
    public HttpClient Client { get; private set; } = null!;

    public static string CreateTestConnection(string database) =>
        new SqlConnectionStringBuilder
        {
            DataSource = "localhost\\SQLEXPRESS",
            InitialCatalog = database,
            IntegratedSecurity = true,
            Encrypt = true,
            TrustServerCertificate = true
        }.ConnectionString;

    public static void ValidateTestConfiguration(string environment, string? connection)
    {
        if (string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Module tests cannot run in Production.");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("Module tests require an explicit connection string.");

        var parsed = new SqlConnectionStringBuilder(connection);
        if (string.Equals(parsed.InitialCatalog, "GestaoPredioDB", StringComparison.OrdinalIgnoreCase) ||
            !parsed.InitialCatalog.StartsWith(DatabasePrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Module tests require a {DatabasePrefix} database.");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ValidateTestConfiguration("Testing", ConnectionString);
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", ConnectionString);
        builder.UseSetting("AllowedHosts", "localhost");
        builder.UseSetting("Security:DataProtectionPath", Path.Combine(PrivateFilesRoot, "keys"));
        builder.UseSetting("Storage:PrivateFilesPath", PrivateFilesRoot);
        builder.UseSetting("Storage:ProfessionalPhotoMaxBytes", (5 * 1024 * 1024).ToString());
        builder.UseSetting("RateLimiting:LoginPermitLimit", "100");
        builder.UseSetting("RateLimiting:LoginIdentifierPermitLimit", "100");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(ConnectionString));
        });
    }

    public async Task InitializeAsync()
    {
        ValidateTestConfiguration("Testing", ConnectionString);
        Directory.CreateDirectory(PrivateFilesRoot);
        Client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
        _databaseInitialized = true;
        await ResetDatabaseAsync(db);
        await EnsureRolesAsync(scope.ServiceProvider);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        Client?.Dispose();
        if (_databaseInitialized)
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            ValidateTestConfiguration("Testing", db.Database.GetConnectionString());
            await db.Database.EnsureDeletedAsync();
        }

        DeletePrivateRootSafely();
        await base.DisposeAsync();
    }

    public async Task ResetAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await ResetDatabaseAsync(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        Client.Dispose();
        Client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    public async Task<ApplicationUser> CreateUserAsync(
        string email,
        string password,
        IReadOnlyCollection<string>? roles = null,
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
        foreach (var role in roles ?? [SystemRoles.Administrador])
            Assert.True((await userManager.AddToRoleAsync(user, role)).Succeeded);
        return user;
    }

    public async Task<string> GetCsrfTokenAsync()
    {
        var response = await Client.GetAsync("/api/auth/csrf");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CsrfPayload>())!.Token;
    }

    public async Task<HttpResponseMessage> LoginAsync(string email, string password)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password })
        };
        request.Headers.Add("X-CSRF-TOKEN", await GetCsrfTokenAsync());
        return await Client.SendAsync(request);
    }

    public Task<HttpResponseMessage> PostWithCsrfAsync(string path, object body) =>
        SendWithCsrfAsync(HttpMethod.Post, path, body);

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

    private static async Task ResetDatabaseAsync(ApplicationDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [Professionals]");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [Rooms]");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [PrivateFiles]");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [AuditEntries]");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [AspNetUserRoles]");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [AspNetUsers]");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [AspNetRoles]");
    }

    private static async Task EnsureRolesAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in SystemRoles.All)
            if (!await roleManager.RoleExistsAsync(role))
                Assert.True((await roleManager.CreateAsync(new IdentityRole(role))).Succeeded);
    }

    private void DeletePrivateRootSafely()
    {
        var root = Path.GetFullPath(PrivateFilesRoot);
        var safeBase = Path.GetFullPath(StorageBase) + Path.DirectorySeparatorChar;
        if (!root.StartsWith(safeBase, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to delete a private-files path outside the test root.");
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private sealed record CsrfPayload(string Token);
}

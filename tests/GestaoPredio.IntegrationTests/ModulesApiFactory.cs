using System.Net.Http.Json;
using System.Text;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Identity;
using GestaoPredio.Infrastructure.Persistence;
using GestaoPredio.Domain.Availability;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace GestaoPredio.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ModulesDatabaseCollection : ICollectionFixture<ModulesApiFactory>
{
    public const string Name = "ModulesDatabase";
}

public sealed class ModulesApiFactory : WebApplicationFactory<recepcaototem.Pages.IndexModel>, IAsyncLifetime
{
    private static readonly string StorageBase = Path.Combine(Path.GetTempPath(), "Lumis-ModulesTests");
    private readonly string _baseConnection;
    private bool _schemaCreated;

    public ModulesApiFactory()
    {
        var suffix = Guid.NewGuid().ToString("N");
        DatabaseName = LocalPostgreSqlTestDatabase.DatabaseName;
        SchemaName = $"{LocalPostgreSqlTestDatabase.SchemaPrefix}{suffix}";
        _baseConnection = LocalPostgreSqlTestDatabase.LoadBaseConnection();
        ConnectionString = LocalPostgreSqlTestDatabase.WithSchema(_baseConnection, SchemaName);
        PrivateFilesRoot = Path.Combine(StorageBase, suffix);
        ValidateTestConfiguration("Testing", ConnectionString);
    }

    public string DatabaseName { get; }
    public string SchemaName { get; }
    public string ConnectionString { get; }
    public string PrivateFilesRoot { get; }
    public HttpClient Client { get; private set; } = null!;

    /// <summary>
    /// Sink wired into the SAME server <see cref="Client"/> talks to (registered unconditionally in
    /// <see cref="ConfigureWebHost"/>). <see cref="CaptureLogs"/> hands out an offset-anchored view so a
    /// test can assert the pipeline never logged a secret / code / hash (spec 7A.10 / Task 16).
    /// </summary>
    private readonly CapturingLoggerProvider _logSink = new();

    /// <summary>The effective configuration of the running server (Task 16 RULING 3).</summary>
    public IConfiguration Configuration => Services.GetRequiredService<IConfiguration>();

    /// <summary>Anchor a log view at "everything logged from now on" — call before the request under test.</summary>
    public LogCapture CaptureLogs() => new(_logSink, _logSink.Length);

    /// <summary>
    /// A throw-away derived host with one or more configuration values overridden, plus a ready client
    /// (Task 16 RULING 3). Same Postgres schema and clock as the parent (config inherited). Dispose
    /// disposes both the derived factory and its client.
    /// </summary>
    public ConfiguredFactory WithConfig(params (string Key, string Value)[] settings)
    {
        var derived = WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in settings) builder.UseSetting(key, value);
        });
        var client = derived.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        return new ConfiguredFactory(derived, client);
    }

    private readonly TestTimeProvider _clock = new();

    /// <summary>The instant the server currently sees. Equals <see cref="DateTimeOffset.UtcNow"/> unless frozen.</summary>
    public DateTimeOffset UtcNow => _clock.GetUtcNow();

    /// <summary>Pin the server clock to <paramref name="value"/> for a single test. Cleared by <see cref="ResetAsync"/>.</summary>
    public void FreezeTime(DateTimeOffset value) => _clock.Freeze(value);

    /// <summary>Return the server to the real system clock.</summary>
    public void UnfreezeTime() => _clock.UseSystemClock();

    public static string CreateTestConnection(string database) =>
        new Npgsql.NpgsqlConnectionStringBuilder(LocalPostgreSqlTestDatabase.LoadBaseConnection())
        {
            Database = database
        }.ConnectionString;

    public static void ValidateTestConfiguration(string environment, string? connection)
    {
        if (string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Module tests cannot run in Production.");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("Module tests require an explicit connection string.");

        LocalPostgreSqlTestDatabase.Validate(connection);
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
        builder.UseSetting("RateLimiting:LoginPermitLimit", "10000");
        builder.UseSetting("RateLimiting:LoginIdentifierPermitLimit", "10000");
        builder.UseSetting("RateLimiting:PermitLimit", "10000");
        builder.UseSetting("RateLimiting:CustomerIpPermitLimit", "10000");
        builder.UseSetting("RateLimiting:CustomerIdentifierPermitLimit", "10000");
        builder.UseSetting("RateLimiting:HandoffCreateIpPermitLimit", "10000");
        builder.UseSetting("RateLimiting:HandoffStatusPermitLimit", "10000");
        builder.UseSetting("RateLimiting:HandoffCancelIpPermitLimit", "10000");
        builder.UseSetting("RateLimiting:HandoffClaimIpPermitLimit", "10000");
        builder.UseSetting("RateLimiting:HandoffResolveIpPermitLimit", "10000");
        builder.UseSetting("RateLimiting:HandoffWindowSeconds", "60");
        builder.UseSetting("Scheduling:TimeZoneId", "America/Porto_Velho");
        builder.UseSetting("CheckIn:ManualCodeHmacKey", "integration-tests-manual-code-hmac-key-not-a-secret");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(ConnectionString));
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(_clock);
            // Added AFTER Program.cs ran Logging.ClearProviders(), so the capture survives and
            // observes everything the real pipeline logs. No filter change: app-category logs are
            // Information by default, which is exactly where an accidental code/hash leak would land.
            services.AddSingleton<ILoggerProvider>(_logSink);
        });
    }

    public async Task InitializeAsync()
    {
        ValidateTestConfiguration("Testing", ConnectionString);
        await LocalPostgreSqlTestDatabase.CreateSchemaAsync(_baseConnection, SchemaName);
        _schemaCreated = true;
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
        await ResetDatabaseAsync(db);
        await EnsureRolesAsync(scope.ServiceProvider);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        Client?.Dispose();
        if (_schemaCreated)
            await LocalPostgreSqlTestDatabase.DropSchemaAsync(_baseConnection, SchemaName);

        DeletePrivateRootSafely();
        await base.DisposeAsync();
    }

    public async Task ResetAsync()
    {
        UnfreezeTime();
        await using var scope = Services.CreateAsyncScope();
        await ResetDatabaseAsync(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        await EnsureRolesAsync(scope.ServiceProvider);
        ClearStoredTestFiles();
        Client.Dispose();
        Client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    public async Task SeedDefaultOperatingHoursAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (await db.OperatingHoursSchedules.AnyAsync()) return;
        var schedule = OperatingHoursSchedule.Create(DateTimeOffset.UtcNow);
        db.OperatingHoursSchedules.Add(schedule);
        foreach (var day in Enum.GetValues<DayOfWeek>())
            db.OperatingHourIntervals.AddRange(OperatingHourInterval.CreateDay(schedule.Id, day,
                [new LocalTimeRange(TimeOnly.MinValue, new TimeOnly(23, 59, 59))]));
        await db.SaveChangesAsync();
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
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"ProfessionalAvailabilityExceptions\"");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"ProfessionalAvailabilityIntervals\"");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"OperatingHourIntervals\"");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"OperatingHoursSchedules\"");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"RoomBlocks\"");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"FinancialCharges\"");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"CheckInTokens\"");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"RescheduleTokens\"");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"ProfessionalPresenceTokens\"");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"ProfessionalPresence\"");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"VisitTransitions\"");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"Visits\"");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"TotemBookingHandoffs\"");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"Reservations\"");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"Customers\"");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"ProfessionalRegistrationRequests\"");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"LeaseOccurrences\"");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"Leases\"");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"Tenants\"");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"Professionals\"");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"Rooms\"");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"PrivateFiles\"");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"AuditEntries\"");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"AspNetUserRoles\"");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"AspNetUsers\"");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"AspNetRoles\"");
    }

    private static async Task EnsureRolesAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in SystemRoles.AuthenticationRoles)
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

    private void ClearStoredTestFiles()
    {
        var root = Path.GetFullPath(PrivateFilesRoot);
        var safeBase = Path.GetFullPath(StorageBase) + Path.DirectorySeparatorChar;
        if (!root.StartsWith(safeBase, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to clear private files outside the test root.");

        foreach (var child in new[] { ".staging", "files" })
        {
            var directory = Path.GetFullPath(Path.Combine(root, child));
            if (!directory.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to clear an invalid private-files subdirectory.");
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            Directory.CreateDirectory(directory);
        }
    }

    private sealed record CsrfPayload(string Token);
}

/// <summary>
/// Minimal in-memory <see cref="ILoggerProvider"/> for no-leak assertions (spec 7A.10). Thread-safe;
/// every formatted message (and scope state) is appended to one shared buffer. Built here rather than
/// reused from another test file so the Modules suite owns its own collector (Task 16 RULING 1).
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly StringBuilder _sink = new();
    private readonly object _gate = new();

    /// <summary>Current length of the shared buffer — the anchor a <see cref="LogCapture"/> remembers.</summary>
    public int Length
    {
        get { lock (_gate) return _sink.Length; }
    }

    /// <summary>Everything appended at or after <paramref name="fromOffset"/>.</summary>
    public string Read(int fromOffset)
    {
        lock (_gate)
            return fromOffset >= _sink.Length ? string.Empty : _sink.ToString(fromOffset, _sink.Length - fromOffset);
    }

    public ILogger CreateLogger(string categoryName) => new SinkLogger(categoryName, this);

    public void Dispose() { }

    private void Append(string text)
    {
        lock (_gate) _sink.AppendLine(text);
    }

    private sealed class SinkLogger(string category, CapturingLoggerProvider owner) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            owner.Append($"scope {category} {state}");
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            owner.Append($"[{logLevel}] {category} {formatter(state, exception)} {exception}");

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}

/// <summary>An offset-anchored view over <see cref="CapturingLoggerProvider"/>: text logged since it was taken.</summary>
public sealed class LogCapture(CapturingLoggerProvider provider, int fromOffset)
{
    public string Text => provider.Read(fromOffset);
}

/// <summary>A derived <see cref="WebApplicationFactory{T}"/> plus its client; disposing releases both.</summary>
public sealed class ConfiguredFactory : IDisposable
{
    private readonly WebApplicationFactory<recepcaototem.Pages.IndexModel> _factory;

    internal ConfiguredFactory(WebApplicationFactory<recepcaototem.Pages.IndexModel> factory, HttpClient client)
    {
        _factory = factory;
        Client = client;
    }

    public HttpClient Client { get; }

    public void Dispose()
    {
        Client.Dispose();
        _factory.Dispose();
    }
}

using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Identity;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class UserPasswordResetTests(ModulesApiFactory factory)
{
    private const string AdminPassword = "Admin-Password-123!";
    private const string OldPassword = "Old-Password-123!";

    private static string ResetPath(string userId) => $"/api/admin/users/{userId}/reset-password";

    private async Task LoginAdminAsync(string email = "reset-admin@lumis.test")
    {
        await factory.CreateUserAsync(email, AdminPassword, [SystemRoles.Administrador]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(email, AdminPassword)).StatusCode);
    }

    private Task<ApplicationUser> CreateTargetAsync(
        string email = "reset-target@lumis.test",
        IReadOnlyCollection<string>? roles = null,
        bool mustChangePassword = false)
        => factory.CreateUserAsync(email, OldPassword, roles ?? [SystemRoles.Profissional],
            mustChangePassword: mustChangePassword, displayName: "Alvo do Reset");

    private sealed record ResetPayload(string UserId, string TemporaryPassword);

    [Fact]
    public async Task Administrator_resets_password_and_receives_a_one_time_temporary_password()
    {
        await factory.ResetAsync();
        var target = await CreateTargetAsync();
        await LoginAdminAsync();

        var response = await factory.PostWithCsrfAsync(ResetPath(target.Id), new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<ResetPayload>())!;
        Assert.Equal(target.Id, body.UserId);
        Assert.False(string.IsNullOrWhiteSpace(body.TemporaryPassword));
    }

    [Fact]
    public async Task Reset_for_an_unknown_user_returns_404()
    {
        await factory.ResetAsync();
        await LoginAdminAsync();
        var response = await factory.PostWithCsrfAsync(ResetPath(Guid.NewGuid().ToString()), new { });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_cannot_reset_a_password()
    {
        await factory.ResetAsync();
        var target = await CreateTargetAsync();
        var response = await factory.PostWithCsrfAsync(ResetPath(target.Id), new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(SystemRoles.Gerente)]
    [InlineData(SystemRoles.Profissional)]
    public async Task A_user_without_the_administration_policy_is_forbidden(string role)
    {
        await factory.ResetAsync();
        var target = await CreateTargetAsync("reset-victim@lumis.test");
        await factory.CreateUserAsync("reset-actor@lumis.test", AdminPassword, [role]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync("reset-actor@lumis.test", AdminPassword)).StatusCode);

        var response = await factory.PostWithCsrfAsync(ResetPath(target.Id), new { });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Reset_without_the_antiforgery_header_is_rejected()
    {
        await factory.ResetAsync();
        var target = await CreateTargetAsync();
        await LoginAdminAsync();

        var response = await factory.Client.PostAsJsonAsync(ResetPath(target.Id), new { });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_old_password_stops_working_and_the_new_temporary_password_works()
    {
        await factory.ResetAsync();
        var target = await CreateTargetAsync();
        await LoginAdminAsync();
        var body = (await (await factory.PostWithCsrfAsync(ResetPath(target.Id), new { }))
            .Content.ReadFromJsonAsync<ResetPayload>())!;

        // /api/auth/login has no "already signed in" guard; each call re-authenticates on the
        // shared client. The old password must be rejected, the fresh temporary one accepted.
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.LoginAsync(target.Email!, OldPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(target.Email!, body.TemporaryPassword)).StatusCode);
    }

    [Fact]
    public async Task Reset_forces_a_password_change_rotates_the_security_stamp_and_preserves_the_account()
    {
        await factory.ResetAsync();
        var target = await CreateTargetAsync(roles: [SystemRoles.Profissional], mustChangePassword: false);

        var professional = Professional.Create("Alvo Vinculado", "Fisio", "65999990007", factory.UtcNow);
        professional.LinkUser(target.Id, factory.UtcNow);
        string stampBefore;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Professionals.Add(professional);
            await db.SaveChangesAsync();
            stampBefore = (await db.Users.AsNoTracking().SingleAsync(x => x.Id == target.Id)).SecurityStamp!;
        }

        await LoginAdminAsync();
        Assert.Equal(HttpStatusCode.OK, (await factory.PostWithCsrfAsync(ResetPath(target.Id), new { })).StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var stored = await db.Users.AsNoTracking().SingleAsync(x => x.Id == target.Id);

            Assert.True(stored.MustChangePassword);
            Assert.NotEqual(stampBefore, stored.SecurityStamp);
            Assert.True(stored.EmailConfirmed); // preserved (CreateUserAsync sets it true)
            Assert.Equal([SystemRoles.Profissional], await users.GetRolesAsync(stored));

            var linkedProfessional = await db.Professionals.AsNoTracking().SingleAsync(x => x.Id == professional.Id);
            Assert.Equal(target.Id, linkedProfessional.ApplicationUserId);
            Assert.Equal("Alvo Vinculado", linkedProfessional.Name);
        }
    }

    [Fact]
    public async Task The_temporary_password_is_never_written_to_the_audit_trail()
    {
        await factory.ResetAsync();
        var target = await CreateTargetAsync();
        await LoginAdminAsync();
        var body = (await (await factory.PostWithCsrfAsync(ResetPath(target.Id), new { }))
            .Content.ReadFromJsonAsync<ResetPayload>())!;

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var audits = await db.AuditEntries.AsNoTracking().ToListAsync();

        Assert.Contains(audits, a => a.Action == "USER_PASSWORD_RESET" && a.Result == "SUCCEEDED" && a.TargetUserId == target.Id);
        var serialized = System.Text.Json.JsonSerializer.Serialize(audits);
        Assert.DoesNotContain(body.TemporaryPassword, serialized);
    }

    [Fact]
    public async Task The_temporary_password_is_never_written_to_the_logs()
    {
        await factory.ResetAsync();
        var capture = new CapturingLoggerProvider();
        // Register via DI after Program.cs has already run its ClearProviders(), so the capture
        // survives and observes everything the real pipeline logs during the reset.
        await using var child = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<ILoggerProvider>(capture);
                services.Configure<LoggerFilterOptions>(options => options.MinLevel = LogLevel.Trace);
            }));
        using var client = child.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var target = await CreateTargetAsync("reset-log-target@lumis.test");
        await factory.CreateUserAsync("reset-log-admin@lumis.test", AdminPassword, [SystemRoles.Administrador]);

        async Task<string> CsrfAsync() =>
            (await (await client.GetAsync("/api/auth/csrf")).Content.ReadFromJsonAsync<TokenPayload>())!.Token;

        using (var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
               { Content = JsonContent.Create(new { email = "reset-log-admin@lumis.test", password = AdminPassword }) })
        {
            login.Headers.Add("X-CSRF-TOKEN", await CsrfAsync());
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(login)).StatusCode);
        }

        string temporaryPassword;
        // A fresh token bound to the now-authenticated identity.
        using (var request = new HttpRequestMessage(HttpMethod.Post, ResetPath(target.Id)) { Content = JsonContent.Create(new { }) })
        {
            request.Headers.Add("X-CSRF-TOKEN", await CsrfAsync());
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            temporaryPassword = (await response.Content.ReadFromJsonAsync<ResetPayload>())!.TemporaryPassword;
        }

        Assert.NotEmpty(capture.Lines); // the pipeline did log something, so the check is meaningful
        Assert.DoesNotContain(capture.Lines, line => line.Contains(temporaryPassword));
    }

    private sealed record TokenPayload(string Token);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _lines = [];
        public IReadOnlyList<string> Lines { get { lock (_lines) return _lines.ToArray(); } }
        public ILogger CreateLogger(string categoryName) => new ListLogger(_lines);
        public void Dispose() { }

        private sealed class ListLogger(List<string> lines) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull
            {
                lock (lines) lines.Add(state?.ToString() ?? "");
                return NullScope.Instance;
            }
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (lines) lines.Add($"{formatter(state, exception)} {exception}");
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}

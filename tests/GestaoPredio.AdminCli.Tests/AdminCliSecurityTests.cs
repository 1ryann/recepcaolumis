using GestaoPredio.AdminCli;
using GestaoPredio.Domain.Security;
using GestaoPredio.Infrastructure.Identity;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.AdminCli.Tests;

public sealed class AdminCliSecurityTests
{
    [Theory]
    [InlineData()]
    [InlineData("bootstrap-admin", "password-on-command-line")]
    [InlineData("other-command")]
    public void Parser_rejects_everything_except_single_bootstrap_command(params string[] args)
    {
        Assert.False(BootstrapCommandParser.IsBootstrapAdmin(args));
    }

    [Fact]
    public void Parser_accepts_single_bootstrap_command()
    {
        Assert.True(BootstrapCommandParser.IsBootstrapAdmin(["bootstrap-admin"]));
    }

    [Fact]
    public void Hidden_reader_requests_intercept_and_does_not_echo_characters()
    {
        var console = new RecordingConsole([
            new ConsoleKeyInfo('S', ConsoleKey.S, false, false, false),
            new ConsoleKeyInfo('e', ConsoleKey.E, false, false, false),
            new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)
        ]);
        var password = new HiddenPasswordReader(console).Read();
        Assert.Equal("Se", password);
        Assert.All(console.InterceptValues, Assert.True);
        Assert.DoesNotContain("Se", console.Output);
    }

    [Fact]
    public void Production_connection_requires_integrated_security()
    {
        ConnectionStringGuard.Validate(
            "Server=localhost\\SQLEXPRESS;Database=GestaoPredioAuthTests;Integrated Security=True;Encrypt=True;TrustServerCertificate=True",
            "Production");
        Assert.Throws<InvalidOperationException>(() => ConnectionStringGuard.Validate(
            "Server=localhost\\SQLEXPRESS;Database=GestaoPredioAuthTests;User Id=user;Password=secret",
            "Production"));
    }

    [Fact]
    public async Task Bootstrap_creates_roles_admin_and_audit_once()
    {
        const string connection = "Server=localhost\\SQLEXPRESS;Database=GestaoPredioAuthTestsCli;Integrated Security=True;Encrypt=True;TrustServerCertificate=True";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connection));
        services.AddIdentityCore<ApplicationUser>(LumisIdentityOptions.Configure)
            .AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        services.AddSingleton(TimeProvider.System);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [AuditEntries]; DELETE FROM [AspNetUserRoles]; DELETE FROM [AspNetUsers]; DELETE FROM [AspNetRoles]");
        var bootstrapper = ActivatorUtilities.CreateInstance<AdminBootstrapper>(scope.ServiceProvider);

        Assert.Equal(BootstrapOutcome.Created,
            await bootstrapper.BootstrapAsync("First Admin", "first.admin@lumis.test", "Valid-Password-123!", default));
        Assert.Equal(BootstrapOutcome.AlreadyProvisioned,
            await bootstrapper.BootstrapAsync("Second Admin", "second.admin@lumis.test", "Valid-Password-456!", default));

        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in SystemRoles.All) Assert.True(await roles.RoleExistsAsync(role));
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admins = await users.GetUsersInRoleAsync(SystemRoles.Administrador);
        Assert.Single(admins);
        Assert.False(admins[0].MustChangePassword);
        Assert.Single(await db.AuditEntries.Where(x => x.Action == "BOOTSTRAP_ADMIN_CREATED").ToListAsync());
    }

    private sealed class RecordingConsole(IEnumerable<ConsoleKeyInfo> keys) : ISecretConsole
    {
        private readonly Queue<ConsoleKeyInfo> _keys = new(keys);
        public List<bool> InterceptValues { get; } = [];
        public string Output { get; private set; } = "";
        public ConsoleKeyInfo ReadKey(bool intercept)
        {
            InterceptValues.Add(intercept);
            return _keys.Dequeue();
        }
        public void WriteLine() => Output += Environment.NewLine;
    }
}

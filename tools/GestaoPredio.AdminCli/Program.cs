using GestaoPredio.AdminCli;
using GestaoPredio.Infrastructure.Identity;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

if (!BootstrapCommandParser.IsBootstrapAdmin(args) && !BootstrapCommandParser.IsProvisionRoles(args))
{
    Console.Error.WriteLine("Uso: GestaoPredio.AdminCli bootstrap-admin | provision-roles");
    return 2;
}

var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection") ?? "";
var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
try { ConnectionStringGuard.Validate(connection, environment); }
catch (InvalidOperationException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 3;
}

var services = new ServiceCollection();
services.AddLogging();
services.AddSingleton(TimeProvider.System);
services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connection));
services.AddIdentityCore<ApplicationUser>(LumisIdentityOptions.Configure)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var bootstrapper = ActivatorUtilities.CreateInstance<AdminBootstrapper>(scope.ServiceProvider);
if (BootstrapCommandParser.IsProvisionRoles(args))
{
    var provisioned = await bootstrapper.ProvisionRolesAsync(CancellationToken.None);
    Console.WriteLine(provisioned ? "Roles de autenticação provisionadas." : "Não foi possível provisionar as roles.");
    return provisioned ? 0 : 6;
}
Console.Write("Nome de exibição: ");
var displayName = Console.ReadLine() ?? "";
Console.Write("E-mail: ");
var email = Console.ReadLine() ?? "";
var passwordReader = new HiddenPasswordReader(new SystemSecretConsole());
Console.Write("Senha: ");
var password = passwordReader.Read();
Console.Write("Confirme a senha: ");
var confirmation = passwordReader.Read();
if (!string.Equals(password, confirmation, StringComparison.Ordinal))
{
    Console.Error.WriteLine("As senhas não conferem.");
    return 4;
}
var outcome = await bootstrapper.BootstrapAsync(displayName, email, password, CancellationToken.None);
Console.WriteLine(outcome switch
{
    BootstrapOutcome.Created => "Administrador inicial criado.",
    BootstrapOutcome.AlreadyProvisioned => "O administrador inicial já foi provisionado.",
    BootstrapOutcome.InvalidInput => "Não foi possível criar o administrador com os dados informados.",
    _ => "Não foi possível concluir o bootstrap."
});
return outcome == BootstrapOutcome.Created ? 0 : 5;

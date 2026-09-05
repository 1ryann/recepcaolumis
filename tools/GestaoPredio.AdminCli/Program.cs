using GestaoPredio.AdminCli;
using GestaoPredio.Infrastructure.Identity;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

if (!BootstrapCommandParser.IsBootstrapAdmin(args))
{
    Console.Error.WriteLine("Uso: GestaoPredio.AdminCli bootstrap-admin");
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

var services = new ServiceCollection();
services.AddLogging();
services.AddSingleton(TimeProvider.System);
services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connection));
services.AddIdentityCore<ApplicationUser>(LumisIdentityOptions.Configure)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var bootstrapper = ActivatorUtilities.CreateInstance<AdminBootstrapper>(scope.ServiceProvider);
var outcome = await bootstrapper.BootstrapAsync(displayName, email, password, CancellationToken.None);
Console.WriteLine(outcome switch
{
    BootstrapOutcome.Created => "Administrador inicial criado.",
    BootstrapOutcome.AlreadyProvisioned => "O administrador inicial já foi provisionado.",
    BootstrapOutcome.InvalidInput => "Não foi possível criar o administrador com os dados informados.",
    _ => "Não foi possível concluir o bootstrap."
});
return outcome == BootstrapOutcome.Created ? 0 : 5;

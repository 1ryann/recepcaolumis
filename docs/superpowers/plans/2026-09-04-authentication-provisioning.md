# Authentication and Secure Provisioning Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Entregar autenticação web real, criação controlada de usuários e bootstrap único do primeiro administrador, com React e API na mesma origem HTTPS.

**Architecture:** ASP.NET Core Identity emite cookie `__Host-Lumis.Auth`; endpoints mínimos concentram login, sessão, logout, troca de senha e criação administrativa. Uma CLI separada provisiona roles e o primeiro Admin, enquanto a SPA Vite é incorporada ao publish da API e mantém sessão/CSRF somente em memória.

**Tech Stack:** .NET 10.0.400, ASP.NET Core Identity 10.0.11, EF Core SQL Server 10.0.11, React, TypeScript, Vite, xUnit e `Microsoft.AspNetCore.Mvc.Testing`.

**Spec:** `docs/superpowers/specs/2026-09-04-authentication-provisioning-design.md`

## Global Constraints

- SPA, API e health checks usam a mesma origem HTTPS; CORS amplo é proibido.
- Production recusa autenticação por HTTP; somente `/health` e `/health/ready` permanecem disponíveis no binding HTTP interno.
- Cookie: `__Host-Lumis.Auth`, HttpOnly, Secure, SameSite=Lax, Path=/ e sem Domain.
- Antiforgery: cookie `__Host-Lumis.Csrf` e request token no header `X-CSRF-TOKEN`.
- Roles exatos: `ADMINISTRADOR`, `GERENTE` e `PROFISSIONAL`.
- Nenhum cadastro público, JWT, senha padrão, senha em Git, argumento CLI, variável de ambiente ou log.
- `GestaoPredio.AdminCli` é publicado separadamente e nunca entra no diretório IIS.
- Não confiar em `X-Forwarded-For` sem proxies confiáveis configurados.
- Não executar `Database.Migrate()`, `EnsureCreated()` ou criação automática de roles/usuários.
- Não aplicar migration, modificar IIS, configurar HTTPS/Cloudflare ou acessar produção durante a implementação local.
- Cada tarefa segue red-green-refactor e termina com revisão do diff e commit focado.

---

## Mapa de arquivos

**Criar:**

- `recepcaototem/Features/Auth/AuthContracts.cs` — DTOs públicos da autenticação.
- `recepcaototem/Features/Auth/AuthEndpoints.cs` — csrf, login, sessão, logout e troca de senha.
- `recepcaototem/Features/Auth/AuthAuditService.cs` — eventos mínimos de autenticação.
- `recepcaototem/Features/Auth/CurrentUserValidation.cs` — validação de ativo/troca obrigatória.
- `recepcaototem/Features/Auth/TemporaryPasswordGenerator.cs` — senha temporária criptográfica.
- `recepcaototem/Features/Users/UserAdministrationEndpoints.cs` — criação administrativa.
- `recepcaototem/Api/Configuration/IdentityConfiguration.cs` — opções compartilhadas de Identity/cookie.
- `src/GestaoPredio.Infrastructure/Persistence/Migrations/*_AuthenticationAndProvisioning.cs` e designer — migration aditiva.
- `tools/GestaoPredio.AdminCli/GestaoPredio.AdminCli.csproj` — ferramenta separada.
- `tools/GestaoPredio.AdminCli/Program.cs` — composição e comando `bootstrap-admin`.
- `tools/GestaoPredio.AdminCli/HiddenPasswordReader.cs` — leitura sem eco.
- `tools/GestaoPredio.AdminCli/AdminBootstrapper.cs` — transação, roles, Admin e auditoria.
- `tests/GestaoPredio.IntegrationTests/AuthApiFactory.cs` — host e banco SQL Server exclusivo de teste.
- `tests/GestaoPredio.IntegrationTests/AuthenticationTests.cs`.
- `tests/GestaoPredio.IntegrationTests/AntiforgeryTests.cs`.
- `tests/GestaoPredio.IntegrationTests/UserAdministrationTests.cs`.
- `tests/GestaoPredio.IntegrationTests/MigrationSafetyTests.cs`.
- `tests/GestaoPredio.IntegrationTests/PublishContentsTests.cs`.
- `tests/GestaoPredio.AdminCli.Tests/GestaoPredio.AdminCli.Tests.csproj`.
- `tests/GestaoPredio.AdminCli.Tests/AdminBootstrapperTests.cs`.
- `recepcaototem/ClientApp/src/api/client.ts` — fetch same-origin e token CSRF em memória.
- `recepcaototem/ClientApp/src/auth/SessionProvider.tsx` — estado da sessão real.
- `recepcaototem/ClientApp/src/auth/RequireSession.tsx` — guarda de rota.
- `recepcaototem/ClientApp/package.json` e `package-lock.json` — script e dependências fixadas dos testes React.
- `docs/operations/authentication-deployment.md` — publicação, migration e bootstrap.

**Modificar:**

- `src/GestaoPredio.Infrastructure/Identity/ApplicationUser.cs`.
- `src/GestaoPredio.Domain/Auditing/AuditEntry.cs`.
- `src/GestaoPredio.Infrastructure/Persistence/ApplicationDbContext.cs`.
- `recepcaototem/Program.cs`.
- `recepcaototem/recepcaototem.csproj`.
- `recepcaototem/appsettings.json`.
- `recepcaototem/ClientApp/src/App.tsx`.
- `recepcaototem/ClientApp/src/pages/Login.tsx`.
- `recepcaototem/ClientApp/src/components/ProtectedRoute.tsx`.
- `recepcaototem/ClientApp/src/components/AdminLayout.tsx`.
- `recepcaototem/ClientApp/src/styles.css` apenas para estados de carregamento/erro/troca de senha no design atual.
- `recepcaototem.sln` para incluir CLI e testes da CLI, sem criar referência da API para a CLI.

---

### Task 1: Modelo Identity, auditoria e migration aditiva

**Files:**

- Modify: `src/GestaoPredio.Infrastructure/Identity/ApplicationUser.cs`
- Modify: `src/GestaoPredio.Domain/Auditing/AuditEntry.cs`
- Modify: `src/GestaoPredio.Infrastructure/Persistence/ApplicationDbContext.cs`
- Create: `src/GestaoPredio.Infrastructure/Persistence/Migrations/*_AuthenticationAndProvisioning.cs`
- Test: `tests/GestaoPredio.IntegrationTests/MigrationSafetyTests.cs`

**Interfaces:**

- Produces: `ApplicationUser.DisplayName`, `IsActive`, `MustChangePassword`.
- Produces: `AuditEntry.TargetUserId`, `IpAddress`.
- Produces: migration `AuthenticationAndProvisioning`, sem alteração destrutiva.

- [ ] **Step 1: escrever teste de modelo que falha**

```csharp
[Fact]
public void Auth_model_has_required_additive_columns()
{
    using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
    var user = db.Model.FindEntityType(typeof(ApplicationUser))!;
    Assert.Equal(200, user.FindProperty(nameof(ApplicationUser.DisplayName))!.GetMaxLength());
    Assert.False(user.FindProperty(nameof(ApplicationUser.IsActive))!.IsNullable);
    Assert.False(user.FindProperty(nameof(ApplicationUser.MustChangePassword))!.IsNullable);
    var audit = db.Model.FindEntityType(typeof(AuditEntry))!;
    Assert.Equal(450, audit.FindProperty(nameof(AuditEntry.TargetUserId))!.GetMaxLength());
    Assert.Equal(45, audit.FindProperty(nameof(AuditEntry.IpAddress))!.GetMaxLength());
}
```

- [ ] **Step 2: executar o teste e confirmar falha por propriedades ausentes**

Run: `dotnet test tests/GestaoPredio.IntegrationTests/GestaoPredio.IntegrationTests.csproj -c Release --filter Auth_model_has_required_additive_columns`

Expected: FAIL porque as cinco propriedades ainda não existem.

- [ ] **Step 3: adicionar as propriedades e configurações mínimas**

```csharp
public sealed class ApplicationUser : IdentityUser
{
    public required string DisplayName { get; set; }
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }
}
```

Configurar comprimentos, defaults SQL e índice `(Action, OccurredAt)` no `ApplicationDbContext`. Não criar FKs de auditoria para Identity.

- [ ] **Step 4: executar o teste e confirmar aprovação**

Run: `dotnet test tests/GestaoPredio.IntegrationTests/GestaoPredio.IntegrationTests.csproj -c Release --filter Auth_model_has_required_additive_columns`

Expected: PASS.

- [ ] **Step 5: gerar a migration sem aplicar banco**

```powershell
dotnet ef migrations add AuthenticationAndProvisioning `
  --project .\src\GestaoPredio.Infrastructure\GestaoPredio.Infrastructure.csproj `
  --startup-project .\recepcaototem\recepcaototem.csproj `
  --output-dir Persistence\Migrations
```

- [ ] **Step 6: gerar SQL idempotente e específico sem conectar a produção**

```powershell
dotnet ef migrations script --idempotent `
  --project .\src\GestaoPredio.Infrastructure\GestaoPredio.Infrastructure.csproj `
  --startup-project .\recepcaototem\recepcaototem.csproj `
  --output .\artifacts\migrations-auth-idempotent.sql

dotnet ef migrations script 20260904174759_InfrastructureFoundation AuthenticationAndProvisioning --idempotent `
  --project .\src\GestaoPredio.Infrastructure\GestaoPredio.Infrastructure.csproj `
  --startup-project .\recepcaototem\recepcaototem.csproj `
  --output .\artifacts\AuthenticationAndProvisioning.sql
```

- [ ] **Step 7: testar segurança do SQL gerado**

```csharp
[Fact]
public void Forward_auth_migration_is_additive()
{
    var sql = File.ReadAllText(Artifact("AuthenticationAndProvisioning.sql"));
    Assert.DoesNotContain("DROP ", sql, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("TRUNCATE ", sql, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("ALTER TABLE [AspNetUsers]", sql);
    Assert.Contains("ALTER TABLE [AuditEntries]", sql);
}
```

- [ ] **Step 8: revisar migration, snapshot e SQL linha por linha e confirmar somente as alterações da spec**

- [ ] **Step 9: executar os testes da solução e commitar**

```powershell
dotnet test recepcaototem.sln -c Release
git add src/GestaoPredio.Infrastructure src/GestaoPredio.Domain tests/GestaoPredio.IntegrationTests/MigrationSafetyTests.cs
git commit -m "feat: extend identity and audit schema"
```

---

### Task 2: Configuração compartilhada de Identity, cookie e policies

**Files:**

- Create: `recepcaototem/Api/Configuration/IdentityConfiguration.cs`
- Create: `recepcaototem/Features/Auth/CurrentUserValidation.cs`
- Modify: `recepcaototem/Program.cs`
- Test: `tests/GestaoPredio.IntegrationTests/AuthenticationTests.cs`

**Interfaces:**

- Produces: `AddLumisIdentity(IServiceCollection, IConfiguration)`.
- Produces: policies `PasswordChanged`, `Administration`, `Operations`, `Professional`.
- Produces: `ActiveUserCookieValidator` para revalidar `IsActive` e security stamp.

- [ ] **Step 1: escrever testes que inspecionam cookie e policies**

```csharp
[Fact]
public void Auth_cookie_uses_approved_security_attributes()
{
    var options = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
        .Get(IdentityConstants.ApplicationScheme);
    Assert.Equal("__Host-Lumis.Auth", options.Cookie.Name);
    Assert.True(options.Cookie.HttpOnly);
    Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
    Assert.Equal(SameSiteMode.Lax, options.Cookie.SameSite);
    Assert.Equal("/", options.Cookie.Path);
    Assert.Null(options.Cookie.Domain);
}
```

- [ ] **Step 2: executar e observar falha da policy `PasswordChanged`/validação ativa ainda inexistente**

- [ ] **Step 3: extrair opções já existentes de `Program.cs` para `IdentityConfiguration` e adicionar claims `is_active` e `must_change_password` no principal**

- [ ] **Step 4: configurar `PasswordChanged` para exigir usuário ativo e `must_change_password=false`; fazer as três policies de role também exigirem `PasswordChanged`**

- [ ] **Step 5: adicionar validação periódica de cookie que rejeita conta inativa ou security stamp inválido**

- [ ] **Step 6: executar testes focados e todos os 16 testes legados**

Run: `dotnet test tests/GestaoPredio.IntegrationTests/GestaoPredio.IntegrationTests.csproj -c Release --filter "AuthenticationTests|SecurityTests|FoundationTests"`

- [ ] **Step 7: revisar diff e commitar**

```powershell
git add recepcaototem/Api/Configuration recepcaototem/Features/Auth/CurrentUserValidation.cs recepcaototem/Program.cs tests/GestaoPredio.IntegrationTests/AuthenticationTests.cs
git commit -m "feat: configure secure identity sessions"
```

---

### Task 3: Infraestrutura de teste SQL Server exclusiva

**Files:**

- Create: `tests/GestaoPredio.IntegrationTests/AuthApiFactory.cs`
- Modify: `tests/GestaoPredio.IntegrationTests/GestaoPredio.IntegrationTests.csproj`

**Interfaces:**

- Consumes: `ConnectionStrings__AuthTests` ou `ConnectionStrings:AuthTests`.
- Produces: `AuthApiFactory.CreateUserAsync(...)`, `CreateCsrfClientAsync()` e limpeza isolada por coleção.

- [ ] **Step 1: escrever teste que rejeita banco de produção**

```csharp
[Theory]
[InlineData("Server=.;Database=GestaoPredioDB;Integrated Security=true")]
[InlineData("Server=.;Initial Catalog=GestaoPredioDB;Integrated Security=true")]
public void Factory_rejects_production_database(string connection)
{
    Assert.Throws<InvalidOperationException>(() => AuthApiFactory.ValidateTestConnection(connection));
}
```

- [ ] **Step 2: implementar validação que exige catálogo diferente de `GestaoPredioDB` e ambiente `Testing`**

- [ ] **Step 3: criar factory que aplica migrations somente no banco exclusivo de testes e remove dados entre casos**

- [ ] **Step 4: executar smoke test de Identity no banco de testes; se a connection string não estiver configurada, falhar com instrução explícita em vez de usar produção ou InMemory**

- [ ] **Step 5: commitar a infraestrutura de testes**

```powershell
git add tests/GestaoPredio.IntegrationTests
git commit -m "test: add isolated SQL Server auth fixture"
```

---

### Task 4: Antiforgery same-origin

**Files:**

- Create: `recepcaototem/Features/Auth/AuthContracts.cs`
- Create: `recepcaototem/Features/Auth/AuthEndpoints.cs`
- Modify: `recepcaototem/Program.cs`
- Test: `tests/GestaoPredio.IntegrationTests/AntiforgeryTests.cs`

**Interfaces:**

- Produces: `GET /api/auth/csrf` → `{ token }` e cookie `__Host-Lumis.Csrf`.
- Produces: `MapAuthEndpoints(IEndpointRouteBuilder)`.

- [ ] **Step 1: escrever teste para emissão de token e atributos do cookie**

```csharp
[Fact]
public async Task Csrf_endpoint_returns_request_token_and_secure_cookie()
{
    var response = await Client.GetAsync("/api/auth/csrf");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Contains(response.Headers.GetValues("Set-Cookie"), value =>
        value.Contains("__Host-Lumis.Csrf=") && value.Contains("Secure") && value.Contains("HttpOnly") && value.Contains("SameSite=Strict"));
}
```

- [ ] **Step 2: escrever teste onde POST sem `X-CSRF-TOKEN` retorna 400 sem detalhes internos**

- [ ] **Step 3: mapear endpoint CSRF usando `IAntiforgery.GetAndStoreTokens` e exigir antiforgery em todos os POSTs desta feature**

- [ ] **Step 4: confirmar que token/cookie não entram em logs ou auditoria capturados pelo teste**

- [ ] **Step 5: executar testes focados e commitar**

```powershell
dotnet test tests/GestaoPredio.IntegrationTests/GestaoPredio.IntegrationTests.csproj -c Release --filter AntiforgeryTests
git add recepcaototem/Features/Auth recepcaototem/Program.cs tests/GestaoPredio.IntegrationTests/AntiforgeryTests.cs
git commit -m "feat: add same-origin antiforgery flow"
```

---

### Task 5: Auditoria de autenticação

**Files:**

- Create: `recepcaototem/Features/Auth/AuthAuditService.cs`
- Modify: `src/GestaoPredio.Application/Abstractions/IAuditWriter.cs` somente se necessário para compartilhar transação sem `SaveChanges` independente
- Modify: `src/GestaoPredio.Infrastructure/Auditing/EfAuditWriter.cs`
- Test: `tests/GestaoPredio.IntegrationTests/AuthenticationTests.cs`

**Interfaces:**

- Produces: `AuthAuditService.WriteAsync(string action, string result, string? actorUserId, string? targetUserId, HttpContext, CancellationToken)`.

- [ ] **Step 1: escrever teste de auditoria falha que usa IP da conexão e não contém e-mail/senha/cookie/token**

```csharp
[Fact]
public async Task Failed_login_audit_contains_no_credentials()
{
    await LoginAsync("missing@example.invalid", "Secret-value-123!");
    var audit = await Db.AuditEntries.SingleAsync(x => x.Action == "LOGIN_FAILED");
    Assert.Null(audit.ActorUserId);
    Assert.Null(audit.TargetUserId);
    Assert.DoesNotContain("missing@example.invalid", Serialize(audit));
    Assert.DoesNotContain("Secret-value-123!", Serialize(audit));
}
```

- [ ] **Step 2: executar e confirmar falha por serviço/evento inexistente**

- [ ] **Step 3: implementar o serviço com `RemoteIpAddress`, UTC, correlation ID e campos mínimos**

- [ ] **Step 4: impedir que `EfAuditWriter` faça commit independente quando auditoria precisar participar da transação de criação de usuário/bootstrap**

- [ ] **Step 5: testar eventos e commitar**

```powershell
git add recepcaototem/Features/Auth/AuthAuditService.cs src/GestaoPredio.Application src/GestaoPredio.Infrastructure tests/GestaoPredio.IntegrationTests/AuthenticationTests.cs
git commit -m "feat: audit authentication events safely"
```

---

### Task 6: Login, sessão, logout, lockout e limiter específico

**Files:**

- Modify: `recepcaototem/Features/Auth/AuthContracts.cs`
- Modify: `recepcaototem/Features/Auth/AuthEndpoints.cs`
- Modify: `recepcaototem/Program.cs`
- Modify: `recepcaototem/appsettings.json`
- Test: `tests/GestaoPredio.IntegrationTests/AuthenticationTests.cs`

**Interfaces:**

- Produces: `POST /api/auth/login`, `GET /api/auth/session`, `POST /api/auth/logout`.
- Produces: named limiter `login` configurado por `RateLimiting:LoginPermitLimit` e `LoginWindowSeconds`.

- [ ] **Step 1: escrever testes para login válido e cookie seguro**

```csharp
[Fact]
public async Task Valid_login_issues_nonpersistent_host_cookie()
{
    await Factory.CreateUserAsync("admin@lumis.test", "Valid-Password-123!", SystemRoles.Administrador);
    var response = await LoginWithCsrfAsync("admin@lumis.test", "Valid-Password-123!");
    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    var cookie = response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Host-Lumis.Auth="));
    Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("expires=", cookie, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: escrever testes equivalentes para inexistente, senha inválida, inativo e bloqueado; todos devem retornar o mesmo status/body**

- [ ] **Step 3: escrever teste de cinco falhas, lockout de 15 minutos e posterior recusa da senha correta**

- [ ] **Step 4: escrever teste do limiter login por `RemoteIpAddress`; confirmar 429 e que `/health` continua 200**

- [ ] **Step 5: implementar login com `PasswordSignInAsync(..., isPersistent: false, lockoutOnFailure: true)` e verificação fictícia para usuário inexistente**

- [ ] **Step 6: mapear sessão mínima e logout com `SignOutAsync`; auditar sucesso/falha/rate limit/logout**

- [ ] **Step 7: configurar partição sem ler `X-Forwarded-For` e validar limites positivos no startup**

- [ ] **Step 8: executar suíte de autenticação e testes legados**

Run: `dotnet test tests/GestaoPredio.IntegrationTests/GestaoPredio.IntegrationTests.csproj -c Release --filter "AuthenticationTests|FoundationTests|SecurityTests"`

- [ ] **Step 9: commitar**

```powershell
git add recepcaototem/Features/Auth recepcaototem/Program.cs recepcaototem/appsettings.json tests/GestaoPredio.IntegrationTests/AuthenticationTests.cs
git commit -m "feat: add identity login session and logout"
```

---

### Task 7: Troca obrigatória de senha

**Files:**

- Modify: `recepcaototem/Features/Auth/AuthContracts.cs`
- Modify: `recepcaototem/Features/Auth/AuthEndpoints.cs`
- Modify: `recepcaototem/Features/Auth/CurrentUserValidation.cs`
- Test: `tests/GestaoPredio.IntegrationTests/AuthenticationTests.cs`

**Interfaces:**

- Produces: `POST /api/auth/change-password`.
- Produces: claim/session `mustChangePassword` e policy `PasswordChanged`.

- [ ] **Step 1: escrever teste onde usuário temporário acessa session/csrf/change-password/logout, mas recebe 403 em endpoint `Administration`**

- [ ] **Step 2: escrever teste de troca com senha atual inválida e teste de sucesso que limpa flag, atualiza security stamp e renova cookie**

- [ ] **Step 3: implementar troca transacional e auditoria `PASSWORD_CHANGED` sem registrar senhas**

- [ ] **Step 4: confirmar que sessão posterior retorna `mustChangePassword=false` e policy passa**

- [ ] **Step 5: executar testes e commitar**

```powershell
git add recepcaototem/Features/Auth tests/GestaoPredio.IntegrationTests/AuthenticationTests.cs
git commit -m "feat: require temporary password replacement"
```

---

### Task 8: Criação administrativa de usuários

**Files:**

- Create: `recepcaototem/Features/Auth/TemporaryPasswordGenerator.cs`
- Create: `recepcaototem/Features/Users/UserAdministrationEndpoints.cs`
- Modify: `recepcaototem/Program.cs`
- Test: `tests/GestaoPredio.IntegrationTests/UserAdministrationTests.cs`

**Interfaces:**

- Produces: `POST /api/admin/users`.
- Produces: `ITemporaryPasswordGenerator.Generate()`.
- Response: `{ userId, displayName, email, role, temporaryPassword }`, somente na criação.

- [ ] **Step 1: escrever testes 401 anônimo, 403 Gerente/Profissional e 201 Administrador**

- [ ] **Step 2: escrever teste que aceita somente um role da lista aprovada e rejeita propriedades inesperadas**

- [ ] **Step 3: escrever teste que a senha retornada autentica, não aparece no banco/auditoria/logs e não é recuperável por GET**

- [ ] **Step 4: implementar gerador com `RandomNumberGenerator.GetBytes`, garantindo maiúscula, minúscula, número, símbolo e comprimento compatível**

- [ ] **Step 5: implementar criação em transação: usuário ativo, flag obrigatória, role e auditoria; resposta 201 com `Cache-Control: no-store`**

- [ ] **Step 6: testar e-mail duplicado e rollback quando associação de role/auditoria falha**

- [ ] **Step 7: executar testes e commitar**

```powershell
git add recepcaototem/Features recepcaototem/Program.cs tests/GestaoPredio.IntegrationTests/UserAdministrationTests.cs
git commit -m "feat: let administrators create users securely"
```

---

### Task 9: CLI transacional de bootstrap

**Files:**

- Create: `tools/GestaoPredio.AdminCli/GestaoPredio.AdminCli.csproj`
- Create: `tools/GestaoPredio.AdminCli/Program.cs`
- Create: `tools/GestaoPredio.AdminCli/HiddenPasswordReader.cs`
- Create: `tools/GestaoPredio.AdminCli/AdminBootstrapper.cs`
- Create: `tests/GestaoPredio.AdminCli.Tests/GestaoPredio.AdminCli.Tests.csproj`
- Create: `tests/GestaoPredio.AdminCli.Tests/AdminBootstrapperTests.cs`
- Modify: `recepcaototem.sln`

**Interfaces:**

- Consumes: comando literal `bootstrap-admin`, configuração externa `ConnectionStrings__DefaultConnection` e ambiente.
- Produces: `AdminBootstrapper.BootstrapAsync(displayName, email, password, CancellationToken)`.

- [ ] **Step 1: escrever testes de parser: somente `bootstrap-admin` aceito e qualquer argumento adicional/senha recusado**

- [ ] **Step 2: escrever teste de password reader usando abstração de console; confirmar que caracteres não são ecoados nem armazenados após retorno**

- [ ] **Step 3: escrever teste de conexão Production que aceita Integrated Security e rejeita User ID/Password**

- [ ] **Step 4: escrever teste transacional que cria três roles, um Admin e auditoria; segunda execução recusa sem mutação**

- [ ] **Step 5: escrever teste que garante ausência de chamadas a `Migrate`, `EnsureCreated` ou qualquer DDL**

- [ ] **Step 6: implementar composição Identity usando as mesmas opções de senha da API, preferencialmente em extensão compartilhada de Infrastructure**

- [ ] **Step 7: implementar leitura oculta, confirmação, transação, roles e auditoria com mensagens sem connection string/senha**

- [ ] **Step 8: publicar API e CLI em destinos separados e provar ausência da CLI no pacote web**

```powershell
dotnet publish .\recepcaototem\recepcaototem.csproj -c Release -o .\artifacts\publish\LumisApi
dotnet publish .\tools\GestaoPredio.AdminCli\GestaoPredio.AdminCli.csproj -c Release -o .\artifacts\tools\GestaoPredio.AdminCli
```

- [ ] **Step 9: executar testes da CLI e commitar**

```powershell
dotnet test tests/GestaoPredio.AdminCli.Tests/GestaoPredio.AdminCli.Tests.csproj -c Release
git add tools tests/GestaoPredio.AdminCli.Tests recepcaototem.sln
git commit -m "feat: add one-time administrator bootstrap CLI"
```

---

### Task 10: Servir o build React pela API

**Files:**

- Modify: `recepcaototem/recepcaototem.csproj`
- Modify: `recepcaototem/Program.cs`
- Modify: `recepcaototem/ClientApp/vite.config.ts`
- Test: `tests/GestaoPredio.IntegrationTests/PublishContentsTests.cs`

**Interfaces:**

- Produces: SPA em `/`, API em `/api/*`, health em `/health*`.
- Produces: target MSBuild que executa `npm ci` e `npm run build` e inclui somente `ClientApp/dist`.

- [ ] **Step 1: escrever teste de publish que exige `wwwroot/index.html` e assets, e proíbe `GestaoPredio.AdminCli`, `ClientApp/src`, `.env` e source maps de produção**

- [ ] **Step 2: executar publish e confirmar falha porque o frontend está excluído atualmente**

- [ ] **Step 3: configurar Vite para saída determinística e csproj para restaurar/buildar o React no publish**

- [ ] **Step 4: adicionar `UseDefaultFiles`, `UseStaticFiles` e fallback SPA depois dos endpoints, excluindo `/api` e `/health`**

- [ ] **Step 5: ajustar CSP para scripts/styles/assets/connect same-origin, `img-src` necessário ao design e webcam, mantendo `object-src 'none'`, `base-uri 'self'`, `frame-ancestors 'none'`**

- [ ] **Step 6: gerar pacote e executar teste de conteúdo**

Run: `dotnet test tests/GestaoPredio.IntegrationTests/GestaoPredio.IntegrationTests.csproj -c Release --filter PublishContentsTests`

- [ ] **Step 7: commitar**

```powershell
git add recepcaototem/recepcaototem.csproj recepcaototem/Program.cs recepcaototem/ClientApp/vite.config.ts tests/GestaoPredio.IntegrationTests/PublishContentsTests.cs
git commit -m "feat: serve React SPA from the API publish"
```

---

### Task 11: Integrar o React à sessão real

**Files:**

- Create: `recepcaototem/ClientApp/src/api/client.ts`
- Create: `recepcaototem/ClientApp/src/auth/SessionProvider.tsx`
- Create: `recepcaototem/ClientApp/src/auth/RequireSession.tsx`
- Modify: `recepcaototem/ClientApp/src/App.tsx`
- Modify: `recepcaototem/ClientApp/src/pages/Login.tsx`
- Modify: `recepcaototem/ClientApp/src/components/ProtectedRoute.tsx`
- Modify: `recepcaototem/ClientApp/src/components/AdminLayout.tsx`
- Modify: `recepcaototem/ClientApp/src/styles.css`
- Modify: `recepcaototem/ClientApp/package.json`
- Modify: `recepcaototem/ClientApp/package-lock.json`

**Interfaces:**

- Produces: `apiClient.ensureCsrf()`, `apiClient.post(...)` com `credentials: 'same-origin'`.
- Produces: `SessionProvider`, `useSession()` e estados `loading`, `anonymous`, `mustChangePassword`, `authenticated`.

- [ ] **Step 1: adicionar testes de componente para login, sessão, logout e troca obrigatória usando fetch mockado; instalar somente a dependência de teste indispensável e fixá-la no lockfile**

- [ ] **Step 2: executar testes e confirmar falha porque o frontend usa credencial fixa/localStorage**

- [ ] **Step 3: implementar cliente same-origin com token CSRF somente em memória; em 401 limpar sessão e em 403 preservar a mensagem autorizada**

- [ ] **Step 4: implementar `SessionProvider` que carrega `/api/auth/session`, sem persistir cookie/token/role no storage**

- [ ] **Step 5: substituir login demonstrativo pelo POST real, remover credenciais visíveis e temporizador falso, preservando layout**

- [ ] **Step 6: substituir `ProtectedRoute` pela sessão real e redirecionar `MustChangePassword` para tela integrada ao mesmo painel visual**

- [ ] **Step 7: usar display name/e-mail/role reais no `AdminLayout` e implementar logout POST real**

- [ ] **Step 8: procurar e provar ausência de `atrium_session`, `admin@demo.com`, senha `123456`, JWT e escrita de autenticação em localStorage**

Run: `rg -n "atrium_session|admin@demo\.com|123456|localStorage.*session|Bearer |jwt" recepcaototem/ClientApp/src`

Expected: nenhum resultado relacionado a autenticação.

- [ ] **Step 9: executar testes e build frontend**

```powershell
npm --prefix recepcaototem/ClientApp test -- --run
npm --prefix recepcaototem/ClientApp run build
```

- [ ] **Step 10: revisar visual em desktop/mobile sem redesenhar e commitar**

```powershell
git add recepcaototem/ClientApp
git commit -m "feat: connect React login to secure session"
```

---

### Task 12: Verificação integrada e documentação operacional

**Files:**

- Create: `docs/operations/authentication-deployment.md`
- Modify: `PROJECT_CONTEXT.md`
- Modify: `README.md`
- Test: all test projects and publish inspection

**Interfaces:**

- Produces: pacote `artifacts/publish/LumisApi`.
- Produces: ferramenta separada `artifacts/tools/GestaoPredio.AdminCli`.
- Produces: SQL `artifacts/AuthenticationAndProvisioning.sql` e `artifacts/migrations-auth-idempotent.sql`.

- [ ] **Step 1: documentar geração e conferência do SQL sem `database update`**

O procedimento deve exigir: backup, consulta a `__EFMigrationsHistory`, inventário das contas existentes, revisão do SQL, hash SHA-256, identidade de deploy com DDL temporário já autorizado, execução manual do script, validação do histórico e remoção/revogação do acesso DDL temporário conforme política operacional.

- [ ] **Step 2: documentar publicação sem alterar servidor automaticamente**

O procedimento deve incluir: backup IIS/publicação, build React dentro do publish, conferência de ausência da CLI, parada/início somente de `LumisApiPool`, preservação das variáveis externas, `/health`, `/health/ready`, rollback de arquivos e validação posterior sobre HTTPS.

- [ ] **Step 3: documentar bootstrap separado**

O procedimento deve publicar a CLI fora de `C:\Sites\Lumis\Api`, executar como identidade operacional/deploy com DML e apagar o pacote operacional após validação conforme política do servidor. Não alterar ACL de `SOPH-SISPONTO\LumisApi`.

- [ ] **Step 4: executar verificação completa fresca**

```powershell
dotnet tool restore
dotnet restore recepcaototem.sln
dotnet build recepcaototem.sln -c Release --no-restore
dotnet test recepcaototem.sln -c Release --no-build
npm --prefix recepcaototem/ClientApp ci
npm --prefix recepcaototem/ClientApp test -- --run
npm --prefix recepcaototem/ClientApp run build
dotnet publish .\recepcaototem\recepcaototem.csproj -c Release --no-restore -o .\artifacts\publish\LumisApi
dotnet publish .\tools\GestaoPredio.AdminCli\GestaoPredio.AdminCli.csproj -c Release --no-restore -o .\artifacts\tools\GestaoPredio.AdminCli
```

- [ ] **Step 5: inspecionar pacote e SQL**

Confirmar `web.config`, DLL da API, `wwwroot/index.html`, assets versionados e ausência de CLI/secrets/fontes. Confirmar que SQL de avanço contém somente `ALTER TABLE`, `ADD`, `CREATE INDEX` e registro da migration.

- [ ] **Step 6: executar smoke local em HTTPS de desenvolvimento**

Validar `/`, `/api/auth/csrf`, login, sessão, troca obrigatória, logout, 401/403/429 e health. Não usar o binding HTTP de Production para autenticação.

- [ ] **Step 7: atualizar contexto com resultados reais, revisar diff e commitar**

```powershell
git add docs PROJECT_CONTEXT.md README.md
git commit -m "docs: add secure authentication operations"
```

---

## Procedimento futuro de migration em produção

Este procedimento não é autorizado por este plano; será submetido novamente com o SQL final:

1. Confirmar backup recente e restauração verificável do `GestaoPredioDB`.
2. Consultar `__EFMigrationsHistory` e confirmar `20260904174759_InfrastructureFoundation`.
3. Inventariar `AspNetUsers`, especialmente contas existentes e `DisplayName` que receberá default vazio.
4. Comparar o hash do SQL aprovado com o arquivo no servidor.
5. Executar o script específico com identidade de deploy separada e DDL autorizado.
6. Confirmar novo registro no histórico, cinco novas colunas e índice composto.
7. Não conceder DDL à identidade runtime e não executar `dotnet ef database update`.
8. Em falha, preservar evidência e executar rollback aprovado; não improvisar alteração manual.

## Procedimento futuro de publicação

Este procedimento também dependerá de autorização específica:

1. Gerar `artifacts/publish/LumisApi` e `artifacts/tools/GestaoPredio.AdminCli` separadamente.
2. Provar que o pacote IIS contém a SPA e não contém a CLI.
3. Fazer backup da configuração IIS e de `C:\Sites\Lumis\Api`.
4. Preservar `ConnectionStrings__DefaultConnection`, `Security__DataProtectionPath`, AllowedHosts e limites externos.
5. Parar somente `LumisApiPool`, substituir publicação e iniciar somente esse pool.
6. Validar localmente `/health` e `/health/ready` na porta interna.
7. Validar `/`, antiforgery e autenticação somente pela origem HTTPS oficial.
8. Executar a CLI fora do webroot com identidade operacional/deploy e remover o pacote operacional após o bootstrap.
9. Em falha, restaurar arquivos/configuração anteriores e reciclar somente `LumisApiPool`.

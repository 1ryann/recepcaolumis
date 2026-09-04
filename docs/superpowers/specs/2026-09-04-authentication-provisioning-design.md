# Autenticação e provisionamento seguro — design aprovado

Data: 04/09/2026. Esta especificação registra a primeira funcionalidade real após a validação da fundação no IIS e SQL Server. Ela complementa `2026-09-04-mvp-congelado.md` sem redefinir arquitetura ou regras de negócio.

## Objetivo e limites

Implementar autenticação web real com ASP.NET Core Identity, cookie seguro, antiforgery, lockout, rate limiting, policies, auditoria e provisionamento controlado de usuários. Esta etapa integra somente o login existente e a identidade exibida no layout React.

Ficam fora desta etapa: salas, profissionais, locações, reservas, visitas, integrações externas, Cloudflare, configuração de HTTPS/443 e mudanças administrativas no IIS ou SQL Server. A aplicação jamais executará migrations, criação de roles ou bootstrap no startup.

## Topologia aprovada

```text
https://<dominio>/
├── /                 React SPA
├── /api/*            ASP.NET Core API
└── /health*           health checks
```

O build React será servido pela própria aplicação ASP.NET Core/IIS. A SPA e a API compartilham origem HTTPS, portanto CORS não é necessário para o fluxo normal. A porta 8082 permanece como binding interno do IIS. Em Production, somente `/health` e `/health/ready` aceitam HTTP; autenticação e demais rotas recusam HTTP. A configuração de HTTPS externo será feita posteriormente e não integra esta entrega.

## Modelo de usuário

`ApplicationUser` continua em Infrastructure, deriva de `IdentityUser` e recebe:

- `DisplayName`: obrigatório, `nvarchar(200)`, nome exibido na aplicação;
- `IsActive`: obrigatório, `bit`, padrão `true`;
- `MustChangePassword`: obrigatório, `bit`, padrão `false`.

O bootstrap cria o primeiro administrador com `MustChangePassword = false`, pois a senha definitiva é escolhida diretamente pelo operador em prompt oculto. Usuários criados pela API administrativa recebem senha temporária gerada pelo servidor e `MustChangePassword = true`.

Não haverá vínculo `ProfessionalId` nesta etapa. Desativação, redefinição administrativa de senha e edição de roles também ficam fora desta entrega; a modelagem apenas inclui `IsActive` para que autenticação e sessão já respeitem o estado.

## Auditoria de autenticação

`AuditEntry` recebe campos opcionais:

- `TargetUserId`: `nvarchar(450)`, sem foreign key, para preservar o histórico se o usuário for removido;
- `IpAddress`: `nvarchar(45)`, suficiente para IPv4 ou IPv6 textual.

Será criado o índice `IX_AuditEntries_Action_OccurredAt` sobre `(Action, OccurredAt)`. O índice existente sobre `OccurredAt` será preservado.

Eventos desta etapa:

- `LOGIN_SUCCEEDED`;
- `LOGIN_FAILED`;
- `LOGIN_RATE_LIMITED`;
- `LOGOUT_SUCCEEDED`;
- `PASSWORD_CHANGED`;
- `USER_CREATED`;
- `BOOTSTRAP_ADMIN_CREATED`.

Falhas de login para usuário inexistente terão `ActorUserId` e `TargetUserId` nulos. O registro conterá ação, resultado, instante UTC, correlation ID e IP obtido de `HttpContext.Connection.RemoteIpAddress`. Não será usado `X-Forwarded-For` enquanto proxies confiáveis não forem configurados. Senha, cookie, token antiforgery, body completo e e-mail inexistente não serão persistidos nem enviados a logs técnicos.

## Migration aditiva prevista

Nome planejado: `AuthenticationAndProvisioning`.

SQL estrutural esperado do EF Core, sujeito à conferência do script efetivamente gerado durante a implementação:

```sql
BEGIN TRANSACTION;

ALTER TABLE [AspNetUsers]
ADD [DisplayName] nvarchar(200) NOT NULL DEFAULT N'';

ALTER TABLE [AspNetUsers]
ADD [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit);

ALTER TABLE [AspNetUsers]
ADD [MustChangePassword] bit NOT NULL DEFAULT CAST(0 AS bit);

ALTER TABLE [AuditEntries]
ADD [TargetUserId] nvarchar(450) NULL;

ALTER TABLE [AuditEntries]
ADD [IpAddress] nvarchar(45) NULL;

CREATE INDEX [IX_AuditEntries_Action_OccurredAt]
ON [AuditEntries] ([Action], [OccurredAt]);

COMMIT;
```

Além desse DDL, o script gerado inserirá em `__EFMigrationsHistory` o ID exato atribuído pelo EF Core à migration `AuthenticationAndProvisioning`, com `ProductVersion = 10.0.11`. O script final gerado será a fonte executável e será apresentado antes de qualquer aplicação.

Alterações de schema:

- três colunas não nulas em `AspNetUsers`;
- duas colunas nulas em `AuditEntries`;
- um índice não único em `AuditEntries`.

Não há `DROP`, exclusão, truncamento, recriação de tabela ou alteração de tipo existente. Contas Identity preexistentes recebem `DisplayName = ''`, `IsActive = 1` e `MustChangePassword = 0`. Antes da aplicação em produção, essas contas deverão ser inventariadas; qualquer nome vazio será tratado operacionalmente, sem uma atualização destrutiva embutida na migration.

## Cookie e sessão

O esquema usa o cookie `__Host-Lumis.Auth` com:

- `HttpOnly = true`;
- `SecurePolicy = Always`;
- `SameSite = Lax`;
- `Path = /`;
- nenhum `Domain`;
- sessão não persistente;
- expiração de 30 minutos;
- renovação deslizante;
- respostas API 401/403 sem redirect HTML.

As chaves permanecem no `Security__DataProtectionPath` externo já validado. JWT e localStorage não serão usados para sessão.

`GET /api/auth/session` retorna somente `userId`, `displayName`, `email`, `roles` e `mustChangePassword`. A sessão é revalidada contra o security stamp e o estado `IsActive`; uma conta inativa perde acesso mesmo que possua cookie ainda válido.

## Antiforgery

`GET /api/auth/csrf` é anônimo, emite o cookie antiforgery `__Host-Lumis.Csrf` e devolve o request token em JSON. O cookie fica `HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/` e sem `Domain`. O token de request existe somente em memória no React e é enviado em `X-CSRF-TOKEN`.

`POST /api/auth/login`, `POST /api/auth/logout`, `POST /api/auth/change-password` e `POST /api/admin/users` exigem antiforgery. Falha retorna resposta genérica apropriada sem expor token ou detalhes internos.

## Login, lockout e rate limiting

`POST /api/auth/login` recebe DTO estrito com `email` e `password`. E-mail é normalizado pelo Identity. O fluxo usa `PasswordSignInAsync` com sessão não persistente e `lockoutOnFailure: true`.

Usuário inexistente, senha inválida, usuário inativo, usuário bloqueado ou usuário sem role permitido retornam HTTP 401 com:

```json
{
  "code": "INVALID_CREDENTIALS",
  "message": "E-mail ou senha inválidos."
}
```

Quando o usuário não existe, uma verificação de hash fictícia reduz diferenças observáveis de tempo. O endpoint não revela `IsLockedOut`, existência do e-mail ou estado da conta.

O limiter específico de login é configurado por `RateLimiting:LoginPermitLimit` e `RateLimiting:LoginWindowSeconds`, particionado por IP real da conexão e uma chave derivada em memória do e-mail normalizado. O e-mail não aparece na chave exposta ou nos logs. Excesso retorna HTTP 429 e registra `LOGIN_RATE_LIMITED`. O limiter global da fundação permanece separado.

## Troca obrigatória de senha

Usuário com `MustChangePassword = true` pode acessar somente:

- `GET /api/auth/session`;
- `GET /api/auth/csrf`;
- `POST /api/auth/change-password`;
- `POST /api/auth/logout`.

Todas as demais APIs autenticadas exigem a policy `PasswordChanged`. A troca exige senha atual, nova senha e confirmação. Após `UserManager.ChangePasswordAsync`, a aplicação define `MustChangePassword = false`, atualiza o security stamp, grava auditoria e renova o cookie. A senha não é devolvida ou registrada.

## Roles e policies

Roles exatos e imutáveis nesta etapa:

- `ADMINISTRADOR`;
- `GERENTE`;
- `PROFISSIONAL`.

Policies:

- `Administration`: usuário ativo, senha já alterada e role `ADMINISTRADOR`;
- `Operations`: usuário ativo, senha já alterada e role `ADMINISTRADOR` ou `GERENTE`;
- `Professional`: usuário ativo, senha já alterada e role `PROFISSIONAL`;
- `PasswordChanged`: usuário ativo e `MustChangePassword = false`.

O backend é a autoridade. A interface apenas reflete permissões já avaliadas pelo servidor.

## CLI de bootstrap

Será criado `tools/GestaoPredio.AdminCli`, projeto separado que referencia Infrastructure. Ele não será referenciado pelo projeto web, não entrará no `dotnet publish` da API e será publicado separadamente em `artifacts/tools/GestaoPredio.AdminCli`.

Comando único:

```powershell
GestaoPredio.AdminCli.exe bootstrap-admin
```

Regras:

1. Lê `ConnectionStrings__DefaultConnection` externamente.
2. Em Production, exige autenticação integrada e rejeita usuário/senha SQL.
3. Não executa migrations ou criação de banco.
4. Solicita `DisplayName`, e-mail e senha; senha e confirmação são lidas por prompt oculto.
5. Não aceita senha por argumento, arquivo ou variável de ambiente.
6. Valida a senha com as mesmas opções Identity da API.
7. Abre transação e cria somente os três roles aprovados quando ausentes.
8. Recusa bootstrap se já houver membro no role `ADMINISTRADOR`.
9. Cria o primeiro Admin com `IsActive = true` e `MustChangePassword = false`.
10. Persiste `BOOTSTRAP_ADMIN_CREATED` antes do commit.
11. Em falha, reverte a transação e mostra mensagem operacional sem senha ou connection string.

A ferramenta será executada por uma identidade operacional/deploy separada que já tenha DML nas tabelas Identity e `AuditEntries`. Ela não exige DDL e não exige aumento de permissões para `SOPH-SISPONTO\LumisApi`.

## Criação administrativa de usuários

`POST /api/admin/users`, protegido por `Administration`, recebe `displayName`, `email` e exatamente um dos três roles aprovados. Não aceita senha do administrador nem propriedades extras de Identity.

O servidor gera uma senha temporária criptograficamente segura, compatível com a política Identity. A resposta HTTP 201 devolve a senha somente nessa resposta. Ela nunca será recuperável depois: somente o hash Identity é persistido. O usuário recebe `IsActive = true` e `MustChangePassword = true`.

Criação de usuário, associação do role e auditoria ocorrem na mesma transação. E-mail duplicado ou role inválido retorna erro de validação sem detalhes internos. A resposta da senha temporária terá `Cache-Control: no-store`.

## Integração React e arquivos estáticos

O build Vite será incorporado ao publish da API e servido com `UseDefaultFiles`, `UseStaticFiles` e fallback para `index.html`, sem interceptar `/api` ou `/health`. O projeto web continuará sendo o único pacote IIS.

O publish executará `npm ci` e `npm run build` de maneira determinística e incluirá somente `ClientApp/dist`. A CLI não será incluída. A Content Security Policy será ajustada para permitir recursos próprios necessários à SPA, mantendo `object-src 'none'`, `base-uri 'self'` e `frame-ancestors 'none'`.

No React serão removidos a credencial demonstrativa e `atrium_session`. Um `SessionProvider` mantém sessão e token antiforgery somente em memória, usa `credentials: 'same-origin'`, carrega `/api/auth/session` e executa logout real. O layout e a identidade visual permanecem.

## Testes e aceite

Os testes seguem TDD e nunca usam produção. Testes de integração de Identity usam um banco SQL Server exclusivo de testes, informado por `ConnectionStrings__AuthTests`. A suíte deve recusar connection strings cujo banco seja `GestaoPredioDB` ou cujo ambiente seja Production.

Cobertura mínima:

- cookie contém `HttpOnly`, `Secure`, `SameSite=Lax`, `Path=/` e não contém `Domain`;
- login válido, sessão, logout e expiração/revalidação;
- respostas equivalentes para usuário inexistente, senha inválida, inativo e bloqueado;
- lockout após cinco falhas;
- rate limit do login e isolamento do health;
- antiforgery obrigatório em todos os POSTs da etapa;
- policies para os três roles;
- bloqueio de APIs quando `MustChangePassword = true`;
- troca de senha libera a sessão e renova cookie;
- criação de usuário somente por Admin;
- senha temporária exibida uma vez e ausente de banco, auditoria e logs;
- bootstrap único, transacional, sem migration e sem senha em argumentos;
- migration contém apenas alterações aditivas previstas;
- publish contém SPA e não contém CLI, fontes, `.env` ou secrets;
- os 16 testes existentes permanecem aprovados.

## Produção e fronteira de aprovação

O fluxo de entrega será: gerar e revisar migration; gerar SQL idempotente; inspecionar `__EFMigrationsHistory` e schema; obter aprovação; backup; aplicar com identidade de deploy; publicar API/SPA; reciclar somente `LumisApiPool`; validar health; executar CLI separada; validar login sobre HTTPS.

Cada alteração em banco, IIS ou servidor exige apresentação concreta e aprovação. Nenhum comando `database update` será usado em produção. A aplicação continua sem `Migrate()` e `EnsureCreated()`.

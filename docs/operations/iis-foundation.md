# Fundação IIS — implantação pendente de aprovação

## Alvo informado

Servidor 10.255.36.167; site LumisApi; pool LumisApiPool; porta interna 8082; publicação em C:\Sites\Lumis\Api. Runtime .NET 10 Hosting Bundle informado como instalado. SQL Server Express local e banco já disponibilizados pelo usuário; valores de conexão são configurados externamente. Identidade Windows própria já tem acesso ao banco.

Esta entrega não alterou IIS, ACLs, contas Windows ou o banco de produção. A conexão de rede ao health do servidor expirou durante a verificação a partir da estação de desenvolvimento. Não afirmar validação no IIS enquanto as duas respostas abaixo não forem obtidas no servidor.

## Gerar pacote

Na raiz do repositório:

```powershell
dotnet tool restore
dotnet restore recepcaototem.sln
dotnet build recepcaototem.sln -c Release --no-restore
dotnet test recepcaototem.sln -c Release --no-build
dotnet publish recepcaototem/recepcaototem.csproj -c Release --no-restore -o artifacts/publish/LumisApi
```

O pacote é framework-dependent e inclui web.config com AspNetCoreModuleV2, hostingModel inprocess e recepcaototem.dll. Binários gerados em artifacts não vão ao Git; são reproduzíveis em casa. Não copiar ClientApp, .git, secrets, banco ou certificados para o diretório do site.

## Mudanças propostas no servidor — executar somente após aprovação

1. Inspecionar configuração atual do site/pool e salvar backup da configuração e da publicação atual.
2. Confirmar pool existente com a conta Windows própria, arquitetura compatível, No Managed Code e perfil de usuário carregado. Qualquer ajuste será mostrado antes; não trocar identidade nem aumentar privilégios.
3. Configurar externamente ASPNETCORE_ENVIRONMENT, ConnectionStrings__DefaultConnection, AllowedHosts e Security__DataProtectionPath conforme configuration.md. O valor da conexão não será commitado.
4. Preparar diretório privado de chaves fora de C:\Sites\Lumis\Api com acesso somente da conta e administradores; mostrar caminho/ACL proposta antes de executar.
5. Publicar o conteúdo de artifacts/publish/LumisApi em C:\Sites\Lumis\Api, preservando backup e coordenando breve parada/reciclagem apenas do pool LumisApiPool. Não reiniciar o IIS inteiro.
6. Manter porta 8082 interna. Não alterar firewall ou expor SQL.
7. Validar localmente as duas rotas. Se falhar, restaurar os arquivos/configuração anteriores; não modificar o banco para fazer o health passar.

```powershell
Invoke-RestMethod http://localhost:8082/health
Invoke-RestMethod http://localhost:8082/health/ready
```

Esperado: HTTP 200 com {"status":"Healthy"} em ambas. Readiness valida conexão pelo processo real da API e sua conta Windows; não valida existência de todas as tabelas nem permissões de alteração do esquema. HTTP 503 retorna somente {"status":"Unhealthy"}.

## Migrations — separadas da aplicação

Migration Identity antiga preservada com seu ID original. Migration InfrastructureFoundation adiciona somente AuditEntries e índice; nenhum cadastro, role ou senha é criado no startup. Não há Migrate/EnsureCreated no startup.

Gerar SQL idempotente sem conectar ao banco:

```powershell
dotnet ef migrations script --idempotent --project src/GestaoPredio.Infrastructure --startup-project recepcaototem --output artifacts/migrations.sql
```

Antes de aplicar: conferir esquema real e __EFMigrationsHistory com uma identidade de deploy, revisar o SQL e backup. Um banco com tabelas existentes mas sem histórico compatível precisa de reconciliação prévia; não executar CreateIdentity cegamente.

A execução exige aprovação do usuário e identidade administrativa/deploy separada, com permissão de criar/alterar tabelas. Não aumentar as permissões da conta runtime e não usar sa. Health pode validar conectividade antes de aplicar migrations.

Factory de design-time configura somente provider SQL Server, sem connection string. Isso permite gerar migration/script sem produção. Não há comando automático de database update nesta entrega.

## Escopo deliberadamente não implementado

Login completo, provisionamento de contas, fluxos comerciais, agenda, arquivos de visitantes, relatórios, Meta e Intelbras. Identity/roles/policies e auditoria são fundação; não representam os módulos funcionais do MVP.

## Fontes técnicas

- https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying

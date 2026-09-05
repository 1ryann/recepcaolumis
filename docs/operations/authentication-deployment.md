# Autenticação: migration, publicação e bootstrap

Este documento descreve uma operação futura. Ele não autoriza alteração de produção. Migrations nunca são executadas no startup e a identidade `SOPH-SISPONTO\LumisApi` não recebe DDL nem permissões adicionais.

## Preparar e revisar a migration

1. Gere `artifacts/AuthenticationAndProvisioning.sql` a partir de `20260904174759_InfrastructureFoundation` e o script idempotente completo com `dotnet ef migrations script`; não use `database update`.
2. Calcule `Get-FileHash -Algorithm SHA256` e registre o hash do arquivo aprovado.
3. Antes da janela, confirme backup recente com restauração verificada, consulte `__EFMigrationsHistory` e inventarie `AspNetUsers` e `AuditEntries`.
4. Confirme que `InfrastructureFoundation` já consta no histórico. A migration nova adiciona `DisplayName`, `IsActive`, `MustChangePassword`, `TargetUserId`, `IpAddress` e o índice `IX_AuditEntries_Action_OccurredAt`.
5. Uma identidade operacional separada, com DDL temporário previamente autorizado, executa manualmente o SQL cujo hash foi aprovado. Depois, confira histórico, colunas e índice e revogue o acesso temporário conforme a política operacional.
6. Em falha, preserve logs/evidências e use apenas o rollback previamente aprovado. Não improvise DDL nem execute o método `Down`, pois ele removeria os novos campos.

## Publicar a aplicação

Gere os destinos separadamente:

```powershell
dotnet publish .\recepcaototem\recepcaototem.csproj -c Release -o .\artifacts\publish\LumisApi
dotnet publish .\tools\GestaoPredio.AdminCli\GestaoPredio.AdminCli.csproj -c Release -o .\artifacts\tools\GestaoPredio.AdminCli
```

O primeiro comando executa `npm ci` e `npm run build` e incorpora somente `ClientApp/dist` em `wwwroot`. Confira `web.config`, `recepcaototem.dll`, `wwwroot/index.html`, assets versionados e a ausência de `.env`, fontes TypeScript, source maps e `GestaoPredio.AdminCli`.

Na operação futura autorizada, exporte a configuração do IIS e copie `C:\Sites\Lumis\Api` para um backup datado. Preserve `ConnectionStrings__DefaultConnection`, `Security__DataProtectionPath`, `AllowedHosts` e limites externos. Pare e inicie somente `LumisApiPool` ao substituir os arquivos. Valide `/health` e `/health/ready` pelo binding interno e valide `/`, CSRF e autenticação pela origem HTTPS oficial. O rollback restaura o backup e recicla somente esse pool.

## Criar o primeiro administrador

Execute a CLI fora de `C:\Sites\Lumis\Api`, como identidade operacional/deploy separada que já tenha o DML necessário. Configure externamente a mesma connection string e execute somente:

```powershell
GestaoPredio.AdminCli.exe bootstrap-admin
```

Nome, e-mail e senha são solicitados interativamente; a senha usa prompt sem eco e não é aceita em argumento ou variável de ambiente. A ferramenta recusa um segundo administrador, cria os três roles e registra auditoria na mesma transação. Ela não cria schema nem aplica migration. Após validar o login, remova o pacote operacional conforme a política do servidor. Não altere a ACL ou os privilégios da identidade runtime.

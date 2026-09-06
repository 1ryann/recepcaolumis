# Migração SQL Server para Supabase PostgreSQL

Este runbook migra o LUMIS de `SOPH-SISPONTO\\SQLEXPRESS / GestaoPredioDB` para o projeto Supabase **Lumis Production**. O SQL Server permanece intacto. A pré-carga não muda IIS, binários, connection string ou tráfego de produção.

## Limites operacionais

- A ferramenta lê o SQL Server e nunca executa `INSERT`, `UPDATE`, `DELETE`, `ALTER`, `DROP` ou migrations nele.
- As migrations PostgreSQL são aplicadas apenas ao target que passar pelo fingerprint aprovado.
- `__EFMigrationsHistory` do SQL Server não é copiada.
- `RowVersion` não é copiado; PostgreSQL gera `xmin`.
- Senhas, hashes, stamps, tokens, connection strings e dados pessoais não são impressos.
- `DefaultConnection` de Development continua apontando para `localhost / LumisDev`.
- Nenhuma etapa deste documento autoriza por si só o cutover. O bloco de cutover exige autorização explícita após a pré-carga.

## Artefatos e migrations PostgreSQL

Migrations da branch:

1. `20260905234344_PostgreSqlBaseline`
2. `20260906034221_LeasesFoundation`

Ferramenta:

```powershell
tools\GestaoPredio.DataMigration\GestaoPredio.DataMigration.csproj
```

O provider `Microsoft.Data.SqlClient` existe somente nessa ferramenta. A aplicação principal permanece exclusivamente Npgsql.

## Mapeamento da pré-carga

| SQL Server | PostgreSQL | Tratamento |
|---|---|---|
| `AspNetRoles` | `AspNetRoles` | IDs e stamps preservados |
| `AspNetUsers` | `AspNetUsers` | IDs, `PasswordHash`, `SecurityStamp`, estado e lockout preservados |
| `AspNetRoleClaims` | `AspNetRoleClaims` | IDs preservados; sequence ajustada |
| `AspNetUserClaims` | `AspNetUserClaims` | IDs preservados; sequence ajustada |
| `AspNetUserLogins` | `AspNetUserLogins` | chaves compostas preservadas |
| `AspNetUserRoles` | `AspNetUserRoles` | relacionamentos preservados |
| `AspNetUserTokens` | `AspNetUserTokens` | valores copiados sem logging |
| `AuditEntries` | `AuditEntries` | IDs e timestamps preservados |
| `PrivateFiles` | `PrivateFiles` | somente metadata; bytes permanecem no filesystem privado |
| `Rooms` | `Rooms` | GUIDs e `decimal(18,2)` preservados; `RowVersion` excluído |
| `Professionals` | `Professionals` | GUIDs e FKs preservados; `RowVersion` excluído |

`Tenants`, `Leases` e `LeaseOccurrences` são criadas pelas migrations e permanecem vazias porque não há equivalente implantado no SQL Server antigo.

## Configuração externa segura

Na worktree `codex/sqlserver-to-supabase-production`, execute:

```powershell
cd "C:\Users\ryan-\OneDrive\Documents\projetos\recepcaolumis\.worktrees\sqlserver-to-supabase-production"
powershell -ExecutionPolicy Bypass -File .\tools\GestaoPredio.DataMigration\Configure-MigrationSecrets.ps1
```

O script solicita as duas conexões com entrada oculta, preserva `ConnectionStrings:DefaultConnection` e grava somente:

- `Migration:SourceConnection`
- `Migration:TargetConnection`
- `Migration:TargetProject=Lumis`
- `Migration:TargetEnvironment=Production`

Para gerar a identificação sanitizada do target:

```powershell
dotnet run --project .\tools\GestaoPredio.DataMigration -- --fingerprint-target
```

Compare o projeto/host no painel Supabase sem copiar o host para logs. Depois armazene o hash exibido, que não contém senha:

```powershell
dotnet user-secrets set "Migration:TargetFingerprint" "<FINGERPRINT_SHA256>" --project .\tools\GestaoPredio.DataMigration
```

## Validação read-only

Validar primeiro cada lado separadamente:

```powershell
dotnet run --project .\tools\GestaoPredio.DataMigration -- --validate-source
dotnet run --project .\tools\GestaoPredio.DataMigration -- --validate-target
dotnet run --project .\tools\GestaoPredio.DataMigration -- --dry-run
```

A origem deve responder como `SQL Server / GestaoPredioDB`, e a consulta live exige `SERVERPROPERTY('ServerName') = SOPH-SISPONTO\\SQLEXPRESS`. O target precisa ser Supabase remoto, não pode ser `LumisDev` e deve coincidir com o fingerprint aprovado.

Se o schema `public` contiver tabelas inesperadas, a ferramenta para. Ela não remove nem altera essas tabelas.

## Preparar schema Supabase

Somente depois de `--validate-target` confirmar target vazio e correto:

```powershell
dotnet run --project .\tools\GestaoPredio.DataMigration -- `
  --prepare-target-schema `
  --confirm PREPARE_LUMIS_PRODUCTION_SCHEMA
```

O comando aplica apenas as migrations Npgsql listadas neste documento. Depois, `--validate-target` deve listar exclusivamente as tabelas esperadas e as duas migrations PostgreSQL.

## Primeira pré-carga real

O IIS continua usando SQL Server durante esta operação. A ferramenta:

1. confirma origem e target sanitizados;
2. exige target sem dados de aplicação;
3. captura as tabelas SQL Server em ordem determinística;
4. insere no PostgreSQL dentro de uma única transação serializable;
5. reconsulta a origem antes do commit;
6. faz rollback se a origem mudou ou se o conteúdo divergiu;
7. ajusta sequences dos claims;
8. reconcilia contagem, SHA-256 de conteúdo e FKs sem imprimir valores.

```powershell
dotnet run --project .\tools\GestaoPredio.DataMigration -- `
  --execute `
  --confirm MIGRATE_GESTAOPREDIODB_TO_LUMIS_PRODUCTION
```

Repetir a reconciliação read-only:

```powershell
dotnet run --project .\tools\GestaoPredio.DataMigration -- --reconcile
```

Todos os registros devem mostrar `SourceCount == TargetCount`, `Match=True` e `ForeignKeyErrors=0`.

## Validação de arquivos privados

Os bytes não são enviados ao Supabase. No servidor, antes do cutover, valide em memória que cada `PrivateFiles.StorageKey` possui arquivo correspondente em:

```text
C:\ProgramData\Lumis\PrivateFiles\files\<StorageKey>
```

O relatório deve conter somente quantidade total e quantidade ausente. Não registrar storage keys, nomes originais ou paths individuais.

## Teste local read-only contra Supabase

Execute a API local com `ConnectionStrings__DefaultConnection` definida apenas no processo a partir do secret de migração. Não altere `DefaultConnection`. Valide:

- `/health` = 200
- `/health/ready` = 200
- `/api/auth/csrf` = 200
- endpoints protegidos retornam 401 sem sessão
- após login manual controlado, listagens de Profissionais, Salas e Locações funcionam sem mutação de negócio

Ao encerrar o processo, remova `ConnectionStrings__DefaultConnection` da sessão. Não salve a conexão em script, log ou histórico.

## Pacote PostgreSQL

```powershell
dotnet test .\recepcaototem.sln --no-restore -m:1 -p:UseSharedCompilation=false
cd .\recepcaototem\ClientApp
npm test -- --run
npm run build
npm run verify:production-bundle
cd ..\..
dotnet publish .\recepcaototem\recepcaototem.csproj -c Release --no-restore `
  -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false `
  -o .\artifacts\publish-supabase
```

Verificar no publish:

- frontend compilado presente;
- nenhuma `GestaoPredio.AdminCli`;
- nenhuma `GestaoPredio.DataMigration`;
- nenhum `.env` ou secret;
- nenhuma URL localhost ou marcador de mock;
- hash SHA-256 registrado para o pacote fechado.

## Pre-cutover no servidor

Executar somente após autorização de cutover:

1. confirmar testes, reconciliação e hash do pacote;
2. criar backup completo de `GestaoPredioDB` em diretório protegido;
3. executar `RESTORE VERIFYONLY` no backup;
4. criar backup IIS com `appcmd add backup`;
5. copiar o pacote SQL Server atual para diretório de rollback protegido;
6. registrar a configuração antiga sem imprimi-la em console;
7. confirmar que o pacote PostgreSQL permanece fechado aos usuários.

Exemplo de backup do banco, ajustando apenas o diretório protegido aprovado:

```powershell
sqlcmd -S "localhost\SQLEXPRESS" -E -b -Q "BACKUP DATABASE [GestaoPredioDB] TO DISK=N'C:\Backups\Lumis\GestaoPredioDB-pre-supabase.bak' WITH COPY_ONLY, CHECKSUM, INIT"
sqlcmd -S "localhost\SQLEXPRESS" -E -b -Q "RESTORE VERIFYONLY FROM DISK=N'C:\Backups\Lumis\GestaoPredioDB-pre-supabase.bak' WITH CHECKSUM"
& "$env:windir\system32\inetsrv\appcmd.exe" add backup LumisPreSupabaseCutover
```

## Manutenção e snapshot final

Executar somente após autorização de cutover:

```powershell
Import-Module WebAdministration
Stop-Website -Name 'LumisApi'
Stop-WebAppPool -Name 'LumisApiPool'
```

Confirmar site e pool parados. Então executar a substituição controlada. Ela usa `DELETE` apenas nas onze tabelas mapeadas, em ordem reversa de FK, dentro da mesma transação que importa o snapshot final. Não altera schema, migrations, `Tenants`, `Leases` ou `LeaseOccurrences`.

```powershell
dotnet run --project .\tools\GestaoPredio.DataMigration -- `
  --replace-import `
  --confirm REPLACE_LUMIS_PRODUCTION_PRELOAD_AFTER_IIS_STOP
dotnet run --project .\tools\GestaoPredio.DataMigration -- --reconcile
```

Se qualquer verificação falhar, a transação restaura a pré-carga anterior.

## Deploy fechado

Ainda com o site parado:

1. substituir `C:\Sites\Lumis\Api` pelo pacote cujo hash foi aprovado;
2. alterar somente `ConnectionStrings__DefaultConnection`, usando entrada oculta e API de configuração do IIS; nunca colocar a conexão em argumento ou console;
3. preservar Data Protection, cookies, CSRF, `AllowedHosts`, CSP, PrivateFiles, TLS, bindings, identidade Windows e ACLs;
4. iniciar apenas `LumisApiPool` e `LumisApi`.

```powershell
Start-WebAppPool -Name 'LumisApiPool'
Start-Website -Name 'LumisApi'
```

## Smoke fechado

Antes de liberar usuários:

1. `/health`
2. `/health/ready`
3. `/api/auth/csrf`
4. login ADMINISTRADOR
5. Profissionais
6. Salas
7. Locações

Não liberar tráfego se qualquer etapa falhar.

## Rollback

O SQL Server e o backup não são apagados. Se o smoke fechado falhar antes da reabertura:

1. parar site e pool;
2. restaurar o diretório binário SQL Server salvo;
3. restaurar o backup IIS `LumisPreSupabaseCutover`, que contém a connection string antiga;
4. iniciar pool e site;
5. validar health, CSRF e login contra SQL Server;
6. manter Supabase fechado para diagnóstico.

Depois de liberar gravações no Supabase, rollback para SQL Server pode perder dados novos. Uma decisão específica de reconciliação reversa será necessária; não executar rollback cego.

## Trava final

Antes de parar IIS ou alterar produção, registrar:

- branch, commits e worktree;
- tabelas e contagens reconciliadas;
- `ForeignKeyErrors=0`;
- Identity e hashes preservados;
- migrations do Supabase;
- testes .NET e React;
- hash do pacote;
- backup verificado;
- pacote e configuração de rollback prontos.

Somente após aprovação explícita prosseguir para manutenção, snapshot final, deploy e abertura.

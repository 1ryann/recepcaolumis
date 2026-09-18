# Room Rental — preparação de staging (2026-09-13)

> Registro do operador. Não contém segredos — nenhuma senha, connection string completa ou número real de
> telefone. Toda variável remota mencionada aqui (WhatsApp, storage) é citada só pelo nome; nenhum valor é
> registrado.

- Spec: `docs/superpowers/specs/2026-09-13-room-rental-and-totem-carousel.md`
- Plano: `docs/superpowers/plans/2026-09-13-room-rental-flow.md` (Task 14 de 14)
- Runbook de referência (convenções de formato e sequência Railway/Supabase): `docs/operations/staging-railway-runbook.md`
- Runbook de referência (convenções de inspeção de migration): `docs/operations/professionals-rooms-production-migration.md`
- Branch: `codex/reception-backend` — HEAD no início desta task: `0bd6b02` ("feat(admin): convert rental inquiries to leases")
- Migration única do plano: `20260914054052_RoomPhotosAndRentalInquiries` (precedida por `20260910215630_TotemBookingHandoff`, confirmado por listagem do diretório de migrations, não pela suposição inicial do brief — que também acertou o nome)

## Status

> **Atualização 2026-09-18 — este status foi superado.** O texto original de 2026-09-13 continua logo abaixo como
> registro histórico. Estado atual, segundo confirmação do usuário (operador) nesta branch:
>
> - **Banco de staging (Supabase `xpblbvrmljtvyltvvnpd`):** todas as 17 migrations PostgreSQL da branch aplicadas.
>   `20260914054052_RoomPhotosAndRentalInquiries` já constava como aplicada quando o estado real foi conferido;
>   `20260915231152_AddDesiredDatesToRoomRentalInquiry` e `20260918020844_AddWhatsAppMessages` foram aplicadas
>   depois, com autorização explícita do usuário, e validadas por ele no SQL Editor.
> - **Código:** `origin/codex/reception-backend` está em `c4f5fe0`. Os commits locais posteriores (relógio fixo nos
>   testes, período desejado na mensagem do WhatsApp, volta automática da tela de sucesso, ajustes no modal de Nova
>   locação e documentação) **não foram enviados** — chegam ao staging só depois de um push autorizado.
> - **Continuam sem confirmação registrada** (ver "Pendências para staging"): volume persistente de
>   `Storage__PrivateFilesPath`, `Whatsapp__FinanceiroPhoneNumber` preenchido, preflight de grants `anon`/`authenticated`
>   da Data API nas tabelas novas, backup/restauração, health e smoke do fluxo em staging.

**Registro original (2026-09-13):** **PREPARAÇÃO LOCAL APROVADA.** **STAGING NÃO ALTERADO.** Nenhuma migration foi
aplicada a nenhum banco real, nenhuma variável de ambiente foi lida ou alterada, nenhum push/deploy/PR foi executado.
Todos os itens remotos abaixo permanecem **PENDENTES DE AUTORIZAÇÃO** explícita e separada.

## Tabela de gates

| Gate | Comando/Evidência | Resultado |
|---|---|---|
| Árvore limpa | `git status --porcelain -uall` (antes e depois dos gates) | **APROVADO LOCAL** — nenhuma alteração fora deste runbook |
| Build + testes completos (.NET) | `dotnet build recepcaototem.sln`; `dotnet test recepcaototem.sln`; `git diff --check` | **APROVADO LOCAL** — build 0 erro/0 aviso; 907/907 testes aprovados (4 projetos); `git diff --check` sem saída |
| Frontend (build + testes) | `npx vitest run`; `npx tsc -b`; `npx vite build`; `node scripts/verify-production-bundle.mjs` | **APROVADO LOCAL** — 505/505 testes (80 arquivos); 0 erro de tipo; build de produção ok; verificador de bundle aprovado |
| Script SQL gerado offline | `dotnet ef migrations script TotemBookingHandoff RoomPhotosAndRentalInquiries --idempotent --output <TEMP>` | **APROVADO LOCAL** (revisão offline, sem conexão) — inspecionado, hash registrado, arquivo temporário apagado |
| `Storage__PrivateFilesPath` aponta para volume persistente do Railway | Confirmação manual no dashboard Railway | **PENDENTE DE AUTORIZAÇÃO** (remoto) — NÃO EXECUTADO NESTA TASK |
| `Whatsapp__FinanceiroPhoneNumber` configurado com número real | Confirmação manual, valor nunca registrado neste documento | **PENDENTE DE AUTORIZAÇÃO** (remoto) — NÃO EXECUTADO NESTA TASK |
| Preflight de privilégios Supabase Data API (anon/authenticated em `RoomPhotos`/`RoomRentalInquiries`) | Consulta manual de grants no schema `public` | **PENDENTE DE AUTORIZAÇÃO** (remoto) — NÃO EXECUTADO NESTA TASK |
| Backup/restore validado | Snapshot recente do Supabase + restauração testada | **PENDENTE DE AUTORIZAÇÃO** (remoto) — NÃO EXECUTADO NESTA TASK |
| Migration aplicada em staging | `dotnet ef database update` via conexão de migração autorizada (Session pooler) | **PENDENTE DE AUTORIZAÇÃO** (remoto) — **NÃO EXECUTADO NESTA TASK, e não pode ser executado sem autorização explícita separada** |
| Deploy | Publicação da imagem/commit no Railway | **PENDENTE DE AUTORIZAÇÃO** (remoto) — NÃO EXECUTADO NESTA TASK |
| Health/smoke | `/health`, `/health/ready`, smoke catálogo/foto/form/QR/Admin | **PENDENTE DE AUTORIZAÇÃO** (remoto) — NÃO EXECUTADO NESTA TASK |

## RED operacional (antes dos gates)

Antes de executar qualquer gate local, o estado era **PREPARAÇÃO INCOMPLETA**: nenhum teste havia sido rodado nesta
task, o script SQL não havia sido gerado/inspecionado, e nenhuma das confirmações de configuração externa
(WhatsApp, storage, backup, grants) havia sido feita. Esse é o RED operacional exigido pelo Step 2 do plano —
pendências remotas não viram PASS por omissão; elas permanecem PENDENTES até este documento ser revisado de novo,
depois de autorização e execução manual pelo operador.

## Evidência detalhada — gates locais (Step 3)

### 1. `dotnet build recepcaototem.sln`

```
Compilação com êxito.
    0 Aviso(s)
    0 Erro(s)
```

Todos os 10 projetos da solução (Domain, Application, Infrastructure, AdminCli, DataMigration + testes,
recepcaototem, UnitTests, IntegrationTests) compilaram sem erro nem aviso.

### 2. `dotnet test recepcaototem.sln`

Saída consolidada (4 projetos de teste, exit code `0`):

| Projeto | Total | Aprovados | Reprovados |
|---|---|---|---|
| GestaoPredio.DataMigration.Tests | 18 | 18 | 0 |
| GestaoPredio.AdminCli.Tests | 9 | 9 | 0 |
| GestaoPredio.UnitTests | 344 | 344 | 0 |
| GestaoPredio.IntegrationTests | 536 | 536 | 0 |
| **Total** | **907** | **907** | **0** |

Nenhuma ocorrência de "Reprovado" no log completo. **Nota sobre falhas pré-existentes conhecidas:** tasks
anteriores desta sessão haviam identificado um cluster de testes (`ReservationAdministrationTests`,
`ProfessionalIncidentApiTests`, `CheckInManualCodeTests`, às vezes `ProfessionalReservationTests`/
`CustomerApiTests`) que falha de forma idêntica mesmo em commits anteriores a este plano, associado a um bug de
reagendamento/conflito de disponibilidade rastreado em outra frente de trabalho, fora do escopo deste plano. **Nesta
execução específica (Task 14), esse cluster não falhou** — os 907 testes passaram, incluindo os desse cluster.
Registrado aqui por transparência: se uma reexecução futura desses mesmos testes voltar a falhar de forma idêntica
ao padrão já documentado, isso deve ser tratado como a mesma condição pré-existente e fora de escopo, não como uma
regressão desta feature.

Algumas linhas de log são ruído esperado de testes negativos (por exemplo, `OptionsValidationException` sobre
`Whatsapp` ausente e `Health check database ... Unhealthy` são asserts intencionais de
`WhatsappConfigurationTests`/testes de health check, não falhas reais).

### 3. `git diff --check`

Sem saída, exit code `0` — nenhum espaço em branco de fim de linha nem marcador de conflito no diff (que, neste
ponto, estava vazio, pois a árvore já estava limpa).

### 4. Frontend — `npx vitest run`

```
Test Files  80 passed (80)
     Tests  505 passed (505)
```

### 5. Frontend — `npx tsc -b`

Saída vazia, exit code `0` — sem erros de tipo.

### 6. Frontend — `npx vite build`

Build de produção concluído com sucesso (`✓ built in 388ms`), 1941 módulos transformados, `dist/` gerado
(`index.html`, `manifest.json`, assets JS/CSS/fontes). Aviso não bloqueante do Vite sobre um chunk acima de 500 kB
(`index-*.js`, 541.89 kB minificado / 148.25 kB gzip) — recomendação de code-splitting, não um erro; não introduzido
por esta task e não bloqueia o gate.

### 7. Frontend — `node scripts/verify-production-bundle.mjs`

```
Production bundle verifier passed: no development AppStore or mock storage markers were emitted.
```

Verificação adicional (não exigida pelo brief, feita por precaução): `grep` no bundle gerado
(`dist/assets/*.js`) por `FinanceiroPhoneNumber`, `+5569999999999` e qualquer variação de número de WhatsApp —
nenhuma ocorrência. A única string relacionada a WhatsApp no bundle é o host genérico `wa.me` (esperado, é o único
host aceito pela UI para abrir o link — o número real nunca é montado no cliente, só o backend monta a URL).

## Inspeção do script de migration (Step 3)

Comando executado (sem conexão com qualquer banco, saída só para arquivo local):

```
dotnet ef migrations script TotemBookingHandoff RoomPhotosAndRentalInquiries --idempotent \
  --project src/GestaoPredio.Infrastructure --startup-project recepcaototem --context ApplicationDbContext \
  --output <TEMP>\lumis-room-rental.sql
```

- Arquivo gerado em `%TEMP%\lumis-room-rental.sql` (fora do repositório), inspecionado e **apagado logo em
  seguida** — não foi commitado nem permanece no disco.
- Tamanho: **4556 bytes**, 108 linhas.
- **SHA-256 do conteúdo:** `f4b2ebf312e1c188175b81040f685194f05884a12233bb3fc0e8d457a80c9455`

Contagem de operações (via `grep -c` sobre o arquivo antes de apagar):

| Operação | Contagem | Detalhe |
|---|---|---|
| `CREATE TABLE` | 2 | `RoomPhotos`, `RoomRentalInquiries` |
| `CREATE INDEX` (inclui `UNIQUE`) | 6 | `IX_RoomPhotos_PrivateFileId`, `IX_RoomPhotos_Room_SortOrder`, `UX_RoomPhotos_Room_Cover` (único, parcial `WHERE "IsCover"`), `IX_RoomRentalInquiries_LeaseId`, `IX_RoomRentalInquiries_Room_CreatedAt`, `IX_RoomRentalInquiries_Status_CreatedAt` (parcial `WHERE "Status" = 'NEW'`) |
| `ALTER TABLE ... DROP CONSTRAINT` | 1 | `CK_PrivateFiles_Purpose` (removido para ser recriado com o novo valor) |
| `ALTER TABLE ... ADD CONSTRAINT` | 1 | `CK_PrivateFiles_Purpose` recriado como `"Purpose" IN ('PROFESSIONAL_PHOTO', 'ROOM_PHOTO')` |
| `FOREIGN KEY` (embutidas nos `CREATE TABLE`) | 4 | `RoomPhotos→PrivateFiles` (RESTRICT), `RoomPhotos→Rooms` (CASCADE), `RoomRentalInquiries→Leases`, `RoomRentalInquiries→Rooms` |
| `CHECK` (embutidas/alteradas) | 2 | `CK_RoomRentalInquiries_ConversionState` (novo, garante `LeaseId`/`ConvertedAt` consistentes com `Status`) + `CK_PrivateFiles_Purpose` (trocado) |
| `INSERT INTO` | 1 | Só a linha de bookkeeping em `__EFMigrationsHistory` (não é dado de aplicação) |
| `DROP TABLE` | **0** | confirmado ausente |
| `DROP COLUMN` | **0** | confirmado ausente |
| `UPDATE`/`DELETE` de dados de aplicação | **0** | confirmado ausente (o único `INSERT` é o de `__EFMigrationsHistory`) |
| Menção a tabelas `AspNet*` (Identity) | **0** | confirmado ausente |
| `ALTER TABLE` em `Rooms`, `Leases` ou `Professionals` | **0** | confirmado ausente — a migration só referencia essas tabelas como alvo de FK, nunca altera suas colunas |

**Confirmação explícita:** o script contém exclusivamente (1) a troca do `CHECK` de `PrivateFiles.Purpose` para
aceitar `ROOM_PHOTO`, (2) a criação de `RoomPhotos` e `RoomRentalInquiries` com seus FKs/CHECK/índices, e (3) a
linha de bookkeeping do próprio EF. Não há `DROP TABLE`, não há `DROP COLUMN`, não há alteração de coluna de
`Room`/`Lease`/`Professional`, não há tabela de identidade tocada, e não há DML de dados reais.

## Revisão de segurança e regressão (Step 5)

### `git diff --check` e `git status --porcelain -uall` (segunda rodada, pós-gates)

Ambos limpos antes deste commit — nenhuma alteração além deste runbook.

### `rg -n "FinanceiroPhoneNumber|ROOM_PHOTO|RoomRentalInquiry"`

Busca executada na raiz do worktree. Resultado: dezenas de ocorrências, todas revisadas; nenhuma expõe um segredo,
connection string ou número real. Resumo por categoria:

- **Configuração versionada** (`recepcaototem/appsettings.json`, `appsettings.Development.json`): `"Whatsapp": {
  "FinanceiroPhoneNumber": "" }` — string vazia, placeholder, como esperado. `appsettings.Production.json` não
  define a seção `Whatsapp` (herda vazio/config externa).
- **Fixtures de teste** (`ModulesApiFactory.cs`, `SecurityTests.cs`, `FoundationTests.cs`,
  `WhatsappConfigurationTests.cs`, `RoomRentalInquiryConversionTests.cs`, `RoomRentalInquiryAdminTests.cs`):
  `+5569999999999` — número **explicitamente fictício** (DDD 69 = Rondônia/Porto Velho, sequência de 9s), usado só
  em `ModulesApiFactory`/testes via `UseSetting`, nunca commitado como valor de produção. Consistente com a nota da
  spec: "ModulesApiFactory injeta `Whatsapp:FinanceiroPhoneNumber = +5569999999999`, explicitamente fictício."
- **Código de produção** (`WhatsappOptions.cs`, `WhatsappOptionsValidator.cs`, `RoomRentalInquiryEndpoints.cs`,
  `RoomRentalInquiryRateLimiter.cs`): só nomes de propriedades/chaves de configuração e lógica de validação/uso —
  nenhum valor literal de telefone.
- **`ROOM_PHOTO`**: só o literal de `Purpose` (constraint/model/migration/testes) e códigos de erro
  (`INVALID_ROOM_PHOTO`, `ROOM_PHOTO_LIMIT_REACHED`) — nenhum dado sensível.
- **`RoomRentalInquiry`**: só nomes de tipo/endpoint/DTO/teste em C# e TS (`api/modules.ts`) — nenhum dado real de
  cliente, já que são todos definições de contrato ou dados de teste sintéticos (`"Ana Silva"`, `"Clínica Ana"`
  etc.).
- **Documentação** (`docs/superpowers/specs/...`, `docs/superpowers/plans/...`): trechos da spec/plano já
  versionados antes desta task, sem segredo.

Nenhuma ocorrência de connection string, chave de API ou número de telefone real em qualquer arquivo versionado.

### Confirmações de autenticação/autorização (releitura do código atual, não da memória de tasks anteriores)

1. **Endpoints públicos do Totem com `AllowAnonymous()` explícito** — confirmado em
   `recepcaototem/Features/Totem/TotemRoomEndpoints.cs:16-18` (`GET /api/totem/rooms`, `GET
   /api/totem/rooms/{id}`, `GET /api/totem/rooms/{roomId}/photos/{photoId}`) e
   `recepcaototem/Features/Rooms/RoomRentalInquiryEndpoints.cs:18` (`POST
   /api/totem/rooms/{roomId}/rental-inquiries`).
2. **Mutações Admin com `RequireAuthorization("Operations")` + `AntiforgeryFilter`** — confirmado em
   `RoomEndpoints.cs:16` (grupo `Operations`) com `AddEndpointFilter<AntiforgeryFilter>()` em `Create`, `Update`,
   `Activate`, `Deactivate` (linhas 19-22); `RoomPhotoEndpoints.cs:13` (grupo `Operations`) com
   `AntiforgeryFilter` em upload, delete, reorder e set-cover (linhas 16-19); as leituras Admin de
   `RoomRentalInquiryEndpoints.cs:19-20` (`GET /api/admin/room-rental-inquiries[/{id}]`) são só
   `RequireAuthorization("Operations")`, sem `AntiforgeryFilter` — correto, são `GET`, não há mutação de inquiry
   por endpoint próprio.
3. **`POST` público de inquiry com rate limiter** — confirmado: `RoomRentalInquiryEndpoints.cs:33` chama
   `RoomRentalInquiryRateLimiter.AcquireAsync` (buckets por IP e por identificador de WhatsApp) antes de qualquer
   escrita, com `429 TOO_MANY_REQUESTS` se esgotado.
4. **Criação/conversão de Lease sob `Operations` + antiforgery** — confirmado em `LeaseEndpoints.cs:23` (grupo
   `/api/admin/leases` com `RequireAuthorization("Operations")`) e `LeaseEndpoints.cs:26`
   (`MapPost("", Create).AddEndpointFilter<AntiforgeryFilter>()`); é o mesmo `POST` que, quando recebe
   `RoomRentalInquiryId`, converte o inquiry na mesma transação — nenhum endpoint paralelo.
5. **DTOs públicos sem preço/contrato/tenant/profissional** — confirmado em `TotemRoomEndpoints.cs`: `PublicRoomCard(Id,
   Name, Description, Availability, AvailableFrom, CoverPhotoUrl?)` e `PublicRoomDetail(Id, Name, Description, Availability,
   AvailableFrom, PhotoUrls)`; e em `RoomRentalInquiryContracts.cs:13`: `RoomRentalInquiryResult(InquiryId,
   WhatsappUrl, PresentedAvailabilityLabel)`. Nenhum desses tipos carrega `Tenant`, `Professional`,
   `ContractedRate`, `HourlyRate` ou `DailyRate`.
6. **Nenhuma chamada de aplicação de migration no startup** — confirmado: busca por
   `MigrateAsync|Migrate\(\)|EnsureCreated` em `recepcaototem/` (incluindo `Program.cs`) não retornou nenhuma
   ocorrência. A aplicação nunca aplica migration ao iniciar, em nenhum ambiente.

## Sequência remota planejada (não executada, checkpoints de autorização)

Cada item abaixo exige autorização explícita e separada antes de ser executado; nenhum foi iniciado nesta task.

1. Confirmar backup recente do Supabase staging com restauração testada e validada.
2. Confirmar, no dashboard do Railway, que `Storage__PrivateFilesPath` aponta para um caminho dentro do volume
   persistente (não efêmero) antes de liberar upload de fotos de sala.
3. Confirmar que `Whatsapp__FinanceiroPhoneNumber` está configurado com um número real e válido no ambiente de
   staging — sem registrar o valor em nenhum lugar deste repositório.
4. Preflight de privilégios Supabase Data API: verificar se as roles `anon`/`authenticated` têm acesso de leitura
   às novas tabelas `RoomPhotos`/`RoomRentalInquiries` no schema `public`. **Não conceder nem revogar acesso
   automaticamente** — se houver exposição indevida, parar para decisão de segurança antes de prosseguir com o
   deploy. O frontend do Totem fala exclusivamente com o backend ASP.NET; nunca deve acessar o Supabase Data API
   diretamente.
5. Aplicar a migration `20260914054052_RoomPhotosAndRentalInquiries` via conexão de migração autorizada (Session
   pooler, conforme `docs/operations/staging-railway-runbook.md` TASK 5), comparando o SHA-256 do script gerado
   naquele momento com o valor registrado aqui antes de aplicar.
6. Verificar pós-aplicação: linha nova em `__EFMigrationsHistory`, existência de `RoomPhotos`/`RoomRentalInquiries`,
   `CK_PrivateFiles_Purpose` atualizado, índices esperados presentes.
7. Publicar o SHA do commit correspondente (deploy).
8. Checar `/health` e `/health/ready` (200 esperado em ambos).
9. Smoke manual: catálogo de salas, foto de sala, formulário de interesse (POST), fluxo de QR/WhatsApp, listagem e
   detalhe de interesses no Admin, conversão de um interesse em locação pelo modal existente.

### Rollback operacional

Em caso de problema após a aplicação em staging, o rollback é **por forward fix**, nunca por `Down` destrutivo:
qualquer correção necessária deve ser uma nova migration aditiva (a migration atual permanece a única migration
do plano). A única ação "para trás" aceitável é restaurar o backup validado no passo 1 — nunca executar `dotnet ef
database update <migration anterior>` nem qualquer `Down` manual depois que dados reais já tiverem sido gravados
nas tabelas novas. Isso é consistente com o runbook de `professionals-rooms-production-migration.md`, que também
proíbe `Down`/rollback improvisado após dados reais.

## Pendências para staging (aguardando autorização do usuário)

- `Storage__PrivateFilesPath` apontando para volume persistente do Railway.
- `Whatsapp__FinanceiroPhoneNumber` com número real configurado em staging.
- Preflight de grants do Supabase Data API para `RoomPhotos`/`RoomRentalInquiries`.
- Backup do Supabase staging confirmado e restauração testada.
- ~~Aplicação da migration `20260914054052_RoomPhotosAndRentalInquiries` em staging (via Session pooler).~~
  **Concluído** (atualização 2026-09-18): as 17 migrations estão aplicadas no staging, incluindo
  `AddDesiredDatesToRoomRentalInquiry` e `AddWhatsAppMessages`, confirmadas pelo usuário.
- ~~Verificação pós-migration (`__EFMigrationsHistory`, tabelas, checks, índices).~~ **Concluído** — validação feita
  pelo usuário no SQL Editor do Supabase staging.
- Push dos commits locais posteriores a `c4f5fe0`, deploy no Railway e publicação do SHA correspondente.
- Checagem de `/health` e `/health/ready` em staging.
- Smoke manual completo (catálogo, foto, formulário, QR/WhatsApp, Admin, conversão em locação).

Com exceção das migrations e da verificação pós-migration (marcadas acima como concluídas pelo usuário), nenhum
desses itens foi executado, e nenhum será executado sem autorização explícita e separada do usuário.

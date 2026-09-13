# Room Rental Flow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Entregar catálogo público de salas, galeria administrável e registro de interesse com continuação por QR/WhatsApp do Financeiro.

**Architecture:** Room + Lease calculam disponibilidade; RoomPhoto referencia PrivateFile e usa o storage/validador/normalizador existentes. RoomRentalInquiry guarda snapshot estruturado; backend monta a URL do WhatsApp após persistir. Interfaces públicas e Admin permanecem separadas, com uma única migration EF PostgreSQL.

**Tech Stack:** ASP.NET Core/.NET 10, EF Core/Npgsql, PostgreSQL, React + TypeScript + Vite, xUnit, Vitest/RTL, qrcode e ImageSharp existentes.

**Spec:** `docs/superpowers/specs/2026-09-13-room-rental-and-totem-carousel.md`, §§1, 5–14.

## Global Constraints

- Fonte de verdade arquitetural: spec acima; decisões aprovadas não são reabertas.
- Timezone oficial: `America/Porto_Velho`; usar TimeZoneInfo já registrado, não timezone do host/browser.
- Room → RoomPhoto → PrivateFile; `ROOM_PHOTO`; máximo 8 fotos.
- Reutilizar `IPrivateFileStorage`, `IProfessionalPhotoValidator`, `IImageNormalizer` sem renomear. ImageSharp **3.1.11**, nenhuma instalação/upgrade.
- Active e Scheduled bloqueiam; nenhum bloqueante → AVAILABLE_NOW; todos com fim → AVAILABLE_SOON, maior OccupancyEndAt + 1 dia civil; qualquer fim aberto → OCCUPIED, excluído do público.
- EndingPending/Ended/Cancelled não bloqueiam. Nenhum checkbox de disponibilidade, nenhuma mudança em Room/Lease.
- DTO público sem Tenant, Professional de contrato, ContractedRate, HourlyRate, DailyRate.
- Primeira foto é capa; havendo fotos, exatamente uma capa; exclusão não-capa preserva capa; exclusão da capa promove menor SortOrder remanescente; última exclusão deixa zero fotos/capa.
- SortOrder contínuo 0..N-1; reorder exige conjunto completo sem duplicata ou ID de outra sala; capa transacional.
- Interesse sem CPF, CNPJ ou Version; Status = New; Admin somente leitura.
- Snapshot persistido: PresentedAvailabilityStatus + PresentedAvailableFrom, nunca texto localizado.
- `Whatsapp__FinanceiroPhoneNumber`; número real nunca commitado ou hardcoded no React.
- Development vazio inicia normalmente e criação retorna 503 claro; Staging/Production exigem número válido no startup; testes usam configuração fictícia própria.
- QR deriva do whatsappUrl retornado, sem polling/status/expiração de handoff de agendamento.
- Públicos com AllowAnonymous explícito e sem AntiforgeryFilter; mutações Admin com Operations + AntiforgeryFilter.
- Requests JSON implementam IStrictModuleRequest. Reutilizar erros existentes; novos erros definidos neste plano limitados aos previstos na spec.
- Única migration futura: RoomPhotosAndRentalInquiries. Nesta etapa: só documentos e commits; não gerar migration, implementar, instalar, mudar ambiente, push, merge ou deploy.
- Execução futura pode gerar migration; aplicação remota, env vars e publicação exigem autorização explícita separada.

## Inspeção real e mapa de responsabilidades

Base: `codex/reception-backend`, worktree `.worktrees/reception-backend`, HEAD `16979ed7ce2b280d74a2c2a70aaffc444c641631`. Spec commitada nesse HEAD. Não usar o checkout principal em codex/leases-design nem main/master.

| Existente confirmado | Reuso |
|---|---|
| `src/GestaoPredio.Domain/Rooms/Room.cs`, `Leases/Lease.cs` | identidade/atividade e GetOperationalStatus(now) |
| `src/GestaoPredio.Domain/Files/PrivateFile.cs`, `PrivateFilePurposes.cs` | whitelist hoje só PROFESSIONAL_PHOTO |
| `src/GestaoPredio.Application/Abstractions/IPrivateFileStorage.cs` | StageAsync, OpenStagedReadAsync, CommitAsync, OpenReadAsync, DeleteAsync, DiscardAsync |
| `src/GestaoPredio.Application/Files/IProfessionalPhotoValidator.cs`, `IImageNormalizer.cs` | validação e WebP; saída atual 512×512 |
| `src/GestaoPredio.Infrastructure/Files/PrivateFileStorageOptions.cs`, `PrivateFileStorageOptionsValidator.cs` | limites e armazenamento existente |
| `recepcaototem/Features/Professionals/ProfessionalPhotoMutation.cs`, `ProfessionalPhotoStreaming.cs` | template real de multipart, cleanup e streaming; métodos específicos não aceitam Room |
| `src/GestaoPredio.Application/Leases/ILeaseResourceLock.cs`, `src/GestaoPredio.Infrastructure/Leases/PostgreSqlLeaseResourceLock.cs` | lock transacional de linha Room; reusar para serializar fotos e snapshots |
| `recepcaototem/Features/Customers/CustomerPublicRateLimiter.cs` | dois buckets IP/identificador SHA-256, leituras públicas |
| `recepcaototem/Features/Rooms/RoomContracts.cs`, `RoomEndpoints.cs` | requests estritos, paginação e autorização |
| `recepcaototem/Features/Common/StrictBody.cs`, `PagingQuery.cs`, `PagedResponse.cs`, `ApiError.cs` | contratos comuns |
| `recepcaototem/ClientApp/src/api/client.ts`, `modules.ts` | apiClient, postMultipart, paginação; post atual busca CSRF |
| `recepcaototem/ClientApp/src/pages/TotemHandoff.tsx`, `TotemHandoff.test.tsx` | QRCode.toDataURL, opções e mock existentes |
| `recepcaototem/ClientApp/src/pages/admin/Rooms.tsx`, `components/Modal.tsx` | cards Admin e modal large |
| `recepcaototem/ClientApp/src/components/AdminLayout.tsx`, `App.tsx` | navegação e rotas reais |
| `tests/GestaoPredio.IntegrationTests/ModulesApiFactory.cs`, `LocalPostgreSqlTestDatabase.cs` | schemas lumis_test_* em localhost:5432/LumisDev, user-secrets locais |
| `tests/GestaoPredio.IntegrationTests/MigrationSafetyTests.cs` | inspeção de migration sem conexão |
| `docs/operations/staging-railway-runbook.md` | procedimento histórico, não prova do estado remoto atual |

Correções factuais necessárias ao planejar:
1. `PrivateFileTests.cs` não existe: a cobertura atual está em `tests/GestaoPredio.UnitTests/EntityRuleTests.cs`; estender esse arquivo.
2. `Professional.NormalizeDescription` é privado. Reutilizar sua regra de trim/limite/rejeição de < e > em Note, sem tentar invocar método inacessível ou refatorar Professional.
3. apiClient.post sempre busca CSRF. Acrescentar apenas postPublic no cliente existente para esta escrita anônima; não criar um segundo cliente HTTP.
4. Streaming profissional recebe Professional. Criar adapter de streaming de RoomPhoto sobre o mesmo IPrivateFileStorage, sem fingir que aquele método recebe Room.
5. Fotos Admin precisam ser visíveis também para sala inativa/ocupada. Acrescentar GET autenticado de bytes sob a mesma coleção de fotos; usar URL Admin na galeria, URL pública apenas no catálogo.
6. “Capa primeiro” no detalhe público = OrderByDescending(IsCover).ThenBy(SortOrder); reorder Admin altera SortOrder e não muda capa.
7. Formatação única do snapshot fica no backend: o DTO Admin inclui campos estruturados e label calculado pelo mesmo RoomAvailabilityFormatter usado no POST e mensagem. React exibe esse label; não duplica a regra.
8. Sem alteração do carrossel neste plano. Pode ser desenvolvido sem aguardar sua homologação, mas prioridade operacional é entregar primeiro o Plano 1.

Todos os caminhos Create abaixo são arquivos **novos propostos**, não alegações de existência. Os arquivos Modify/Test existentes foram inspecionados. Namespaces novos acompanham suas pastas.

## Gates e convenções de execução

Comandos na raiz da worktree:
```powershell
git status --porcelain -uall
git branch --show-current
git rev-parse HEAD
git log -10 --oneline
dotnet build recepcaototem.sln
dotnet test tests/GestaoPredio.UnitTests/GestaoPredio.UnitTests.csproj
dotnet test tests/GestaoPredio.IntegrationTests/GestaoPredio.IntegrationTests.csproj
git diff --check
```

Gates frontend, em `recepcaototem/ClientApp`:
```powershell
npx vitest run
npx tsc -b
npx vite build
node scripts/verify-production-bundle.mjs
```

README também documenta `dotnet test recepcaototem.sln` (inclui AdminCli/DataMigration). Usá-lo no gate final, com isolamento local das integrações confirmado. Não executar testes contra Supabase. Não imprimir conexão/user-secrets.

Cada tarefa tem seis passos. RED por símbolo novo ausente é documentado como falha de compilação; após existir o contrato, o teste deve falhar na asserção do comportamento antes da implementação. Não tratar indisponibilidade de banco como RED funcional. Snippets usam os namespaces indicados; completar imports explícitos, sem helpers não definidos.

Dependências: 1 → 2 → 3 → 4; 2 → 5; 3+4 → 6; 1+3+6 → 7; 1+2+5+7 → 8; 7 → 9 → 10; 6 → 11; 1+3+8 → 12; 1–12 → 13. Os consumidores frontend usam contratos exatos abaixo. Regressões por tarefa são focadas; gate completo ao final, sem repetições sem motivo.

---

### Task 1: Disponibilidade pura e contrato público sem dados comerciais

**Files:**
- Create: `src/GestaoPredio.Domain/Rooms/PublicRoomAvailabilityStatus.cs`.
- Create: `src/GestaoPredio.Application/Rooms/RoomRentalAvailability.cs`.
- Create: `src/GestaoPredio.Application/Rooms/RoomAvailabilityCalculator.cs`.
- Create: `src/GestaoPredio.Application/Rooms/RoomAvailabilityFormatter.cs`.
- Create: `recepcaototem/Features/Totem/PublicRoomContracts.cs`.
- Modify: `recepcaototem/Program.cs` (conversor JSON somente do enum novo).
- Test: Create `tests/GestaoPredio.UnitTests/RoomAvailabilityTests.cs`, `tests/GestaoPredio.UnitTests/PublicRoomContractsTests.cs`.

**Interfaces:**
- Consumes: `Lease.GetOperationalStatus(DateTimeOffset now)`, `Lease.OccupancyEndAt`, TimeZoneInfo registrado.
- Produces: enum `GestaoPredio.Domain.Rooms.PublicRoomAvailabilityStatus { AvailableNow, AvailableSoon }`; record Application.Rooms `RoomRentalAvailability(PublicRoomAvailabilityStatus Status, DateOnly? AvailableFrom)`.
- Produces: `RoomAvailabilityCalculator.Calculate(IEnumerable<Lease> leases, DateTimeOffset now, TimeZoneInfo timeZone): RoomRentalAvailability?`; null representa OCCUPIED internamente.
- Produces: `RoomAvailabilityFormatter.Format(PublicRoomAvailabilityStatus status, DateOnly? availableFrom): string`.
- Produces: records `recepcaototem.Features.Totem.PublicRoomCard(Guid Id, string Name, string? Description, PublicRoomAvailabilityStatus Availability, DateOnly? AvailableFrom, string? CoverPhotoUrl)` e `PublicRoomDetail(Guid Id, string Name, string? Description, PublicRoomAvailabilityStatus Availability, DateOnly? AvailableFrom, IReadOnlyList<string> PhotoUrls)`.

- [ ] **Step 1: Escrever testes que falham.**

Em RoomAvailabilityTests, fixture de lease local válida (helper novo no próprio teste):
```csharp
private static Lease Contract(DateTimeOffset start, DateTimeOffset? end, DateTimeOffset now) =>
    Lease.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LeaseMode.Monthly,
        100m, start, 1, start, end, 1, now);

[Fact]
public void Consecutive_active_and_scheduled_use_latest_civil_end_plus_one_day()
{
    var now = DateTimeOffset.Parse("2026-09-13T12:00:00Z");
    var zone = OperationalTimeZone.Resolve("America/Porto_Velho");
    var leases = new[] {
        Contract(DateTimeOffset.Parse("2026-09-01T04:00:00Z"),
            DateTimeOffset.Parse("2026-10-01T02:00:00Z"), now),
        Contract(DateTimeOffset.Parse("2026-10-01T04:00:00Z"),
            DateTimeOffset.Parse("2026-11-16T02:00:00Z"), now)
    };
    var result = RoomAvailabilityCalculator.Calculate(leases, now, zone);
    Assert.Equal(PublicRoomAvailabilityStatus.AvailableSoon, result!.Status);
    Assert.Equal(new DateOnly(2026, 11, 16), result.AvailableFrom);
}
```

Casos independentes com mesmo helper: lista vazia → Now/null; Active finito → Soon; Scheduled finito → Soon; Active ou Scheduled indefinido junto de finito → null; Cancel antes de iniciar, MarkEnded e MarkEndingPending → ignorados; instante now == OccupancyEndAt → EndingPending e Now. A conversão civil evita usar 17/11 no exemplo UTC acima.

Em PublicRoomContractsTests:
```csharp
[Theory]
[InlineData(typeof(PublicRoomCard))]
[InlineData(typeof(PublicRoomDetail))]
public void Public_contract_excludes_private_and_price_fields(Type type)
{
    var names = type.GetProperties().Select(p => p.Name).ToArray();
    foreach (var forbidden in new[] { "Tenant", "TenantId", "Professional", "ProfessionalId",
        "ContractedRate", "HourlyRate", "DailyRate" })
        Assert.DoesNotContain(forbidden, names);
}
```

Testar formatter Now/null = “Disponível agora”; Soon/2026-11-16 = “Disponível em breve — a partir de 16/11/2026”; par inválido rejeitado.

- [ ] **Step 2: Rodar e confirmar RED.**

```powershell
dotnet test tests/GestaoPredio.UnitTests/GestaoPredio.UnitTests.csproj --filter "FullyQualifiedName~RoomAvailabilityTests|FullyQualifiedName~PublicRoomContractsTests"
```
Esperado: novos tipos ausentes; após contratos declarados, cálculo vazio deve falhar nos casos finitos/indefinidos.

- [ ] **Step 3: Implementar o mínimo.**

```csharp
public static RoomRentalAvailability? Calculate(IEnumerable<Lease> leases,
    DateTimeOffset now, TimeZoneInfo timeZone)
{
    var blocking = leases.Where(x => x.GetOperationalStatus(now)
        is LeaseOperationalStatus.Active or LeaseOperationalStatus.Scheduled).ToArray();
    if (blocking.Length == 0) return new(PublicRoomAvailabilityStatus.AvailableNow, null);
    if (blocking.Any(x => x.OccupancyEndAt is null)) return null;
    var end = blocking.Max(x => x.OccupancyEndAt!.Value);
    var civilEnd = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(end, timeZone).DateTime);
    return new(PublicRoomAvailabilityStatus.AvailableSoon, civilEnd.AddDays(1));
}
public static string Format(PublicRoomAvailabilityStatus status, DateOnly? availableFrom) =>
    (status, availableFrom) switch {
        (PublicRoomAvailabilityStatus.AvailableNow, null) => "Disponível agora",
        (PublicRoomAvailabilityStatus.AvailableSoon, { } date) =>
            $"Disponível em breve — a partir de {date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}",
        _ => throw new ArgumentException("Par de disponibilidade inválido.")
    };
```

Registrar em ConfigureHttpJsonOptions o conversor específico:
```csharp
options.SerializerOptions.Converters.Add(
    new JsonStringEnumConverter<PublicRoomAvailabilityStatus>(JsonNamingPolicy.SnakeCaseUpper, false));
```
Não mudar serialização global de enums existentes. DTOs usam camelCase JSON normal do projeto e strings AVAILABLE_NOW/AVAILABLE_SOON.

- [ ] **Step 4: Rodar e confirmar GREEN.** Repetir filtro; todos os casos passam.
- [ ] **Step 5: Rodar regressões.** UnitTests completo e dotnet build; LeaseTests/LeaseLifecycleTests preservados; git diff --check.
- [ ] **Step 6: Commit.**
```powershell
git add src/GestaoPredio.Domain/Rooms/PublicRoomAvailabilityStatus.cs src/GestaoPredio.Application/Rooms recepcaototem/Features/Totem/PublicRoomContracts.cs recepcaototem/Program.cs tests/GestaoPredio.UnitTests/RoomAvailabilityTests.cs tests/GestaoPredio.UnitTests/PublicRoomContractsTests.cs
git commit -m "feat(rooms): calculate public rental availability"
```

### Task 2: Entidades de fotos/interesses e extensão aditiva de PrivateFile

**Files:**
- Create: `src/GestaoPredio.Domain/Rooms/RoomPhoto.cs`, `src/GestaoPredio.Domain/Rooms/RoomRentalInquiry.cs`, `src/GestaoPredio.Domain/Rooms/RoomRentalInquiryStatus.cs`.
- Modify: `src/GestaoPredio.Domain/Files/PrivateFilePurposes.cs`, `src/GestaoPredio.Domain/Files/PrivateFile.cs`.
- Test: Modify `tests/GestaoPredio.UnitTests/EntityRuleTests.cs`.
- Test: Create `tests/GestaoPredio.UnitTests/RoomPhotoDomainTests.cs`, `tests/GestaoPredio.UnitTests/RoomRentalInquiryDomainTests.cs`.

**Interfaces:**
- Consumes: PublicRoomAvailabilityStatus Task 1, WhatsAppNormalizer.TryNormalize(string?, out string), TimestampNormalizer.ToUtcMicroseconds.
- Produces: `PrivateFilePurposes.RoomPhoto = "ROOM_PHOTO"`.
- Produces: RoomPhoto com Id/RoomId/PrivateFileId Guid, SortOrder int, IsCover bool, CreatedAt DateTimeOffset; `Attach(Guid roomId, Guid privateFileId, int sortOrder, bool isCover, DateTimeOffset occurredAt): RoomPhoto`, `Reorder(int): void`, `SetCover(bool): void`.
- Produces: RoomRentalInquiry com propriedades exatas da spec, getters/private setters; `Create(Guid roomId, string fullName, string whatsApp, string professionOrCompany, string? note, PublicRoomAvailabilityStatus presentedAvailabilityStatus, DateOnly? presentedAvailableFrom, DateTimeOffset occurredAt): RoomRentalInquiry`; enum `RoomRentalInquiryStatus { New = 1 }`.

- [ ] **Step 1: Escrever testes que falham.**

```csharp
[Fact]
public void Room_photo_purpose_is_allowed()
{
    var file = PrivateFile.Create("room-photo-test", "image/webp", 123,
        "ROOM_PHOTO", DateTimeOffset.UtcNow);
    Assert.Equal("ROOM_PHOTO", file.Purpose);
}
[Theory]
[InlineData("OTHER")]
[InlineData("room_photo")]
[InlineData("")]
public void Unknown_purpose_is_rejected(string purpose) =>
    Assert.Throws<ArgumentException>(() =>
        PrivateFile.Create("test", "image/webp", 123, purpose, DateTimeOffset.UtcNow));
```

Em testes novos: Attach com IDs válidos/ordem zero preserva dados; Guid.Empty e ordem negativa rejeitados; Reorder(-1) rejeitado; SetCover true/false funciona. Inquiry normaliza WhatsApp BR para E.164, trim dos textos, Note vazio → null; limites 200/200/500; falta de campo, telefone inválido, < ou > em Note rejeitados. Now com data ou Soon sem data rejeitados; nenhuma propriedade CPF/CNPJ/Version; Status New.

- [ ] **Step 2: Rodar e confirmar RED.**
```powershell
dotnet test tests/GestaoPredio.UnitTests/GestaoPredio.UnitTests.csproj --filter "FullyQualifiedName~EntityRuleTests|FullyQualifiedName~RoomPhotoDomainTests|FullyQualifiedName~RoomRentalInquiryDomainTests"
```
Esperado: whitelist rejeita ROOM_PHOTO; entidades ausentes.

- [ ] **Step 3: Implementar o mínimo.**

```csharp
public const string RoomPhoto = "ROOM_PHOTO";
// Dentro de PrivateFile.Create:
if (purpose is not (PrivateFilePurposes.ProfessionalPhoto or PrivateFilePurposes.RoomPhoto))
    throw new ArgumentException("A finalidade do arquivo não é permitida.", nameof(purpose));
```

RoomPhoto usa construtor privado e Attach preenchendo Guid.NewGuid/IDs/ordem/capa/data UTC; Reorder valida >=0, SetCover atribui bool. A unicidade de capa e limite dependem da coleção/transação, não do objeto isolado.

Na fábrica de Inquiry:
```csharp
var name = fullName?.Trim() ?? "";
var occupation = professionOrCompany?.Trim() ?? "";
var cleanNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
if (roomId == Guid.Empty || name.Length is < 1 or > 200 ||
    occupation.Length is < 1 or > 200 ||
    !WhatsAppNormalizer.TryNormalize(whatsApp, out var canonical) ||
    cleanNote is { Length: > 500 } || cleanNote?.Contains('<') == true ||
    cleanNote?.Contains('>') == true)
    throw new ArgumentException("Os dados do interesse são inválidos.");
if (!Enum.IsDefined(presentedAvailabilityStatus) ||
    (presentedAvailabilityStatus == PublicRoomAvailabilityStatus.AvailableNow && presentedAvailableFrom != null) ||
    (presentedAvailabilityStatus == PublicRoomAvailabilityStatus.AvailableSoon && presentedAvailableFrom == null))
    throw new ArgumentException("Par de disponibilidade inválido.");
```
Preencher exatamente Id novo, RoomId, FullName=name, WhatsApp=canonical, ProfessionOrCompany=occupation, Note=cleanNote, snapshot, Status=New, CreatedAt UTC microseconds. Sem campo de label na entidade.

- [ ] **Step 4: Rodar e confirmar GREEN.** Repetir filtro.
- [ ] **Step 5: Rodar regressões.** UnitTests completo; profissional e PrivateFile antigo continuam aceitos; git diff --check.
- [ ] **Step 6: Commit.**
```powershell
git add src/GestaoPredio.Domain/Rooms/RoomPhoto.cs src/GestaoPredio.Domain/Rooms/RoomRentalInquiry.cs src/GestaoPredio.Domain/Rooms/RoomRentalInquiryStatus.cs src/GestaoPredio.Domain/Files/PrivateFile.cs src/GestaoPredio.Domain/Files/PrivateFilePurposes.cs tests/GestaoPredio.UnitTests/EntityRuleTests.cs tests/GestaoPredio.UnitTests/RoomPhotoDomainTests.cs tests/GestaoPredio.UnitTests/RoomRentalInquiryDomainTests.cs
git commit -m "feat(rooms): add photo and rental inquiry domain"
```

### Task 3: Modelo EF e única migration RoomPhotosAndRentalInquiries

**Files:**
- Create: `src/GestaoPredio.Infrastructure/Persistence/Configurations/RoomPhotoConfiguration.cs`, `src/GestaoPredio.Infrastructure/Persistence/Configurations/RoomRentalInquiryConfiguration.cs`.
- Create: par migration/Designer gerado pelo EF em `src/GestaoPredio.Infrastructure/Persistence/Migrations/PostgreSql/`, sufixo `_RoomPhotosAndRentalInquiries` (timestamp somente ao gerar; não inventar hoje).
- Modify: `src/GestaoPredio.Infrastructure/Persistence/Configurations/PrivateFileConfiguration.cs`.
- Modify: `src/GestaoPredio.Infrastructure/Persistence/ApplicationDbContext.cs`.
- Modify: `src/GestaoPredio.Infrastructure/Persistence/Migrations/PostgreSql/ApplicationDbContextModelSnapshot.cs`.
- Modify: `tests/GestaoPredio.IntegrationTests/ModulesApiFactory.cs` (ordem do reset).
- Test: Create `tests/GestaoPredio.IntegrationTests/RoomRentalModelTests.cs`; Modify `tests/GestaoPredio.IntegrationTests/MigrationSafetyTests.cs`.

**Interfaces:**
- Consumes: entidades Tasks 1–2, DesignTimeDbContextFactory existente, Npgsql.
- Produces: `DbSet<RoomPhoto> RoomPhotos`, `DbSet<RoomRentalInquiry> RoomRentalInquiries`, schema e índices abaixo; migration ativa posterior a `20260910215630_TotemBookingHandoff`.

- [ ] **Step 1: Escrever testes que falham.**

Teste sem conexão:
```csharp
[Fact]
public void Room_rental_migration_contains_exactly_two_new_tables()
{
    using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
    var assembly = db.GetService<IMigrationsAssembly>();
    var metadata = Assert.Single(assembly.Migrations,
        x => x.Key.EndsWith("_RoomPhotosAndRentalInquiries", StringComparison.Ordinal));
    var migration = assembly.CreateMigration(metadata.Value, "Npgsql.EntityFrameworkCore.PostgreSQL");
    Assert.Equal(new[] { "RoomPhotos", "RoomRentalInquiries" },
        migration.UpOperations.OfType<CreateTableOperation>().Select(x => x.Name).Order().ToArray());
    var drop = Assert.Single(migration.UpOperations.OfType<DropCheckConstraintOperation>());
    Assert.Equal("PrivateFiles", drop.Table);
    Assert.Equal("CK_PrivateFiles_Purpose", drop.Name);
    Assert.DoesNotContain(migration.UpOperations, x => x is DropTableOperation or DropColumnOperation);
}
```

RoomRentalModelTests verifica FKs/índices/limites/conversões reais com db.Model; sem navegação para Tenant/Professional. Assert ausência de Version e coluna de label. Testar fim date nullable e CreatedAt timestamp with time zone. Gate de migração permite somente CreateTable/CreateIndex e Drop/Add do CHECK específico; proibir alterações em Room/Lease/Professional.

- [ ] **Step 2: Rodar e confirmar RED.**
```powershell
dotnet test tests/GestaoPredio.IntegrationTests/GestaoPredio.IntegrationTests.csproj --filter "FullyQualifiedName~RoomRentalModelTests|FullyQualifiedName~MigrationSafetyTests"
```
Esperado: migration/entidades não mapeadas. Não precisa de conexão para esses testes.

- [ ] **Step 3: Implementar configuração e gerar uma migration, somente na execução futura.**

Config RoomPhoto:
```csharp
entity.ToTable("RoomPhotos");
entity.HasKey(x => x.Id);
entity.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
entity.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Cascade);
entity.HasOne<PrivateFile>().WithMany().HasForeignKey(x => x.PrivateFileId).OnDelete(DeleteBehavior.Restrict);
entity.HasIndex(x => new { x.RoomId, x.SortOrder }).HasDatabaseName("IX_RoomPhotos_Room_SortOrder");
entity.HasIndex(x => x.RoomId).IsUnique().HasFilter("\"IsCover\"")
    .HasDatabaseName("UX_RoomPhotos_Room_Cover");
```

Config Inquiry: table/key; FullName/ProfessionOrCompany varchar(200), WhatsApp varchar(16), Note varchar(500) nullable; snapshot status varchar(20) com ValueConverter explícito AVAILABLE_NOW/AVAILABLE_SOON; PresentedAvailableFrom date nullable; Status varchar(10) NEW; CreatedAt timestamp with time zone; FK Room NoAction; índice `IX_RoomRentalInquiries_Room_CreatedAt` (RoomId,CreatedAt). Não mapear Version/label/CPF/CNPJ. Invalid enum é rejeitado antes da persistência.

PrivateFile CHECK:
```csharp
table.HasCheckConstraint("CK_PrivateFiles_Purpose",
    "\"Purpose\" IN ('PROFESSIONAL_PHOTO', 'ROOM_PHOTO')");
```
Adicionar DbSets e ApplyConfiguration explícitos (contexto não usa descoberta automática). ResetDatabaseAsync apaga RoomRentalInquiries e RoomPhotos antes de Rooms/PrivateFiles.

Comando confirmado pelo help local; executar só futuramente:
```powershell
dotnet ef migrations add RoomPhotosAndRentalInquiries --project src/GestaoPredio.Infrastructure --startup-project recepcaototem --context ApplicationDbContext --output-dir Persistence/Migrations/PostgreSql
```
DesignTimeDbContextFactory permite geração offline. Não aplicar remotamente; não gerar uma migration separada de Purpose. Inspecionar Up/Down/Designer/snapshot. Down não deve ser executado sobre dados de interesse/fotos.

- [ ] **Step 4: Rodar e confirmar GREEN.** Repetir filtro sem conexão; confirmar uma única migration nova e CHECK alterado.
- [ ] **Step 5: Rodar regressões.** Executar integração completa apenas após LocalPostgreSqlTestDatabase validar localhost:5432/LumisDev; fixture cria schema isolado e aplica migrations nele. Build e git diff --check.
- [ ] **Step 6: Commit.**
```powershell
git add src/GestaoPredio.Infrastructure/Persistence/Configurations/RoomPhotoConfiguration.cs src/GestaoPredio.Infrastructure/Persistence/Configurations/RoomRentalInquiryConfiguration.cs src/GestaoPredio.Infrastructure/Persistence/Configurations/PrivateFileConfiguration.cs src/GestaoPredio.Infrastructure/Persistence/ApplicationDbContext.cs src/GestaoPredio.Infrastructure/Persistence/Migrations/PostgreSql tests/GestaoPredio.IntegrationTests/ModulesApiFactory.cs tests/GestaoPredio.IntegrationTests/RoomRentalModelTests.cs tests/GestaoPredio.IntegrationTests/MigrationSafetyTests.cs
git commit -m "feat(rooms): persist gallery and rental inquiries"
```
Conferir staged: somente o par gerado e snapshot, nenhuma migration anterior editada.

### Task 4: Serviço transacional da galeria de fotos

**Files:**
- Create: `recepcaototem/Features/Rooms/RoomPhotoContracts.cs`.
- Create: `recepcaototem/Features/Rooms/RoomPhotoMutation.cs`.
- Modify: `src/GestaoPredio.Infrastructure/Files/PrivateFileStorageOptions.cs`, `src/GestaoPredio.Infrastructure/Files/PrivateFileStorageOptionsValidator.cs`.
- Modify: `recepcaototem/appsettings.json`, `recepcaototem/appsettings.Development.json`.
- Test: Create `tests/GestaoPredio.IntegrationTests/RoomPhotoTests.cs`; Modify `tests/GestaoPredio.UnitTests/PrivateFileStorageOptionsTests.cs`.

**Interfaces:**
- Consumes: `IPrivateFileStorage`, `IProfessionalPhotoValidator`, `IImageNormalizer`, `ILeaseResourceLock`, `PrivateFilePurposes.RoomPhoto`, `ApplicationDbContext.RoomPhotos`, TimeProvider.
- Produces: `RoomPhotoResponse(Guid Id, string PhotoUrl, int SortOrder, bool IsCover, DateTimeOffset CreatedAt)`; `ReorderRoomPhotosRequest(IReadOnlyList<Guid>? OrderedPhotoIds) : IStrictModuleRequest`.
- Produces: `RoomPhotoMutation.UploadAsync(...)`, `DeleteAsync(...)`, `ReorderAsync(...)`, `SetCoverAsync(...)`, todos retornando `Task<IResult>` e usados apenas pelos endpoints da Task 6.
- Produces: `PrivateFileStorageOptions.RoomPhotoMaxBytes`, default 5 MiB, máximo 10 MiB.

- [ ] **Step 1: Escrever testes que falham.**

Em `RoomPhotoTests`, preparar duas salas via `/api/admin/rooms`, login Operations e CSRF. Cobrir upload válido WebP normalizado; inválido/oversized sem arquivos/metadata/audit; primeira capa; segunda não troca capa; limite 8 retorna `ROOM_PHOTO_LIMIT_REACHED`; delete não-capa preserva capa e compacta; delete capa promove menor SortOrder; delete última zera; reorder válido produz 0..N-1 sem trocar capa; arrays incompleto, duplicado, vazio com fotos e ID de outra sala retornam `400 INVALID_ROOM_PHOTO` sem mutação; set-cover troca exatamente uma capa; sala inexistente retorna 404. O teste concorrente deve disparar duas operações sobre a mesma sala e, após ambas concluírem, verificar uma capa, nunca aceitar exceção 500/violação do índice.

Exemplo de invariante após chamadas reais:
```csharp
var photos = await db.RoomPhotos.AsNoTracking().Where(x => x.RoomId == room.Id)
    .OrderBy(x => x.SortOrder).ToListAsync();
Assert.Equal(Enumerable.Range(0, photos.Count), photos.Select(x => x.SortOrder));
Assert.Equal(photos.Count == 0 ? 0 : 1, photos.Count(x => x.IsCover));
```

Em `PrivateFileStorageOptionsTests`, 0 e >10 MiB em RoomPhotoMaxBytes falham; 5 MiB passa, além das asserções atuais de ProfessionalPhotoMaxBytes.

- [ ] **Step 2: Rodar e confirmar RED.**
```powershell
dotnet test tests/GestaoPredio.IntegrationTests/GestaoPredio.IntegrationTests.csproj --filter FullyQualifiedName~RoomPhotoTests
dotnet test tests/GestaoPredio.UnitTests/GestaoPredio.UnitTests.csproj --filter FullyQualifiedName~PrivateFileStorageOptionsTests
```
Esperado: contratos/serviço/opção ausentes. Se PostgreSQL local não estiver disponível, isso é bloqueio ambiental; iniciar o banco local pelo procedimento existente antes de interpretar o resultado.

- [ ] **Step 3: Implementar o mínimo.**

Reutilizar `ILeaseResourceLock`: toda mutação abre transação e chama `AcquireAsync(new LeaseResourceLockRequest([], [roomId], []), ct)` antes de contar/carregar fotos. O upload faz stage → valida → normaliza → stage normalizado → descarta original → commit storage; só então, dentro da transação/lock, confirma sala, conta <8, cria PrivateFile + RoomPhoto e audit. Em falha do banco, remover o storage recém-commitado; em sucesso, não expor StorageKey.

No upload:
```csharp
await using var transaction = await db.Database.BeginTransactionAsync(ct);
await resourceLock.AcquireAsync(new LeaseResourceLockRequest([], [roomId], []), ct);
var roomExists = await db.Rooms.AnyAsync(x => x.Id == roomId, ct);
if (!roomExists) { await transaction.RollbackAsync(ct); await DeleteSafely(storage, storageKey); return Results.NotFound(); }
var count = await db.RoomPhotos.CountAsync(x => x.RoomId == roomId, ct);
if (count >= 8) {
    await transaction.RollbackAsync(ct);
    await DeleteSafely(storage, storageKey);
    return Results.BadRequest(new ApiError("ROOM_PHOTO_LIMIT_REACHED",
        "Remova uma foto antes de adicionar outra."));
}
var file = PrivateFile.Create(storageKey, "image/webp", normalized.Length, PrivateFilePurposes.RoomPhoto, now);
var photo = RoomPhoto.Attach(roomId, file.Id, count, isCover: count == 0, now);
```

Definir no próprio `RoomPhotoMutation`, pois o helper profissional é privado:
```csharp
private static async Task DeleteSafely(IPrivateFileStorage storage, string storageKey)
{
    try { await storage.DeleteAsync(storageKey, CancellationToken.None); }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
}
```

Multipart aceita exatamente um campo `file`; não exige concurrencyToken de Room. Extraia parser próprio em RoomPhotoMutation e não altere a assinatura específica de ProfessionalPhotoMutation. Os códigos são `INVALID_ROOM_PHOTO`, `PHOTO_UNAVAILABLE`, `ROOM_PHOTO_LIMIT_REACHED`.

Delete, reorder e capa usam o mesmo lock da sala. Reorder primeiro valida `orderedPhotoIds != null`, contagem, unicidade e `SetEquals` com IDs carregados da sala; depois aplica posição. Para delete capa: remover a linha, compactar sobreviventes, definir a sobrevivente de menor ordem como capa dentro da mesma transação. Após commit, apagar arquivo físico; somente após sucesso físico remover a metadata PrivateFile em uma nova operação best-effort, espelhando cleanup profissional. Falha de cleanup é logada sem reverter a mutação já commitada. Auditar com ações exatas `ROOM_PHOTO_UPLOADED`, `ROOM_PHOTO_REMOVED`, `ROOM_PHOTOS_REORDERED` e `ROOM_PHOTO_COVER_CHANGED`; testes contam uma ação por mutação e garantem ausência de audit em falha.

Para set-cover, evitar a ordem indefinida de updates rastreados:
```csharp
await db.RoomPhotos.Where(x => x.RoomId == roomId && x.IsCover)
    .ExecuteUpdateAsync(set => set.SetProperty(x => x.IsCover, false), ct);
await db.RoomPhotos.Where(x => x.RoomId == roomId && x.Id == photoId)
    .ExecuteUpdateAsync(set => set.SetProperty(x => x.IsCover, true), ct);
await transaction.CommitAsync(ct);
```
O lock serializa operações da sala; o índice parcial impede duas capas. Uma transação não expõe o estado intermediário de zero capas.

Adicionar `RoomPhotoMaxBytes` ao validator. `ILeaseResourceLock` já está registrado como scoped em Program e não recebe segundo registro. Appsettings versionados usam 5242880; não alteram path nem credencial.

- [ ] **Step 4: Rodar e confirmar GREEN.** Repetir comandos; todos os casos passam, sem 500 em concorrência e sem artefatos órfãos nos casos inválidos.
- [ ] **Step 5: Rodar regressões.** Rodar ProfessionalPhotoTests, RoomPhotoDomainTests, PrivateFileStorageOptionsTests, integração completa local, build e git diff --check. Verificar que os arquivos normalizados continuam 512×512 WebP, conforme normalizador real reutilizado.
- [ ] **Step 6: Commit.**
```powershell
git add src/GestaoPredio.Infrastructure/Files/PrivateFileStorageOptions.cs src/GestaoPredio.Infrastructure/Files/PrivateFileStorageOptionsValidator.cs recepcaototem/Features/Rooms/RoomPhotoContracts.cs recepcaototem/Features/Rooms/RoomPhotoMutation.cs recepcaototem/appsettings.json recepcaototem/appsettings.Development.json tests/GestaoPredio.IntegrationTests/RoomPhotoTests.cs tests/GestaoPredio.UnitTests/PrivateFileStorageOptionsTests.cs
git commit -m "feat(rooms): add transactional room photo gallery"
```

### Task 5: Configuração validada e montagem do WhatsApp

**Files:**
- Create: `recepcaototem/Features/Rooms/WhatsappOptions.cs`, `recepcaototem/Features/Rooms/WhatsappOptionsValidator.cs`, `recepcaototem/Features/Rooms/WhatsappLinkBuilder.cs`.
- Modify: `recepcaototem/Program.cs`, `recepcaototem/appsettings.json`, `recepcaototem/appsettings.Development.json`.
- Modify: `tests/GestaoPredio.IntegrationTests/ModulesApiFactory.cs` (número fictício de teste).
- Test: Create `tests/GestaoPredio.UnitTests/WhatsappOptionsTests.cs`, `tests/GestaoPredio.UnitTests/WhatsappLinkBuilderTests.cs`.
- Test: Create `tests/GestaoPredio.IntegrationTests/WhatsappConfigurationTests.cs`.

**Interfaces:**
- Consumes: `WhatsAppNormalizer.TryNormalize`, `IOptions<WhatsappOptions>`, IWebHostEnvironment.
- Produces: `WhatsappOptions.SectionName = "Whatsapp"`, propriedade `FinanceiroPhoneNumber`.
- Produces: `WhatsappLinkBuilder.TryBuild(string configuredPhone, string message, out string url): bool`; URL `https://wa.me/{digits}?text={Uri.EscapeDataString(message)}`.
- Produces: erro runtime `ROOM_RENTAL_WHATSAPP_NOT_CONFIGURED` 503 em Development vazio; validação de start em Staging/Production.

- [ ] **Step 1: Escrever testes que falham.**

Unit: vazio é permitido em Development/Testing, recusado em Staging/Production; BR formatado e E.164 válidos normalizam; inválido falha; builder remove `+`, codifica quebras, acentos, `&`, `?` e não inclui telefone cru fora de `wa.me/`.
```csharp
[Fact]
public void Builder_encodes_exact_message()
{
    Assert.True(WhatsappLinkBuilder.TryBuild("+5569999999999", "Olá!\nSala: A & B", out var url));
    var uri = new Uri(url);
    Assert.Equal("wa.me", uri.Host);
    Assert.Equal("/5569999999999", uri.AbsolutePath);
    Assert.Equal("Olá!\nSala: A & B", Uri.UnescapeDataString(uri.Query[6..]));
}
```
Integração: factory derivada com `UseEnvironment("Staging")` + valor vazio falha na resolução inicial de opções; Testing com fictício sobe; Development vazio sobe e endpoint helper da Task 8 posteriormente devolve 503 (aqui testar validator diretamente para não depender da Task 8).

- [ ] **Step 2: Rodar e confirmar RED.**
```powershell
dotnet test tests/GestaoPredio.UnitTests/GestaoPredio.UnitTests.csproj --filter "FullyQualifiedName~WhatsappOptionsTests|FullyQualifiedName~WhatsappLinkBuilderTests"
dotnet test tests/GestaoPredio.IntegrationTests/GestaoPredio.IntegrationTests.csproj --filter FullyQualifiedName~WhatsappConfigurationTests
```
Esperado: tipos e binding ausentes.

- [ ] **Step 3: Implementar o mínimo.**

O validator recebe environmentName. Vazio retorna Success somente em Development/Testing; valor presente sempre precisa passar WhatsAppNormalizer. Registrar IValidateOptions e options bind. Chamar ValidateOnStart apenas fora de Development/Testing; o próprio validator ainda protege valor inválido quando Options é acessado.
```csharp
var whatsapp = builder.Services.AddOptions<WhatsappOptions>()
    .Bind(builder.Configuration.GetSection(WhatsappOptions.SectionName));
if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
    whatsapp.ValidateOnStart();
```
Appsettings ambos:
```json
"Whatsapp": { "FinanceiroPhoneNumber": "" }
```
ModulesApiFactory injeta `Whatsapp:FinanceiroPhoneNumber = +5569999999999`, explicitamente fictício. Builder devolve false em vazio/inválido e nunca loga valor/mensagem/URL.

- [ ] **Step 4: Rodar e confirmar GREEN.** Repetir os dois comandos; conferir mensagens de validação sem número exposto.
- [ ] **Step 5: Rodar regressões.** Unit/integration completas e FoundationTests de ambiente; start Development com appsettings vazio; build e diff check.
- [ ] **Step 6: Commit.**
```powershell
git add recepcaototem/Features/Rooms/WhatsappOptions.cs recepcaototem/Features/Rooms/WhatsappOptionsValidator.cs recepcaototem/Features/Rooms/WhatsappLinkBuilder.cs recepcaototem/Program.cs recepcaototem/appsettings.json recepcaototem/appsettings.Development.json tests/GestaoPredio.IntegrationTests/ModulesApiFactory.cs tests/GestaoPredio.UnitTests/WhatsappOptionsTests.cs tests/GestaoPredio.UnitTests/WhatsappLinkBuilderTests.cs tests/GestaoPredio.IntegrationTests/WhatsappConfigurationTests.cs
git commit -m "feat(rooms): configure finance WhatsApp handoff"
```

### Task 6: Endpoints Admin de fotos e streaming protegido

**Files:**
- Create: `recepcaototem/Features/Rooms/RoomPhotoEndpoints.cs`, `recepcaototem/Features/Rooms/RoomPhotoStreaming.cs`.
- Modify: `recepcaototem/Program.cs`.
- Test: Modify `tests/GestaoPredio.IntegrationTests/RoomPhotoTests.cs`, `tests/GestaoPredio.IntegrationTests/AntiforgeryTests.cs`, `tests/GestaoPredio.IntegrationTests/SecurityTests.cs`.

**Interfaces:**
- Consumes: RoomPhotoMutation/Response Task 4, IPrivateFileStorage, ApplicationDbContext, Operations, AntiforgeryFilter.
- Produces: `MapRoomPhotoEndpoints(IEndpointRouteBuilder): IEndpointRouteBuilder` e seis rotas Admin exatas listadas abaixo.
- Produces: `RoomPhotoStreaming.StreamAsync(RoomPhoto photo, ..., string cacheControl, bool publicFailureIsNotFound, CancellationToken): Task<IResult>`.

- [ ] **Step 1: Escrever testes que falham.**

Cobrir auth/CSRF e verbo/rota:
- GET `/api/admin/rooms/{roomId}/photos` → lista ordenada, Operations.
- GET `/api/admin/rooms/{roomId}/photos/{photoId}` → bytes Admin, private/no-store, Operations.
- POST `/api/admin/rooms/{roomId}/photos` → multipart file, Operations + CSRF.
- DELETE `/api/admin/rooms/{roomId}/photos/{photoId}` → Operations + CSRF.
- PUT `/api/admin/rooms/{roomId}/photos/reorder` → request estrito, Operations + CSRF.
- POST `/api/admin/rooms/{roomId}/photos/{photoId}/cover` → Operations + CSRF.

Validar que Manager/Admin passam Operations e Professional recebe 403. Verificar request reorder com membro JSON extra → 400. Foto de outra sala em URL → 404. Sala inativa continua visível na rota Admin.

- [ ] **Step 2: Rodar e confirmar RED.**
```powershell
dotnet test tests/GestaoPredio.IntegrationTests/GestaoPredio.IntegrationTests.csproj --filter "FullyQualifiedName~RoomPhotoTests|FullyQualifiedName~AntiforgeryTests|FullyQualifiedName~SecurityTests"
```
Esperado: 404/catch-all autenticado ou rotas ausentes.

- [ ] **Step 3: Implementar o mínimo.**

Mapear grupo Admin `/api/admin/rooms/{roomId:guid}/photos` com RequireAuthorization("Operations"); adicionar AntiforgeryFilter somente POST upload, DELETE, PUT reorder e POST cover. O GET de bytes usa o mesmo path de DELETE, distinguido por verbo. `RoomPhotoResponse.PhotoUrl` aponta para o GET Admin e inclui `?v={PrivateFileId}`. A rota pública nasce completa, com limiter, na Task 7.

```csharp
var group = endpoints.MapGroup("/api/admin/rooms/{roomId:guid}/photos")
    .RequireAuthorization("Operations");
group.MapGet("", List);
group.MapGet("/{photoId:guid}", Content);
group.MapPost("", Upload).AddEndpointFilter<AntiforgeryFilter>();
group.MapDelete("/{photoId:guid}", Delete).AddEndpointFilter<AntiforgeryFilter>();
group.MapPut("/reorder", Reorder).AddEndpointFilter<AntiforgeryFilter>();
group.MapPost("/{photoId:guid}/cover", SetCover).AddEndpointFilter<AntiforgeryFilter>();
```

Streaming carrega PrivateFile por photo.PrivateFileId, exige purpose ROOM_PHOTO, valida stream seek/length, define ContentDisposition inline, CacheControl passado e X-Content-Type-Options nosniff. Erro Admin de storage é 503 PHOTO_UNAVAILABLE e loga apenas IDs/correlation; público devolve 404 para não revelar metadata, conforme padrão de profissional.

```csharp
context.Response.Headers.ContentDisposition = "inline";
context.Response.Headers.CacheControl = cacheControl;
context.Response.Headers.XContentTypeOptions = "nosniff";
return Results.Stream(stream, metadata.MimeType, enableRangeProcessing: false);
```

Em Program, chamar `app.MapRoomPhotoEndpoints()` antes do catch-all. O streaming aceita a política de falha/cache por parâmetro para a Task 7 reutilizar, mas nesta tarefa só é chamado pela rota autenticada.

- [ ] **Step 4: Rodar e confirmar GREEN.** Repetir filtro; conferir headers e que nenhuma resposta contém StorageKey/path.
- [ ] **Step 5: Rodar regressões.** ProfessionalPhotoTests, StrictModuleContractsTests, integração completa local, build e diff check.
- [ ] **Step 6: Commit.**
```powershell
git add recepcaototem/Features/Rooms/RoomPhotoEndpoints.cs recepcaototem/Features/Rooms/RoomPhotoStreaming.cs recepcaototem/Program.cs tests/GestaoPredio.IntegrationTests/RoomPhotoTests.cs tests/GestaoPredio.IntegrationTests/AntiforgeryTests.cs tests/GestaoPredio.IntegrationTests/SecurityTests.cs
git commit -m "feat(rooms): expose protected room photo management"
```

### Task 7: Catálogo público calculado por Room + Lease

**Files:**
- Create: `recepcaototem/Features/Totem/TotemRoomEndpoints.cs`.
- Modify: `recepcaototem/Program.cs`, `recepcaototem/Features/Rooms/RoomPhotoEndpoints.cs` (delegar rota pública ao novo módulo).
- Test: Create `tests/GestaoPredio.IntegrationTests/PublicRoomCatalogTests.cs`; Modify `tests/GestaoPredio.IntegrationTests/SecurityTests.cs`.

**Interfaces:**
- Consumes: PublicRoomCard/Detail, RoomAvailabilityCalculator, CustomerPublicRateLimiter, Room/Lease/RoomPhoto/PrivateFile, TimeProvider e TimeZoneInfo.
- Produces: `MapTotemRoomEndpoints(IEndpointRouteBuilder): IEndpointRouteBuilder`.
- Produces: GET `/api/totem/rooms`; GET `/api/totem/rooms/{id:guid}`; GET `/api/totem/rooms/{id:guid}/photos/{photoId:guid}`.

- [ ] **Step 1: Escrever testes que falham.**

Sem leases → Now; Active/Scheduled finitos → Soon com maior fim +1 dia civil; bloqueante aberto/inativa → omitida da lista e 404 detalhe/foto; EndingPending/Ended/Cancelled ignorados. Lista ordena NormalizedName/Id. Detalhe ordena capa primeiro e demais por SortOrder; lista usa cover URL ou null. DTO serializado nunca contém tenant/professional/contractedRate/hourlyRate/dailyRate/StorageKey/PrivateFileId. Limiter esgotado → 429 TOO_MANY_REQUESTS. GETs funcionam anônimos e não caem no catch-all.

```csharp
var json = await response.Content.ReadAsStringAsync();
foreach (var forbidden in new[] { "tenant", "professional", "contractedRate", "hourlyRate", "dailyRate", "storageKey", "privateFileId" })
    Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
```

- [ ] **Step 2: Rodar e confirmar RED.**
```powershell
dotnet test tests/GestaoPredio.IntegrationTests/GestaoPredio.IntegrationTests.csproj --filter "FullyQualifiedName~PublicRoomCatalogTests|FullyQualifiedName~SecurityTests"
```
Esperado: rotas ausentes/catch-all e 401/404.

- [ ] **Step 3: Implementar o mínimo.**

Carregar apenas salas ativas. Para lista, carregar leases das salas candidatas em uma consulta e agrupar em memória; calcular com now/TimeZoneInfo; filtrar null. Carregar fotos capa em consulta separada e montar URL versionada. Para detalhe, buscar Room ativa, leases só da sala, calcular e 404 em null; fotos ordenadas:
```csharp
endpoints.MapGet("/api/totem/rooms", List).AllowAnonymous();
endpoints.MapGet("/api/totem/rooms/{id:guid}", Detail).AllowAnonymous();
endpoints.MapGet("/api/totem/rooms/{roomId:guid}/photos/{photoId:guid}", Photo).AllowAnonymous();

var ordered = photos.OrderByDescending(x => x.IsCover).ThenBy(x => x.SortOrder)
    .Select(x => $"/api/totem/rooms/{room.Id}/photos/{x.Id}?v={x.PrivateFileId}").ToArray();
```
Nenhum Include de Tenant/Professional. Antes de cada resposta/stream, adquirir CustomerPublicRateLimiter por IP e identificador `rooms`, `room:{id}` ou `room-photo:{roomId}:{photoId}`; falha retorna TOO_MANY_REQUESTS 429. Todas as rotas têm AllowAnonymous. Mover o mapeamento público criado na Task 6 para este módulo, sem duplicar rota. Chamar MapTotemRoomEndpoints antes do catch-all.

- [ ] **Step 4: Rodar e confirmar GREEN.** Repetir filtro; congelar relógio da factory nos testes para resultados determinísticos.
- [ ] **Step 5: Rodar regressões.** TotemProfessionalsCarouselTests e módulos públicos existentes; integração completa local; query logging assegura número limitado de consultas (não N+1); build/diff check.
- [ ] **Step 6: Commit.**
```powershell
git add recepcaototem/Features/Totem/TotemRoomEndpoints.cs recepcaototem/Features/Rooms/RoomPhotoEndpoints.cs recepcaototem/Program.cs tests/GestaoPredio.IntegrationTests/PublicRoomCatalogTests.cs tests/GestaoPredio.IntegrationTests/SecurityTests.cs
git commit -m "feat(totem): add public room catalog endpoints"
```

### Task 8: Criar interesse público, snapshot e rate limiting

**Files:**
- Create: `recepcaototem/Features/Rooms/RoomRentalInquiryContracts.cs`, `recepcaototem/Features/Rooms/RoomRentalInquiryRateLimiter.cs`, `recepcaototem/Features/Rooms/RoomRentalInquiryEndpoints.cs`.
- Modify: `recepcaototem/Program.cs`, `recepcaototem/appsettings.json`, `recepcaototem/appsettings.Development.json`.
- Modify: `tests/GestaoPredio.IntegrationTests/ModulesApiFactory.cs` (limites altos de teste).
- Test: Create `tests/GestaoPredio.UnitTests/RoomRentalInquiryRateLimiterTests.cs`.
- Test: Create `tests/GestaoPredio.IntegrationTests/RoomRentalInquiryTests.cs`; Modify `tests/GestaoPredio.IntegrationTests/StrictModuleContractsTests.cs`, `tests/GestaoPredio.IntegrationTests/SecurityTests.cs`.

**Interfaces:**
- Consumes: RoomAvailabilityCalculator/Formatter, RoomRentalInquiry.Create, WhatsappOptions/LinkBuilder, WhatsAppNormalizer, ILeaseResourceLock, ApplicationDbContext, TimeProvider/TimeZoneInfo.
- Produces: `RoomRentalInquiryRequest(string? FullName, string? WhatsApp, string? ProfessionOrCompany, string? Note) : IStrictModuleRequest`.
- Produces: `RoomRentalInquiryResult(Guid InquiryId, string WhatsappUrl, string PresentedAvailabilityLabel)`.
- Produces: `RoomRentalInquiryInput.TryValidate(..., out ValidRoomRentalInquiryInput?, out ApiError?)` com nome 200, profissão/empresa 200, Note 500 e sem `<`/`>`, telefone normalizado.
- Produces: `RoomRentalInquiryRateLimiter.AcquireAsync(string ipAddress, string identifierValue, CancellationToken): ValueTask<RoomRentalInquiryRateLimitLease>`.
- Produces: POST `/api/totem/rooms/{roomId:guid}/rental-inquiries`, AllowAnonymous, sem antiforgery.

- [ ] **Step 1: Escrever testes que falham.**

Unit do limiter: IP e identificador têm buckets independentes, identificador armazenado pelo hash SHA-256, janela renova e limites inválidos são elevados a 1 conforme padrão existente. Integração: válido em Now e Soon persiste snapshot real, Status New e Audit `ROOM_RENTAL_INQUIRY_CREATED`; request não contém roomId/status/data; resposta tem URL e label. Decodificar texto e comparar integralmente:
```csharp
var expected = $"Olá! Tenho interesse em alugar uma sala na Lumis.\n\n" +
    $"Sala: Sala 101\nDisponibilidade: Disponível agora\nNome: Ana Souza\n" +
    $"WhatsApp: +5569999999999\nProfissão/Empresa: Clínica A\nObservação: —";
Assert.Equal(expected, Uri.UnescapeDataString(new Uri(result.WhatsappUrl).Query[6..]));
```

Campos ausentes, > limites, WhatsApp inválido e HTML na Note → 400 INVALID_ROOM_RENTAL_INQUIRY e zero rows/audit. Sala inexistente/inativa/OCCUPIED → 404. Campo JSON extra → 400. Tentativa de enviar availability/roomId → 400 por request estrito. Config vazia em Development → 503 ROOM_RENTAL_WHATSAPP_NOT_CONFIGURED e não persiste. Limite por IP ou telefone → 429 TOO_MANY_REQUESTS. Congelar relógio e provar que alteração de leases antes do POST muda o snapshot; cliente não decide status. Captura de logs não contém nome, WhatsApp, Note, mensagem ou URL.

- [ ] **Step 2: Rodar e confirmar RED.**
```powershell
dotnet test tests/GestaoPredio.UnitTests/GestaoPredio.UnitTests.csproj --filter FullyQualifiedName~RoomRentalInquiryRateLimiterTests
dotnet test tests/GestaoPredio.IntegrationTests/GestaoPredio.IntegrationTests.csproj --filter "FullyQualifiedName~RoomRentalInquiryTests|FullyQualifiedName~StrictModuleContractsTests|FullyQualifiedName~SecurityTests"
```
Esperado: tipos/rota ausentes.

- [ ] **Step 3: Implementar o mínimo.**

Limiter usa defaults exatos: IP 8, identificador 3, janela 600 s; ambos fixed-window, QueueLimit 0, AutoReplenishment true. Program registra singleton; appsettings versionados registram os três valores. ModulesApiFactory usa 10000/10000/600.

Handler: adquirir limiter primeiro; para identificador, usar telefone normalizado quando válido ou valor trimado quando inválido. Validar body. Ler WhatsappOptions e devolver 503 antes de persistir se builder falhar. Abrir transação, adquirir lock de Room via ILeaseResourceLock com somente roomId, carregar Room ativa e leases, recalcular disponibilidade; null → 404. Criar entidade + Audit com ActorUserId null, IpAddress, correlation, target inquiry. Salvar/commit. Só então responder com URL já calculada.

Mensagem construída pelo backend, usando o mesmo formatter:
```csharp
var label = RoomAvailabilityFormatter.Format(availability.Status, availability.AvailableFrom);
var message = $"Olá! Tenho interesse em alugar uma sala na Lumis.\n\n" +
    $"Sala: {room.Name}\nDisponibilidade: {label}\nNome: {input.FullName}\n" +
    $"WhatsApp: {input.WhatsApp}\nProfissão/Empresa: {input.ProfessionOrCompany}\n" +
    $"Observação: {input.Note ?? "—"}";
```
O endpoint não loga request/URL. Mapear antes do catch-all, AllowAnonymous explícito, sem AntiforgeryFilter. Status retornado 200 porque o resultado é uma continuação, e inquiryId identifica o registro; documentar essa escolha no teste.

- [ ] **Step 4: Rodar e confirmar GREEN.** Repetir comandos; conferir estado direto no DbContext e URL decodificada.
- [ ] **Step 5: Rodar regressões.** RoomAvailabilityTests, PublicRoomCatalogTests, Login/Customer limiters, integração completa local, build e diff check.
- [ ] **Step 6: Commit.**
```powershell
git add recepcaototem/Features/Rooms/RoomRentalInquiryContracts.cs recepcaototem/Features/Rooms/RoomRentalInquiryRateLimiter.cs recepcaototem/Features/Rooms/RoomRentalInquiryEndpoints.cs recepcaototem/Program.cs recepcaototem/appsettings.json recepcaototem/appsettings.Development.json tests/GestaoPredio.IntegrationTests/ModulesApiFactory.cs tests/GestaoPredio.UnitTests/RoomRentalInquiryRateLimiterTests.cs tests/GestaoPredio.IntegrationTests/RoomRentalInquiryTests.cs tests/GestaoPredio.IntegrationTests/StrictModuleContractsTests.cs tests/GestaoPredio.IntegrationTests/SecurityTests.cs
git commit -m "feat(rooms): accept rate-limited rental inquiries"
```

### Task 9: Catálogo público React e entrada “Alugar sala”

**Files:**
- Create: `recepcaototem/ClientApp/src/pages/TotemRoomsCatalog.tsx`.
- Modify: `recepcaototem/ClientApp/src/api/modules.ts`, `recepcaototem/ClientApp/src/App.tsx`, `recepcaototem/ClientApp/src/pages/TotemProfessionals.tsx`, `recepcaototem/ClientApp/src/styles.css`.
- Test: Create `recepcaototem/ClientApp/src/pages/TotemRoomsCatalog.test.tsx`; Modify `recepcaototem/ClientApp/src/pages/TotemProfessionals.test.tsx`, `recepcaototem/ClientApp/src/api/modules.test.ts`.

**Interfaces:**
- Consumes: GET `/api/totem/rooms`; ApiClient.get; PublicRoomCard JSON com `availability: 'AVAILABLE_NOW'|'AVAILABLE_SOON'`.
- Produces: TS `PublicRoomAvailability`, `PublicRoomCardDto`, `PublicRoomDetailDto`, `RoomRentalInquiryInput`, `RoomRentalInquiryResultDto`.
- Produces: `totemRoomApi.list(signal?): Promise<PublicRoomCardDto[]>`, `detail(id, signal?)`, `createInquiry(id,input)`; createInquiry será ligado na Task 10.
- Produces: rota `/totem/salas`; botão Alugar sala navega para ela.

- [ ] **Step 1: Escrever testes que falham.**

Mockar `totemRoomApi.list`. Testar loading, retry em erro, vazio, AbortError silencioso e dois grupos. Now aparece primeiro; Soon ordena por availableFrom/nome e mostra “Disponível em breve — a partir de DD/MM/AAAA”. Card sem foto usa fallback visual e descrição. Nenhuma tarifa aparece. Clique no card navega `/totem/salas/{id}`. Botões voltar → `/totem`; CTA existente fica habilitada e navega `/totem/salas`.
```tsx
expect(await screen.findByRole('heading', { name: 'Disponíveis agora' })).toBeInTheDocument()
expect(screen.getByText('Disponível em breve — a partir de 16/11/2026')).toBeInTheDocument()
expect(screen.queryByText(/por hora|diária|R\$/i)).not.toBeInTheDocument()
```
modules.test verifica paths escapados e que list/detail usam GET.

- [ ] **Step 2: Rodar e confirmar RED.**
```powershell
npx vitest run src/pages/TotemRoomsCatalog.test.tsx src/pages/TotemProfessionals.test.tsx src/api/modules.test.ts
```
Esperado: módulo, página e rota ausentes; CTA disabled.

- [ ] **Step 3: Implementar o mínimo.**

Adicionar contratos/funções no módulo existente, sem criar cliente paralelo. Em App, importar página e mapear rota pública antes do wildcard. Em TotemProfessionals, remover comentário “Em breve”, disabled/title e usar `onClick={() => navigate('/totem/salas')}`.

Catalog carrega com AbortController. Agrupa por availability, formata DateOnly `YYYY-MM-DD` sem `new Date` para evitar timezone:
```tsx
function dateLabel(value: string) {
  const [year, month, day] = value.split('-')
  return `${day}/${month}/${year}`
}
```
Renderizar seção somente quando tem itens; vazio global tem voltar/tentar novamente. A imagem usa coverPhotoUrl do backend, alt descritivo da sala e fallback após onError. CSS segue identidade escura e responsiva; não toca classes do carrossel do Plano 1.

- [ ] **Step 4: Rodar e confirmar GREEN.** Repetir comando; nenhuma tarifa ou mock hardcoded aparece.
- [ ] **Step 5: Rodar regressões.** Vitest completo, tsc, vite build, verify-production-bundle e diff check; testar 390×844 e 1920×1080.
- [ ] **Step 6: Commit.**
```powershell
git add recepcaototem/ClientApp/src/pages/TotemRoomsCatalog.tsx recepcaototem/ClientApp/src/pages/TotemRoomsCatalog.test.tsx recepcaototem/ClientApp/src/api/modules.ts recepcaototem/ClientApp/src/api/modules.test.ts recepcaototem/ClientApp/src/App.tsx recepcaototem/ClientApp/src/pages/TotemProfessionals.tsx recepcaototem/ClientApp/src/pages/TotemProfessionals.test.tsx recepcaototem/ClientApp/src/styles.css
git commit -m "feat(totem): add public room catalog"
```

### Task 10: Detalhe, formulário e tela estática de QR/WhatsApp

**Files:**
- Create: `recepcaototem/ClientApp/src/pages/TotemRoomDetail.tsx`, `recepcaototem/ClientApp/src/pages/TotemRoomInterestSuccess.tsx`.
- Modify: `recepcaototem/ClientApp/src/api/client.ts`, `recepcaototem/ClientApp/src/api/modules.ts`, `recepcaototem/ClientApp/src/App.tsx`, `recepcaototem/ClientApp/src/styles.css`.
- Test: Create `recepcaototem/ClientApp/src/pages/TotemRoomDetail.test.tsx`, `recepcaototem/ClientApp/src/pages/TotemRoomInterestSuccess.test.tsx`; Modify `recepcaototem/ClientApp/src/api/client.test.ts`.

**Interfaces:**
- Consumes: detail e POST inquiry Tasks 7–9, ApiError, qrcode default export e opções existentes `{ margin:1,width:320,color:{dark:'#181818',light:'#ffffff'} }`.
- Produces: `apiClient.postPublic<T>(path:string, body:unknown): Promise<T>` sem CSRF; `totemRoomApi.createInquiry` passa a usá-lo.
- Produces: rotas `/totem/salas/:id` e `/totem/salas/:id/interesse`.
- Produces: `RoomInterestNavState { roomName:string; whatsappUrl:string; presentedAvailabilityLabel:string }` mantido só em navigation state.

- [ ] **Step 1: Escrever testes que falham.**

client.test: postPublic envia JSON/Content-Type/same-origin, não chama `/api/auth/csrf`, decodifica ApiError. Detail: loading/erro/404, galeria segue photoUrls na ordem, seletor de miniatura, sem tarifas; label Now/Soon; “Tenho interesse” abre formulário; Nome/WhatsApp/Profissão obrigatórios, Note opcional; erro 400 inline; submit único enquanto pending; sucesso navega para rota interesse com state exato.

Success usa mock qrcode do padrão TotemHandoff e exige o título aprovado `Continue no WhatsApp`:
```tsx
expect(screen.getByRole('heading', { name: 'Continue no WhatsApp' })).toBeInTheDocument()
expect(QRCode.toDataURL).toHaveBeenCalledWith('https://wa.me/5569?text=ola', expect.objectContaining({ width: 320 }))
fireEvent.click(await screen.findByRole('button', { name: 'Abrir WhatsApp' }))
expect(window.open).toHaveBeenCalledWith('https://wa.me/5569?text=ola', '_blank', 'noopener,noreferrer')
expect(vi.getTimerCount()).toBe(0)
```
Testar título/texto/room/label, voltar catálogo/início, ausência de state redireciona `/totem/salas`; nenhuma chamada poll/interval. Testar falha QR mostra botão Abrir WhatsApp ainda utilizável. URL só é aceita se protocolo https e host wa.me; state inválido redireciona, evitando `window.open` arbitrário.

- [ ] **Step 2: Rodar e confirmar RED.**
```powershell
npx vitest run src/api/client.test.ts src/pages/TotemRoomDetail.test.tsx src/pages/TotemRoomInterestSuccess.test.tsx
```
Esperado: postPublic/páginas/rotas ausentes.

- [ ] **Step 3: Implementar o mínimo.**

Em apiClient:
```tsx
postPublic<T>(path: string, body: unknown) {
  return request<T>(path, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })
}
```
Isso reutiliza request e não chama mutate/getCsrfToken. Detail usa useParams, AbortController e estado local do formulário. Enviar somente quatro campos, sem roomId/availability. On success:
```tsx
navigate(`/totem/salas/${encodeURIComponent(room.id)}/interesse`, { state: {
  roomName: room.name, whatsappUrl: result.whatsappUrl,
  presentedAvailabilityLabel: result.presentedAvailabilityLabel,
} })
```

Success valida URL com `new URL`, protocolo https e hostname exato wa.me. Renderiza `<h1>Continue no WhatsApp</h1>`, a orientação `Escaneie para continuar no seu celular`, sala e disponibilidade apresentada. QR gerado client-side com target exato. Botão usa `window.open(url, '_blank', 'noopener,noreferrer')`; QR usa alt. Não armazenar em local/sessionStorage nem mostrar inquiryId. Não copiar polling, countdown, token ou expiry de TotemHandoff. CSS garante galeria/form/QR em 390×844 e Totem; campos têm labels e erros role=alert.

- [ ] **Step 4: Rodar e confirmar GREEN.** Repetir comando; zero timers e QR exato.
- [ ] **Step 5: Rodar regressões.** Vitest completo, tsc, build, verify bundle, diff check; navegação real catálogo→detalhe→form→success; teste QR em celular comum e Totem sem efetuar contato externo.
- [ ] **Step 6: Commit.**
```powershell
git add recepcaototem/ClientApp/src/pages/TotemRoomDetail.tsx recepcaototem/ClientApp/src/pages/TotemRoomDetail.test.tsx recepcaototem/ClientApp/src/pages/TotemRoomInterestSuccess.tsx recepcaototem/ClientApp/src/pages/TotemRoomInterestSuccess.test.tsx recepcaototem/ClientApp/src/api/client.ts recepcaototem/ClientApp/src/api/client.test.ts recepcaototem/ClientApp/src/api/modules.ts recepcaototem/ClientApp/src/App.tsx recepcaototem/ClientApp/src/styles.css
git commit -m "feat(totem): add room inquiry WhatsApp handoff"
```

### Task 11: Galeria no Admin de Salas

**Files:**
- Create: `recepcaototem/ClientApp/src/features/rooms/RoomPhotoManager.tsx`.
- Modify: `recepcaototem/ClientApp/src/api/modules.ts`, `recepcaototem/ClientApp/src/pages/admin/Rooms.tsx`, `recepcaototem/ClientApp/src/styles.css`.
- Test: Create `recepcaototem/ClientApp/src/features/rooms/RoomPhotoManager.test.tsx`; Modify `recepcaototem/ClientApp/src/api/modules.test.ts`, `src/pages/admin/Rooms.test.tsx`.

**Interfaces:**
- Consumes: seis endpoints Admin da Task 6, Modal existente, apiClient postMultipart/get/delete/put/post.
- Produces: TS `RoomPhotoDto { id:string; photoUrl:string; sortOrder:number; isCover:boolean; createdAt:string }`.
- Produces: `roomPhotosApi.list(roomId,signal?)`, `upload(roomId,file)`, `remove(roomId,photoId)`, `reorder(roomId,orderedPhotoIds)`, `setCover(roomId,photoId)`.
- Produces: `RoomPhotoManager({ room: RoomDto, onClose():void })` exibido no Modal large.

- [ ] **Step 1: Escrever testes que falham.**

modules.test verifica URL encode e corpos exatos; upload FormData contém apenas file. RoomPhotoManager: carrega por room; loading/erro/retry/vazio; grade ordenada; badge Capa; contador `2/8 fotos`; arquivo aceito chama upload; limite desabilita input e orienta remover; remover confirma pela ação explícita e recarrega; “Definir como capa” chama endpoint; mover anterior/próxima envia todos os IDs uma vez, em nova ordem; extremos desabilitados; erros INVALID_ROOM_PHOTO/ROOM_PHOTO_LIMIT_REACHED/PHOTO_UNAVAILABLE aparecem inline; botões desabilitados enquanto mutação ocorre. Rooms.test verifica “Gerenciar fotos de Sala 101” abre modal e fechar devolve foco ao botão.
```tsx
fireEvent.click(await screen.findByRole('button', { name: 'Mover foto 2 para antes' }))
await waitFor(() => expect(roomPhotosApi.reorder)
  .toHaveBeenCalledWith('room-1', ['photo-2', 'photo-1']))
```
Escolher botões mover para cima/baixo, conforme alternativa aprovada na spec; não introduzir biblioteca de drag-and-drop.

- [ ] **Step 2: Rodar e confirmar RED.**
```powershell
npx vitest run src/features/rooms/RoomPhotoManager.test.tsx src/pages/admin/Rooms.test.tsx src/api/modules.test.ts
```
Esperado: API/componente/botão ausentes.

- [ ] **Step 3: Implementar o mínimo.**

Adicionar `Images` ao card Admin e estado `photoRoom: RoomDto|null`. Modal:
```tsx
<Modal open={photoRoom !== null} onClose={() => setPhotoRoom(null)}
  title={photoRoom ? `Fotos — ${photoRoom.name}` : 'Fotos da sala'} size="large">
  {photoRoom && <RoomPhotoManager room={photoRoom} onClose={() => setPhotoRoom(null)} />}
</Modal>
```
O manager mantém array da resposta, chama `load` após cada mutação e ordena por sortOrder para renderizar. Cada img usa photoUrl retornada pela rota autenticada. Input aceita image/jpeg,image/png,image/webp, um arquivo por operação. Contador deriva de photos.length. Botões têm nomes acessíveis com posição. Reorder usa array completo; setCover não altera ordem local antes da resposta. Remoção usa botão próprio com nome da posição/capa; não usa browser confirm que dificulta teste/kiosk, mas exibe confirmação inline com Cancelar/Remover.

CSS usa grid responsivo e miniaturas com `object-fit: cover`; foco e status não dependem só de cor. Não mostrar storage key ou file id.

- [ ] **Step 4: Rodar e confirmar GREEN.** Repetir comando; checar todas as chamadas e contador.
- [ ] **Step 5: Rodar regressões.** Vitest, tsc, build, production bundle, diff check; verificar mouse/teclado e 390×844; CRUD de Room continua inalterado.
- [ ] **Step 6: Commit.**
```powershell
git add recepcaototem/ClientApp/src/features/rooms/RoomPhotoManager.tsx recepcaototem/ClientApp/src/features/rooms/RoomPhotoManager.test.tsx recepcaototem/ClientApp/src/api/modules.ts recepcaototem/ClientApp/src/api/modules.test.ts recepcaototem/ClientApp/src/pages/admin/Rooms.tsx recepcaototem/ClientApp/src/pages/admin/Rooms.test.tsx recepcaototem/ClientApp/src/styles.css
git commit -m "feat(admin): manage room photo galleries"
```

### Task 12: Listagem somente leitura de interesses no Admin

**Files:**
- Modify: `recepcaototem/Features/Rooms/RoomRentalInquiryContracts.cs`, `recepcaototem/Features/Rooms/RoomRentalInquiryEndpoints.cs`.
- Create: `recepcaototem/ClientApp/src/pages/admin/RoomRentalInquiries.tsx`.
- Modify: `recepcaototem/ClientApp/src/api/modules.ts`, `recepcaototem/ClientApp/src/components/AdminLayout.tsx`, `recepcaototem/ClientApp/src/App.tsx`, `recepcaototem/ClientApp/src/styles.css`.
- Test: Create `tests/GestaoPredio.IntegrationTests/RoomRentalInquiryAdminTests.cs`, `recepcaototem/ClientApp/src/pages/admin/RoomRentalInquiries.test.tsx`, `recepcaototem/ClientApp/src/components/AdminLayout.test.tsx`; Modify `tests/GestaoPredio.IntegrationTests/SecurityTests.cs`, `recepcaototem/ClientApp/src/api/modules.test.ts`.

**Interfaces:**
- Consumes: RoomRentalInquiry/Room, RoomAvailabilityFormatter, PagingQuery/PagedResponse, Operations.
- Produces: `RoomRentalInquiryAdminResponse(Guid Id, Guid RoomId, string RoomName, string FullName, string WhatsApp, string ProfessionOrCompany, string? Note, PublicRoomAvailabilityStatus PresentedAvailabilityStatus, DateOnly? PresentedAvailableFrom, string PresentedAvailabilityLabel, string Status, DateTimeOffset CreatedAt)`.
- Produces: GET `/api/admin/room-rental-inquiries?page={int}&pageSize={int}`, RequireAuthorization("Operations").
- Produces: TS `RoomRentalInquiryAdminDto`, `roomRentalInquiriesApi.list({page,pageSize},signal?)`, rota/nav `/admin/interesses-locacao`.

- [ ] **Step 1: Escrever testes que falham.**

Backend: anônimo 401, Professional 403, Manager/Admin 200; paginação 1..100 via PagingQuery; ordena CreatedAt desc/Id; join traz RoomName; label Now/Soon usa formatter e snapshot histórico mesmo que leases mudem; Status `NEW`; resposta não inclui tarifas/Tenant/Professional relacionado ao contrato; nenhum PUT/POST/DELETE de status existe (405/404). Front: loading/erro/retry/vazio, colunas Interessado/WhatsApp/Sala/Disponibilidade apresentada/Profissão ou empresa/Data/Status, Note acessível sem quebrar tabela, paginação; usa presentedAvailabilityLabel retornado e mostra “Novo”; não há controle de editar status. AdminLayout.test verifica link e título da rota; App inclui página sob ProtectedRoute Admin.
```tsx
expect(await screen.findByText('Ana Souza')).toBeInTheDocument()
expect(screen.getByText('Disponível em breve — a partir de 16/11/2026')).toBeInTheDocument()
expect(screen.queryByRole('button', { name: /alterar status/i })).not.toBeInTheDocument()
```

- [ ] **Step 2: Rodar e confirmar RED.**
```powershell
dotnet test tests/GestaoPredio.IntegrationTests/GestaoPredio.IntegrationTests.csproj --filter "FullyQualifiedName~RoomRentalInquiryAdminTests|FullyQualifiedName~SecurityTests"
npx vitest run src/pages/admin/RoomRentalInquiries.test.tsx src/components/AdminLayout.test.tsx src/api/modules.test.ts
```
Esperado: rota/DTO/página/nav ausentes.

- [ ] **Step 3: Implementar o mínimo.**

No endpoint, mapear GET autenticado separado da rota pública. Validar paginação com `PagingQuery.TryCreate(page,pageSize,null,null,...)`; query AsNoTracking join Room, total count, order desc e projection. Formatar labels após materializar a página; Status = `x.Status == New ? "NEW" : throw`, sem endpoint de mutação.

```csharp
endpoints.MapGet("/api/admin/room-rental-inquiries", ListAdmin)
    .RequireAuthorization("Operations");

var query = from inquiry in db.RoomRentalInquiries.AsNoTracking()
            join room in db.Rooms.AsNoTracking() on inquiry.RoomId equals room.Id
            select new { Inquiry = inquiry, RoomName = room.Name };
var total = await query.CountAsync(ct);
var rows = await query.OrderByDescending(x => x.Inquiry.CreatedAt)
    .ThenByDescending(x => x.Inquiry.Id)
    .Skip((paging.Page - 1) * paging.PageSize).Take(paging.PageSize).ToListAsync(ct);
```

No frontend, adicionar client e página com `pageSize=20`, AbortController, tabela responsiva e botões Anterior/Próxima. Formatar CreatedAt em pt-BR com timezone `America/Porto_Velho`; disponibilidade vem do label do backend. Link da sala vai para `/admin/salas` porque não existe rota de detalhe Admin por ID; usar texto/aria-label, sem inventar rota.

```tsx
const createdAtLabel = (value: string) => new Date(value).toLocaleString('pt-BR', {
  timeZone: 'America/Porto_Velho', dateStyle: 'short', timeStyle: 'short',
})
// navItems, antes de Configurações:
{ to: '/admin/interesses-locacao', label: 'Interesses de locação', icon: MessageSquareText }
// App.tsx, dentro de /admin:
<Route path="interesses-locacao" element={<RoomRentalInquiries />} />
```

Adicionar `MessageSquareText` em navItems antes de Configurações; ajustar os slices atuais para manter item na seção Gestão e Configurações em Preferências. Acrescentar pageNames e Route. AdminLayout.test mocka useSession/logout e renderiza MemoryRouter + Routes, verificando link; não depende de backend.

- [ ] **Step 4: Rodar e confirmar GREEN.** Repetir ambos; confirmar paginação e autorização.
- [ ] **Step 5: Rodar regressões.** Integração completa local, Vitest, tsc, build, production bundle e diff check; smoke de nav Admin em desktop/mobile.
- [ ] **Step 6: Commit.**
```powershell
git add recepcaototem/Features/Rooms/RoomRentalInquiryContracts.cs recepcaototem/Features/Rooms/RoomRentalInquiryEndpoints.cs tests/GestaoPredio.IntegrationTests/RoomRentalInquiryAdminTests.cs tests/GestaoPredio.IntegrationTests/SecurityTests.cs recepcaototem/ClientApp/src/pages/admin/RoomRentalInquiries.tsx recepcaototem/ClientApp/src/pages/admin/RoomRentalInquiries.test.tsx recepcaototem/ClientApp/src/components/AdminLayout.tsx recepcaototem/ClientApp/src/components/AdminLayout.test.tsx recepcaototem/ClientApp/src/api/modules.ts recepcaototem/ClientApp/src/api/modules.test.ts recepcaototem/ClientApp/src/App.tsx recepcaototem/ClientApp/src/styles.css
git commit -m "feat(admin): list room rental inquiries"
```

### Task 13: Gates finais e preparação de staging sem mudança remota

**Files:**
- Create: `docs/operations/2026-09-13-room-rental-staging.md`.
- Modify: esse documento com resultados locais verdadeiros.
- Test: solução completa, frontend, SQL gerado offline e checklist operacional.

**Interfaces:**
- Consumes: commits Tasks 1–12, migration RoomPhotosAndRentalInquiries, runbook `docs/operations/staging-railway-runbook.md` e configuração externa existente.
- Produces: pacote local revisado e runbook com SHAs, migration, sequência de staging, gates/pendências; nenhuma alteração no Supabase, Railway ou env vars.

- [ ] **Step 1: Escrever o teste operacional que falha inicialmente.**

Criar o runbook com tabela `Gate | Comando/evidência | Resultado`, inicialmente NÃO EXECUTADO para: árvore limpa; build/test completo; frontend; script SQL; PrivateFilesPath persistente/volume; variável WhatsApp; backup/restore; migration; deploy; health/smoke. Registrar explicitamente que resultados remotos são PENDENTES DE AUTORIZAÇÃO. Incluir rollback operacional por forward fix e restauração validada; não instruir `Down` destrutivo após dados reais.

- [ ] **Step 2: Rodar e confirmar RED de preparação.**

Antes dos gates, o documento deve indicar PREPARAÇÃO INCOMPLETA porque testes/script/configuração externa não foram validados. Isso é o RED operacional; não transformar pendências remotas em PASS.

- [ ] **Step 3: Executar o mínimo local e preencher evidência verdadeira.**

Na raiz:
```powershell
dotnet build recepcaototem.sln
dotnet test recepcaototem.sln
git diff --check
```
Em ClientApp:
```powershell
npx vitest run
npx tsc -b
npx vite build
node scripts/verify-production-bundle.mjs
```
Gerar script idempotente em arquivo temporário fora do repo, sem conexão:
```powershell
dotnet ef migrations script TotemBookingHandoff RoomPhotosAndRentalInquiries --idempotent --project src/GestaoPredio.Infrastructure --startup-project recepcaototem --context ApplicationDbContext --output $env:TEMP\lumis-room-rental.sql
```
Inspecionar que contém apenas troca do CHECK PrivateFiles, RoomPhotos/RoomRentalInquiries, FKs/índices esperados; nenhum DML, DROP TABLE/COLUMN, tabela de identidade, Room/Lease/Professional alterada. Apagar o temporário após registrar hash SHA-256/contagem de operações no runbook.

Preparar a sequência remota, sem executá-la: verificar backup restaurável; confirmar que `Storage__PrivateFilesPath` aponta para volume persistente do Railway; confirmar número real em `Whatsapp__FinanceiroPhoneNumber` sem registrar valor; aplicar migration via conexão de migração autorizada ao Session pooler conforme runbook existente; verificar `__EFMigrationsHistory`, tabelas/check/índices; publicar SHA; `/health` e `/health/ready`; smoke catálogo/foto/form/QR/Admin. Cada ação remota tem checkpoint explícito de autorização.

Como o Supabase pode expor o schema public pela Data API, incluir preflight de privilégios: verificar se anon/authenticated têm acesso às novas tabelas. Não conceder acesso nem criar política automaticamente; se houver exposição, parar para decisão de segurança antes do deploy. O frontend fala somente com ASP.NET, nunca direto com Supabase.

- [ ] **Step 4: Confirmar GREEN local e pendências remotas separadamente.**

Somente se todos os comandos locais e inspeção SQL passarem, marcar `PREPARAÇÃO LOCAL APROVADA`. Manter `STAGING NÃO ALTERADO` e itens remotos PENDENTES. Qualquer falha local volta à tarefa responsável com commit corretivo; repetir apenas gates afetados e gate final.

- [ ] **Step 5: Rodar regressões finais e revisar segurança.**

Confirmar git diff --check, git status, ausência de número real/connection string/storage path em `git diff` e bundle, todos os endpoints públicos AllowAnonymous explícitos, mutações Admin com CSRF, POST público com limiter, DTOs sem preços/contratos, nenhum startup migration. Executar `rg -n "FinanceiroPhoneNumber|ROOM_PHOTO|RoomRentalInquiry"` e revisar cada ocorrência; não imprimir segredos de ambiente.

- [ ] **Step 6: Commit do runbook e resultado local.**
```powershell
git add docs/operations/2026-09-13-room-rental-staging.md
git commit -m "docs(operations): prepare room rental staging rollout"
```
Não executar push, migration remota, mudança de env, deploy ou smoke remoto nesta task. Após commit, apresentar SHAs/gates/pendências ao usuário e parar para autorização separada.

## Endpoints finais planejados

| Verbo | Rota | Proteção |
|---|---|---|
| GET | `/api/totem/rooms` | AllowAnonymous + CustomerPublicRateLimiter |
| GET | `/api/totem/rooms/{id}` | AllowAnonymous + CustomerPublicRateLimiter |
| GET | `/api/totem/rooms/{roomId}/photos/{photoId}` | AllowAnonymous + CustomerPublicRateLimiter |
| POST | `/api/totem/rooms/{roomId}/rental-inquiries` | AllowAnonymous + RoomRentalInquiryRateLimiter |
| GET | `/api/admin/rooms/{roomId}/photos` | Operations |
| GET | `/api/admin/rooms/{roomId}/photos/{photoId}` | Operations |
| POST | `/api/admin/rooms/{roomId}/photos` | Operations + antiforgery |
| DELETE | `/api/admin/rooms/{roomId}/photos/{photoId}` | Operations + antiforgery |
| PUT | `/api/admin/rooms/{roomId}/photos/reorder` | Operations + antiforgery |
| POST | `/api/admin/rooms/{roomId}/photos/{photoId}/cover` | Operations + antiforgery |
| GET | `/api/admin/room-rental-inquiries` | Operations |

## Riscos controlados pelo plano

- Concorrência na primeira capa/reorder/delete: lock da linha Room + transação + índice parcial.
- Arquivo órfão entre storage e banco: cleanup explícito e testes de falha; storage existente não oferece transação distribuída.
- Arquivos em Railway: fotos só podem ser habilitadas após confirmar volume persistente no PrivateFilesPath.
- Data civil: converter OccupancyEndAt para America/Porto_Velho antes de AddDays(1).
- Vazamento comercial/pessoal: DTOs públicos fechados e testes negativos; logs sem conteúdo do formulário/URL.
- Abuso do POST: buckets por IP e WhatsApp, além do limiter global.
- WhatsApp ausente/inválido: 503 em Development; start falha em Staging/Production.
- Refresh da tela de sucesso perde navigation state e volta ao catálogo; URL não fica em storage.
- Migration altera um CHECK existente: inspecionar script/backup e aplicar antes do build dependente, somente com autorização.
- Supabase Data API: verificar grants/exposição antes de aplicar; nenhuma concessão é presumida.
- QR abre serviço externo: somente host `wa.me` aceito na UI e target vem do backend.

## Autorrevisão do plano

- §5 → Tasks 1 e 7; §6.1–6.3 → Tasks 2–4 e 6; §6.4 → Task 11.
- §7.1–7.3 → Tasks 2, 3 e 8; §7.4 → Tasks 9–10; §7.5 → Task 12.
- §8 → Tasks 5, 8 e 10; §9 → Tasks 6–8; §10 → Task 3; §11 → Tasks 6–8 e 12; §12 → testes distribuídos; §14 → Task 13.
- Migration única e nome exato fixados; nenhum comando de aplicação remota autorizado.
- Contratos, paths, namespaces e comandos existentes foram verificados; caminhos Create são propostas explícitas.
- Cada Task tem Files Create/Modify/Test, Interfaces Consumes/Produces e ciclo RED → mínimo → GREEN → regressões → commit.
- Não há CPF/CNPJ/Version/CRM, preço público, storage paralelo, checkbox de disponibilidade, polling de WhatsApp ou número real.
- Revisão documental concluída; nenhuma implementação, migration ou operação remota foi executada.

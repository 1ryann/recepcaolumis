# Professionals and Rooms Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the demonstrative Professionals and Rooms modules with authenticated, audited, concurrent-safe SQL Server features, add private professional-photo storage and controlled Identity linking, and make every out-of-scope production route explicitly unavailable.

**Architecture:** Keep the existing Domain → Application → Infrastructure → ASP.NET Core API dependency direction. Domain owns entities and deterministic value rules; Application owns ports and feature contracts; Infrastructure owns EF Core and private filesystem implementations; minimal-API feature folders own HTTP mapping and orchestration; React consumes same-origin typed endpoints. Database mutation and success audit share one transaction, while filesystem work uses explicit pre-commit staging and post-commit cleanup.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, ASP.NET Core Identity, EF Core 10, SQL Server Express, xUnit, React, TypeScript, Vite, Vitest, Testing Library.

**Spec:** `docs/superpowers/specs/2026-09-05-professionals-rooms-design.md`

## Global Constraints

- Target .NET 10, EF Core 10, SQL Server, React, TypeScript, and Vite already present in the solution; do not replace the architecture or introduce generic repositories.
- Use only local code, local publish output, and dedicated `GestaoPredioAuthTests*` / `GestaoPredioModulesTests*` SQL Server databases; reject `GestaoPredioDB` before opening a connection.
- Never connect to production, run production bootstrap, deploy to IIS, change IIS/firewall/bindings/ports/SQL Server/server permissions, or apply a production migration.
- Never run migrations automatically at application startup. Generate and review forward SQL; production rollback never uses migration `Down`.
- Keep Professionals and Rooms under policy `Operations`; keep eligible-user and user-link endpoints exclusively under `Administration`; require antiforgery for every POST, PUT, and DELETE.
- Preserve same-origin secure-cookie authentication, existing auth contract semantics, and feature-scoped strict JSON handling.
- Store private bytes outside SQL Server, `wwwroot`, and the IIS publish tree. Never persist/log original filenames, physical paths, storage keys in audit, file bytes, passwords, cookies, or tokens.
- Keep all schema changes additive: no DROP, TRUNCATE, DELETE, rename, database/table/Identity collation change, Identity-schema rewrite, or cascade delete.
- `Storage__PrivateFilesPath` is mandatory in every executable environment. `Storage__ProfessionalPhotoMaxBytes` defaults to 5 MiB and must fail startup outside the approved 1-byte to 10-MiB range.
- Room rates use backend `decimal`, SQL `decimal(18,2)`, JSON numbers, range `0` through `9999999999999.99`, and at most two fractional digits without rounding or truncation.
- Production builds retain every route and navigation item but render `ModuleUnavailable` for Dashboard, Reception, Leases, Visits, and Settings; no runtime switch may reactivate mocks.

## File Structure

- `src/GestaoPredio.Domain/{Professionals,Rooms,Files}` holds entities and deterministic business-value rules without EF or HTTP dependencies.
- `src/GestaoPredio.Application/{Abstractions,Files}` holds storage/photo interfaces and transport-neutral results used by the API and Infrastructure.
- `src/GestaoPredio.Infrastructure/{Persistence,Files}` holds EF configurations/migration and the private-filesystem/image-parser implementations.
- `recepcaototem/Features/{Common,Professionals,Rooms}` holds focused request/response contracts, endpoint maps, projections, and known SQL error translation.
- `tests/GestaoPredio.UnitTests` holds pure rule/parser/storage tests; `tests/GestaoPredio.IntegrationTests` holds guarded SQL Server and HTTP tests.
- `recepcaototem/ClientApp/src/{api,features,pages}` holds the typed same-origin client and API-backed screens; `src/dev` is the sole home of demonstrative state.
- `artifacts/sql` holds generated, reviewable SQL and checksums; `docs/operations` holds future production procedures that stop at an approval gate.

---

### Task 1: Add the unit-test project and deterministic value rules

**Files:**
- Create: `tests/GestaoPredio.UnitTests/GestaoPredio.UnitTests.csproj`
- Create: `tests/GestaoPredio.UnitTests/TextNormalizerTests.cs`
- Create: `tests/GestaoPredio.UnitTests/WhatsAppNormalizerTests.cs`
- Create: `tests/GestaoPredio.UnitTests/RoomRateTests.cs`
- Create: `src/GestaoPredio.Domain/Common/TextNormalizer.cs`
- Create: `src/GestaoPredio.Domain/Professionals/WhatsAppNormalizer.cs`
- Create: `src/GestaoPredio.Domain/Rooms/RoomRate.cs`
- Modify: `recepcaototem.sln`

**Interfaces:**
- Consumes: only .NET BCL Unicode and `decimal` APIs.
- Produces: `TextNormalizer.Normalize(string): string`, `WhatsAppNormalizer.TryNormalize(string?, out string): bool`, `RoomRate.Maximum: decimal`, and `RoomRate.IsValid(decimal): bool`.

- [ ] **Step 1: Create failing normalization tests**

Cover trim, whitespace collapse, case, diacritics, composed/decomposed Unicode, already-normalized input, repeatability, and expansion under uppercase. Use a concrete expansion case such as `straße` → `STRASSE`, and assert the same implementation normalizes professional name, profession, room name, and search terms.

```csharp
[Theory]
[InlineData("  Fisióterapia  ", "FISIOTERAPIA")]
[InlineData("Fisio\u0301terapia", "FISIOTERAPIA")]
[InlineData("Sala   01", "SALA 01")]
[InlineData("straße", "STRASSE")]
public void Normalize_produces_the_deterministic_key(string input, string expected) =>
    Assert.Equal(expected, TextNormalizer.Normalize(input));
```

- [ ] **Step 2: Run the focused test and confirm RED**

Run: `dotnet test tests/GestaoPredio.UnitTests/GestaoPredio.UnitTests.csproj --filter TextNormalizerTests`

Expected: compilation failure because `TextNormalizer` does not exist.

- [ ] **Step 3: Implement one shared `TextNormalizer`**

Implement `public static string Normalize(string value)` using trim, Unicode whitespace collapse, FormD, removal of `UnicodeCategory.NonSpacingMark`, FormC, then `ToUpperInvariant()`. Do not duplicate normalization in entities, handlers, or endpoints.

- [ ] **Step 4: Add failing WhatsApp tests**

Cover masked and unmasked Brazilian 10/11-digit values, valid explicit international E.164, punctuation, letters, extensions, short/long values, ambiguous international input without `+`, empty text, and already-normalized values.

- [ ] **Step 5: Implement `WhatsAppNormalizer.TryNormalize`**

Expose `public static bool TryNormalize(string? input, out string canonical)`. Permit punctuation only in safely recognized national formatting, infer `+55` only for 10 or 11 national digits, require explicit `+` for other international numbers, and cap the canonical form at 15 digits after `+`.

- [ ] **Step 6: Add failing room-rate tests**

Cover `0`, integer, one/two/three fractional digits, negative, `9_999_999_999_999.99m`, and one cent above the application maximum.

- [ ] **Step 7: Implement the decimal-only rate guard**

```csharp
public static class RoomRate
{
    public const decimal Maximum = 9_999_999_999_999.99m;
    public static bool IsValid(decimal value) =>
        value >= 0m && value <= Maximum && decimal.Round(value, 2) == value;
}
```

Use `decimal.Round` only for comparison in validation; never return the rounded value or persist a transformed value.

- [ ] **Step 8: Verify GREEN and commit**

Run: `dotnet test tests/GestaoPredio.UnitTests/GestaoPredio.UnitTests.csproj`

Expected: all Task 1 tests pass.

Commit: `test: establish module value rules`

---

### Task 2: Model Professionals, Rooms, PrivateFiles, and audit targets

**Files:**
- Create: `src/GestaoPredio.Domain/Professionals/Professional.cs`
- Create: `src/GestaoPredio.Domain/Rooms/Room.cs`
- Create: `src/GestaoPredio.Domain/Files/PrivateFile.cs`
- Create: `src/GestaoPredio.Domain/Files/PrivateFilePurposes.cs`
- Create: `src/GestaoPredio.Domain/Auditing/AuditActions.cs`
- Create: `src/GestaoPredio.Domain/Auditing/AuditTargetTypes.cs`
- Modify: `src/GestaoPredio.Domain/Auditing/AuditEntry.cs`
- Create: `tests/GestaoPredio.UnitTests/EntityRuleTests.cs`
- Create: `tests/GestaoPredio.UnitTests/AuditFieldTests.cs`

**Interfaces:**
- Consumes: `TextNormalizer`, `WhatsAppNormalizer`, and `RoomRate` from Task 1.
- Produces: `Professional`, `Room`, `PrivateFile`, `PrivateFilePurposes.ProfessionalPhoto`, `AuditActions`, `AuditTargetTypes`, and the extended `AuditEntry` model.

- [ ] **Step 1: Write failing entity tests**

Assert that factory/update methods calculate normalized fields, new Professionals and Rooms start active, server timestamps are supplied explicitly, GUID is the technical identity, and two identical professional records remain distinct. Assert that input models cannot set `IsActive`, normalized fields, timestamps, rowversion, file ID, or user ID.

- [ ] **Step 2: Add the three entities**

Use these server-owned properties:

```csharp
public sealed class Professional
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public string NormalizedName { get; private set; } = "";
    public string Profession { get; private set; } = "";
    public string NormalizedProfession { get; private set; } = "";
    public string WhatsApp { get; private set; } = "";
    public Guid? PhotoFileId { get; private set; }
    public string? ApplicationUserId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
}
```

Give `Room` the approved fields and `PrivateFile` only `Id`, `StorageKey`, `MimeType`, `Length`, `Purpose`, and `CreatedAt`. Keep explicit internal methods for update, activation, photo reference, and Identity association so endpoints cannot set derived properties freely.

- [ ] **Step 3: Write failing audit tests**

Assert `ChangedFields` sorts unique approved names deterministically and never contains a colon, `=`, WhatsApp value, email, filename, storage key, path, or bytes. Existing auth entries must remain valid with null entity target fields.

- [ ] **Step 4: Extend `AuditEntry` and constants**

Add nullable `TargetEntityType`, `TargetEntityId`, and `ChangedFields`. Add controlled constants for `PROFESSIONAL`, `ROOM`, and every approved event. Provide a helper that accepts field-name constants and returns a comma-separated, sorted string within 500 characters.

- [ ] **Step 5: Run and commit**

Run: `dotnet test tests/GestaoPredio.UnitTests/GestaoPredio.UnitTests.csproj`

Commit: `feat: model professionals rooms and private files`

---

### Task 3: Configure EF Core and generate the additive migration

**Files:**
- Create: `src/GestaoPredio.Infrastructure/Persistence/Configurations/ProfessionalConfiguration.cs`
- Create: `src/GestaoPredio.Infrastructure/Persistence/Configurations/RoomConfiguration.cs`
- Create: `src/GestaoPredio.Infrastructure/Persistence/Configurations/PrivateFileConfiguration.cs`
- Modify: `src/GestaoPredio.Infrastructure/Persistence/ApplicationDbContext.cs`
- Modify: `tests/GestaoPredio.IntegrationTests/MigrationSafetyTests.cs`
- Create: `tests/GestaoPredio.IntegrationTests/ModuleModelTests.cs`
- Create via EF command: `src/GestaoPredio.Infrastructure/Persistence/Migrations/*_ProfessionalsAndRooms.cs` (the EF tool supplies the migration ID)
- Create via EF command: `src/GestaoPredio.Infrastructure/Persistence/Migrations/*_ProfessionalsAndRooms.Designer.cs` (same generated migration ID)
- Modify: `src/GestaoPredio.Infrastructure/Persistence/Migrations/ApplicationDbContextModelSnapshot.cs`

**Interfaces:**
- Consumes: the Domain entities and audit model from Task 2.
- Produces: `ApplicationDbContext.Professionals`, `.Rooms`, `.PrivateFiles`, their EF mappings, named indexes/constraints, and migration `ProfessionalsAndRooms`.

- [ ] **Step 1: Add failing model-metadata tests**

Assert exact lengths: Professional input columns 200/150, normalized columns 400/300, WhatsApp non-Unicode length 16; Room name 100 and normalized name 200; description 1000; rates `decimal(18,2)`; audit target type 50 and changed fields 500. Assert rowversion concurrency tokens, check constraint for `PROFESSIONAL_PHOTO`, and delete behavior `NoAction`.

Assert named indexes:

```text
UX_Professionals_ApplicationUserId (unique, filtered IS NOT NULL)
UX_Professionals_PhotoFileId       (unique, filtered IS NOT NULL)
UX_Rooms_NormalizedName            (unique)
UX_PrivateFiles_StorageKey         (unique)
IX_AuditEntries_TargetEntity       (TargetEntityType, TargetEntityId, OccurredAt)
```

- [ ] **Step 2: Configure the model and DbSets**

Add `DbSet<Professional>`, `DbSet<Room>`, and `DbSet<PrivateFile>`. Apply dedicated configurations from `OnModelCreating`. Keep existing Identity and audit indexes intact. Use `IsRowVersion()`, filtered SQL Server unique indexes, `DeleteBehavior.NoAction`, and a named purpose check constraint.

- [ ] **Step 3: Run model tests and confirm GREEN**

Run: `dotnet test tests/GestaoPredio.IntegrationTests/GestaoPredio.IntegrationTests.csproj --filter "ModuleModelTests|MigrationSafetyTests"`

- [ ] **Step 4: Generate the migration locally**

Run:

```powershell
dotnet tool restore
dotnet ef migrations add ProfessionalsAndRooms --project src/GestaoPredio.Infrastructure/GestaoPredio.Infrastructure.csproj --startup-project recepcaototem/recepcaototem.csproj --output-dir Persistence/Migrations
```

Do not run `dotnet ef database update` here.

- [ ] **Step 5: Add migration safety assertions**

Inspect generated operations programmatically and as SQL. Assert the `Up` path contains only `CreateTable`, `AddColumn`, `CreateIndex`, `AddForeignKey`, and `AddCheckConstraint` operations; no drop, delete, truncate, rename, raw destructive SQL, collation change, Identity alteration, or cascade delete. Assert `Down` is never part of the production runbook.

- [ ] **Step 6: Verify Unicode expansion persistence capacity**

In the SQL Server integration suite, persist a valid 200-character professional name whose uppercase mapping expands (for example repeated `ß` within the input limit) and assert the derived value fits and round-trips from `nvarchar(400)`.

- [ ] **Step 7: Commit**

Commit: `feat: add professionals and rooms schema`

---

### Task 4: Build a production-safe modules integration fixture

**Files:**
- Create: `tests/GestaoPredio.IntegrationTests/ModulesApiFactory.cs`
- Create: `tests/GestaoPredio.IntegrationTests/ModulesApiFactoryTests.cs`
- Modify: `tests/GestaoPredio.IntegrationTests/AuthApiFactory.cs`

**Interfaces:**
- Consumes: `ApplicationDbContext` and migration `ProfessionalsAndRooms` from Task 3.
- Produces: `ModulesApiFactory`, authenticated clients, multi-role user creation, CSRF mutation helpers, and guarded `GestaoPredioModulesTests*` lifecycle.

- [ ] **Step 1: Write failing guard tests**

Exercise missing connection, Production environment, `GestaoPredioDB`, a database without `GestaoPredioModulesTests` prefix, and fallback to the normal app connection. Each must fail before opening SQL. Also assert each fixture gets a unique private-files root and does not share mutable database state.

- [ ] **Step 2: Implement `ModulesApiFactory`**

Use a connection whose catalog is `GestaoPredioModulesTests` or a deterministic test-specific suffix. Set environment `Testing`, HTTPS base address, `Storage:PrivateFilesPath`, and `Storage:ProfessionalPhotoMaxBytes`. Migrate only this guarded test database. Reset tables in FK-safe order: Professionals, Rooms, PrivateFiles, AuditEntries, user roles/users/roles.

Extend helper methods to support multiple roles so the eligibility matrix can create `PROFISSIONAL + GERENTE` and `PROFISSIONAL + ADMINISTRADOR` accounts. Add CSRF-aware `Post`, `Put`, and `Delete` helpers.

- [ ] **Step 3: Run fixture tests and commit**

Run: `dotnet test tests/GestaoPredio.IntegrationTests/GestaoPredio.IntegrationTests.csproj --filter ModulesApiFactoryTests`

Commit: `test: isolate modules integration database`

---

### Task 5: Add shared module HTTP contracts and strict input handling

**Files:**
- Create: `recepcaototem/Features/Common/ApiError.cs`
- Create: `recepcaototem/Features/Common/PagedResponse.cs`
- Create: `recepcaototem/Features/Common/PagingQuery.cs`
- Create: `recepcaototem/Features/Common/ConcurrencyToken.cs`
- Create: `recepcaototem/Features/Common/StrictBody.cs`
- Create: `tests/GestaoPredio.UnitTests/ConcurrencyTokenTests.cs`
- Create: `tests/GestaoPredio.IntegrationTests/StrictModuleContractsTests.cs`

**Interfaces:**
- Consumes: existing `AntiforgeryFilter` and the Task 4 test fixture.
- Produces: `ApiError`, `PagedResponse<T>`, validated `PagingQuery`, `ConcurrencyToken.TryDecode(string?, out byte[])`, and feature-scoped `StrictBody<T>` binding.

- [ ] **Step 1: Write failing paging/token tests**

Assert defaults page 1/pageSize 20/status all; reject page 0, pageSize 0 or 101, invalid status, and search beyond 100 characters using `INVALID_PAGE`, `INVALID_PAGE_SIZE`, `INVALID_STATUS`, and `INVALID_SEARCH`. Empty normalized search means no search. Assert missing, malformed Base64, and structurally invalid rowversion return 400; stale valid tokens are left for EF concurrency handling.

- [ ] **Step 2: Implement reusable contracts**

Use a single `PagedResponse<T>` and validate query values before constructing `IQueryable`. Encode/decode SQL Server rowversion as opaque Base64 and require exactly eight bytes.

- [ ] **Step 3: Add strict-body regression tests**

Post an unknown property to each new create/update contract and expect 400. Re-run login, CSRF, change-password, and administrative user creation with their published bodies and expect their existing results.

- [ ] **Step 4: Implement feature-scoped strict deserialization**

Use a module endpoint filter or explicit `JsonSerializerOptions` with `UnmappedMemberHandling = Disallow` only for new module DTOs. Do not change the global JSON settings.

- [ ] **Step 5: Run and commit**

Run: `dotnet test tests/GestaoPredio.UnitTests/GestaoPredio.UnitTests.csproj --filter ConcurrencyTokenTests`

Run: `dotnet test tests/GestaoPredio.IntegrationTests/GestaoPredio.IntegrationTests.csproj --filter "StrictModuleContractsTests|AuthenticationTests|AntiforgeryTests|UserAdministrationTests"`

Commit: `feat: add strict module api contracts`

---

### Task 6: Implement Professional list, detail, create, and edit

**Files:**
- Create: `recepcaototem/Features/Professionals/ProfessionalContracts.cs`
- Create: `recepcaototem/Features/Professionals/ProfessionalMappings.cs`
- Create: `recepcaototem/Features/Professionals/ProfessionalEndpoints.cs`
- Create: `tests/GestaoPredio.IntegrationTests/ProfessionalQueryTests.cs`
- Create: `tests/GestaoPredio.IntegrationTests/ProfessionalMutationTests.cs`
- Modify: `recepcaototem/Program.cs`

**Interfaces:**
- Consumes: Tasks 1–5 entities, normalizers, EF sets, paging, strict bodies, antiforgery, and rowversion decoding.
- Produces: `MapProfessionalEndpoints()`, Professional request/response records, and GET/POST/PUT `/api/admin/professionals` contracts.

- [ ] **Step 1: Write failing authorization and query tests**

Cover anonymous 401, `PROFISSIONAL` 403, Admin/Gerente success, defaults, pages, pageSize 100, invalid parameters, page beyond total, status filters, combined search/status, `totalCount`, deterministic `NormalizedName, Id` ordering, case/accent-insensitive name and profession search, and no N+1-sensitive Identity/file details in DTOs.

- [ ] **Step 2: Implement GET collection/detail**

Build `IQueryable<Professional>().AsNoTracking()`, apply filters before `CountAsync`, then ordering, `Skip`, `Take`, and projection. Return only:

```text
id, name, profession, whatsapp, isActive, hasPhoto, photoUrl,
hasLinkedUser, createdAt, updatedAt, concurrencyToken
```

`photoUrl` is `/api/admin/professionals/{id}/photo`; never expose IDs, keys, paths, or Identity properties.

- [ ] **Step 3: Write failing create/edit tests**

Cover input lengths, normalization, E.164 conversion, invalid phone, duplicate professionals allowed in every approved combination, GUID identity, active-on-create, overposted status rejection, changed normalized columns, no-op, audit fields, stale token, and new token after mutation.

- [ ] **Step 4: Implement POST and PUT**

Map both under `Operations` and `AntiforgeryFilter`. POST accepts only `name`, `profession`, `whatsapp`. PUT additionally requires `concurrencyToken`. Set EF original rowversion before save. In a transaction, save the mutation and `PROFESSIONAL_CREATED`/`PROFESSIONAL_UPDATED` audit together. For updates, calculate stable `ChangedFields` from `Name`, `Profession`, and `WhatsApp` only.

- [ ] **Step 5: Run and commit**

Run: `dotnet test tests/GestaoPredio.IntegrationTests/GestaoPredio.IntegrationTests.csproj --filter "ProfessionalQueryTests|ProfessionalMutationTests"`

Commit: `feat: add professional records api`

---

### Task 7: Add Professional activation and deactivation

**Files:**
- Modify: `recepcaototem/Features/Professionals/ProfessionalContracts.cs`
- Modify: `recepcaototem/Features/Professionals/ProfessionalEndpoints.cs`
- Create: `tests/GestaoPredio.IntegrationTests/ProfessionalStatusTests.cs`

**Interfaces:**
- Consumes: `MapProfessionalEndpoints()` and Professional DTO/token contracts from Task 6.
- Produces: POST `/api/admin/professionals/{id:guid}/activate` and `/deactivate` with `ConcurrencyRequest`.

- [ ] **Step 1: Write failing tests**

Cover Admin/Gerente success, 401/403, antiforgery, required token, invalid/stale token, activate/deactivate, same-state no-op, updated timestamp/token only for real mutation, no success audit after conflict, and no automatic Identity status change.

- [ ] **Step 2: Implement explicit status endpoints**

Add `POST /api/admin/professionals/{id:guid}/activate` and `/deactivate`. Both accept only `concurrencyToken`, validate the token before no-op detection, and write `PROFESSIONAL_ACTIVATED` or `PROFESSIONAL_DEACTIVATED` in the same transaction.

- [ ] **Step 3: Verify and commit**

Run: `dotnet test tests/GestaoPredio.IntegrationTests/GestaoPredio.IntegrationTests.csproj --filter ProfessionalStatusTests`

Commit: `feat: add professional status operations`

---

### Task 8: Implement Room query and mutation endpoints

**Files:**
- Create: `recepcaototem/Features/Rooms/RoomContracts.cs`
- Create: `recepcaototem/Features/Rooms/RoomMappings.cs`
- Create: `recepcaototem/Features/Rooms/RoomEndpoints.cs`
- Create: `recepcaototem/Features/Rooms/SqlServerRoomErrors.cs`
- Create: `tests/GestaoPredio.IntegrationTests/RoomQueryTests.cs`
- Create: `tests/GestaoPredio.IntegrationTests/RoomMutationTests.cs`
- Create: `tests/GestaoPredio.IntegrationTests/RoomConcurrencyTests.cs`
- Modify: `recepcaototem/Program.cs`

**Interfaces:**
- Consumes: Tasks 1–5 shared validation, EF model, paging, and concurrency contracts.
- Produces: `MapRoomEndpoints()`, Room DTOs, GET/POST/PUT `/api/admin/rooms`, and explicit activate/deactivate endpoints.

- [ ] **Step 1: Write failing query tests**

Mirror professional paging/authorization tests, searching only `Room.NormalizedName`, sorting by normalized name then ID, and returning only the approved Room DTO.

- [ ] **Step 2: Implement GET collection/detail**

Use server-side `CountAsync` plus paginated SQL projection with `AsNoTracking`.

- [ ] **Step 3: Write failing mutation tests**

Cover required fields, description length, every approved rate value including max/above max, three decimals, negative values, JSON currency strings, exact decimal persistence, equivalent room names across case/spacing/accents, inactive duplicate blocking, edit without name change, rename conflict, and SQL race protection.

- [ ] **Step 4: Implement POST/PUT/status endpoints**

Map under `Operations` with antiforgery. Normalize name only through `TextNormalizer`. Validate rates before EF. Use the permanent unique index as final protection and translate only SQL 2601/2627 that identify `UX_Rooms_NormalizedName` to `409 ROOM_NAME_ALREADY_EXISTS`. Preserve `RESOURCE_MODIFIED` for rowversion conflicts. Audit create/update/activate/deactivate, with effective changed field names for update.

- [ ] **Step 5: Add concurrency tests and commit**

Simulate two reads sharing a token: first edit succeeds, second returns `409 RESOURCE_MODIFIED`; repeat for status. Assert successful mutation returns a new token and conflicts produce no success audit.

Run: `dotnet test tests/GestaoPredio.IntegrationTests/GestaoPredio.IntegrationTests.csproj --filter "RoomQueryTests|RoomMutationTests|RoomConcurrencyTests"`

Commit: `feat: add rooms api`

---

### Task 9: Implement eligible Identity accounts and explicit association

**Files:**
- Create: `recepcaototem/Features/Professionals/ProfessionalUserLinkContracts.cs`
- Create: `recepcaototem/Features/Professionals/ProfessionalUserLinkEndpoints.cs`
- Create: `recepcaototem/Features/Professionals/EligibleUserQuery.cs`
- Create: `recepcaototem/Features/Professionals/SqlServerProfessionalErrors.cs`
- Create: `tests/GestaoPredio.IntegrationTests/EligibleProfessionalUserTests.cs`
- Create: `tests/GestaoPredio.IntegrationTests/ProfessionalUserLinkTests.cs`
- Modify: `recepcaototem/Program.cs`

**Interfaces:**
- Consumes: Professional rowversion/API conventions, Identity schema, `SystemRoles`, and Task 4 multi-role fixture.
- Produces: `MapProfessionalUserLinkEndpoints()`, `EligibleUserQuery.Collation`, paged eligible-user DTOs, and GET/PUT/DELETE user-link contracts.

- [ ] **Step 1: Write failing authorization and eligibility tests**

All four endpoints return 401 for anonymous and 403 for Gerente/Profissional. For Admin, cover pagination and search by DisplayName/email with `Latin1_General_100_CI_AI`, including a database integration assertion that this collation exists. Cover this exact role matrix in both eligibility and PUT validation:

```text
PROFISSIONAL only          eligible
ADMINISTRADOR only         ineligible
GERENTE only               ineligible
PROFISSIONAL + GERENTE     ineligible
PROFISSIONAL + ADMIN       ineligible
```

Also exclude inactive and already-linked accounts.

- [ ] **Step 2: Implement the SQL eligibility query**

Centralize the collation name in `EligibleUserQuery`. Compose Identity user-role joins so the user must have PROFISSIONAL and must not have ADMINISTRADOR or GERENTE. Keep the query in SQL, page it, and return only user ID, display name, and email.

- [ ] **Step 3: Write failing association tests**

Cover GET linked false/true, missing Professional 404, valid link, same-link no-op, replacement, unlink, empty unlink no-op, inactive/missing/invalid-role user, unique-link conflict, two Professionals racing for the same account, stale token, new token, minimal DTOs, and exact audit targets/events.

- [ ] **Step 4: Implement GET/PUT/DELETE user-link**

Require `Administration`; require antiforgery on PUT/DELETE. Re-run the full role predicate and active check at write time. Validate professional rowversion before no-op. Never modify the Identity user or roles. Translate only the named filtered-index violation to `PROFESSIONAL_USER_ALREADY_LINKED`. For link/replace/unlink, audit `TargetEntityType=PROFESSIONAL`, Professional ID, and relevant `TargetUserId`, without email/name/old-new account data.

- [ ] **Step 5: Run and commit**

Run: `dotnet test tests/GestaoPredio.IntegrationTests/GestaoPredio.IntegrationTests.csproj --filter "EligibleProfessionalUserTests|ProfessionalUserLinkTests"`

Commit: `feat: add professional identity links`

---

### Task 10: Validate private-storage configuration and implement fail-safe storage

**Files:**
- Create: `src/GestaoPredio.Application/Abstractions/IPrivateFileStorage.cs`
- Create: `src/GestaoPredio.Application/Files/StagedPrivateFile.cs`
- Create: `src/GestaoPredio.Infrastructure/Files/PrivateFileStorageOptions.cs`
- Create: `src/GestaoPredio.Infrastructure/Files/PrivateFileStorageOptionsValidator.cs`
- Create: `src/GestaoPredio.Infrastructure/Files/FileSystemPrivateFileStorage.cs`
- Create: `src/GestaoPredio.Infrastructure/Files/PrivateFileStorageRegistration.cs`
- Create: `tests/GestaoPredio.UnitTests/PrivateFileStorageOptionsTests.cs`
- Create: `tests/GestaoPredio.UnitTests/FileSystemPrivateFileStorageTests.cs`
- Modify: `recepcaototem/Program.cs`
- Modify: `recepcaototem/appsettings.json`

**Interfaces:**
- Consumes: ASP.NET Core options, host environment/content roots, and filesystem APIs.
- Produces: `IPrivateFileStorage` with `StageAsync`, `CommitAsync`, `OpenReadAsync`, `DeleteAsync`, `DiscardAsync`; validated `PrivateFileStorageOptions`.

- [ ] **Step 1: Write failing options tests**

Reject absent/relative paths, private path equal to or nested under content/web roots, content/web roots nested under the private path, non-writable roots, and size limits outside 1 byte through 10 MiB. Require the root to exist in Production; allow controlled subdirectory creation in Development/Testing. Default `ProfessionalPhotoMaxBytes` to 5 MiB.

- [ ] **Step 2: Implement fail-fast validation**

Bind `Storage` options with `ValidateOnStart`. Normalize full paths before checking both overlap directions. Probe create/write/delete using a random filename without logging the path. Register the storage service through Infrastructure.

- [ ] **Step 3: Write failing filesystem tests**

Assert staging uses random names beneath a controlled temp subdirectory on the same root, `FileMode.CreateNew`, bounded streaming, cleanup after cancellation/I/O/validation/database-compensation paths, final move without overwrite, collision retry/fail-closed behavior, and technical ID/key generation independent of client filename.

- [ ] **Step 4: Implement `IPrivateFileStorage`**

Use explicit methods such as:

```csharp
Task<StagedPrivateFile> StageAsync(Stream source, long maximumBytes, CancellationToken ct);
Task<string> CommitAsync(StagedPrivateFile staged, CancellationToken ct);
Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct);
Task<bool> DeleteAsync(string storageKey, CancellationToken ct);
Task DiscardAsync(StagedPrivateFile staged, CancellationToken ct);
```

Keep keys backend-generated, validate keys before resolving paths, perform same-volume atomic move when available, and never overwrite.

- [ ] **Step 5: Run and commit**

Run: `dotnet test tests/GestaoPredio.UnitTests/GestaoPredio.UnitTests.csproj --filter "PrivateFileStorageOptionsTests|FileSystemPrivateFileStorageTests"`

Commit: `feat: add private file storage`

---

### Task 11: Implement strict JPEG, PNG, and WebP validation

**Files:**
- Create: `src/GestaoPredio.Application/Files/ValidatedImage.cs`
- Create: `src/GestaoPredio.Application/Files/IProfessionalPhotoValidator.cs`
- Create: `src/GestaoPredio.Infrastructure/Files/ProfessionalPhotoValidator.cs`
- Create: `src/GestaoPredio.Infrastructure/Files/ImageParsers/JpegParser.cs`
- Create: `src/GestaoPredio.Infrastructure/Files/ImageParsers/PngParser.cs`
- Create: `src/GestaoPredio.Infrastructure/Files/ImageParsers/WebPParser.cs`
- Create: `tests/GestaoPredio.UnitTests/ProfessionalPhotoValidatorTests.cs`
- Add: `tests/GestaoPredio.UnitTests/Fixtures/Images/*`

**Interfaces:**
- Consumes: staged seekable streams/files from `IPrivateFileStorage` and approved filename/MIME metadata.
- Produces: `IProfessionalPhotoValidator.ValidateAsync(...)` returning `ValidatedImage(MimeType, Length, Width, Height)` or a single invalid result.

- [ ] **Step 1: Add minimal valid and malformed fixtures**

Use small licensed/generated fixtures for JPEG, PNG, and WebP. Include truncated segments/chunks, invalid PNG CRC, malformed RIFF sizes, zero/oversized dimensions, and bounded seeded pseudo-random inputs.

- [ ] **Step 2: Write failing three-way agreement tests**

Cover `.jpg/.jpeg + image/jpeg + JPEG`, `.png + image/png + PNG`, `.webp + image/webp + WebP`; mismatched extension/MIME/binary; missing filename/extension; traversal filename; uppercase extension and case-insensitive MIME; multiple dots including `dra.ana.png`, `perfil.2026.jpg`, and `foto.exe.png` accepted when the final extension and content agree; `.exe` rejected. Every external failure maps to `INVALID_PROFESSIONAL_PHOTO`.

- [ ] **Step 3: Implement bounded structural parsers**

Use `Path.GetFileName` only to isolate the logical name and `Path.GetExtension` only for the final allowlist. Do not persist or log any filename. Derive persisted MIME exclusively from the parser. Validate dimensions ≤4096 each and total pixels ≤16,777,216. Parse JPEG segments through a valid SOF, PNG signature/IHDR/chunk boundaries/CRC, and WebP RIFF plus VP8/VP8L/VP8X structure. Do not add a heavyweight image library.

- [ ] **Step 4: Run and commit**

Run: `dotnet test tests/GestaoPredio.UnitTests/GestaoPredio.UnitTests.csproj --filter ProfessionalPhotoValidatorTests`

Commit: `feat: validate professional photo formats`

---

### Task 12: Implement professional photo endpoints and compensation

**Files:**
- Create: `recepcaototem/Features/Professionals/ProfessionalPhotoEndpoints.cs`
- Create: `tests/GestaoPredio.IntegrationTests/ProfessionalPhotoTests.cs`
- Create: `tests/GestaoPredio.IntegrationTests/ProfessionalPhotoFailureTests.cs`
- Modify: `recepcaototem/Program.cs`

**Interfaces:**
- Consumes: Professional EF model/token handling, `IPrivateFileStorage`, `IProfessionalPhotoValidator`, audit constants, and antiforgery.
- Produces: GET/PUT/DELETE `/api/admin/professionals/{id:guid}/photo` with safe streaming and compensation behavior.

- [ ] **Step 1: Write failing endpoint tests**

Cover 401/403, antiforgery, max request size, all valid/mismatched file cases, upload/replace/remove, missing/invalid/stale token, concurrent replacement, new token, audit redaction, and no partial success audit. Ensure no filename, storage key, path, bytes, or MIME appears in audit/log assertions.

- [ ] **Step 2: Implement PUT photo**

Receive multipart file plus `concurrencyToken`. Stage bytes with a bounded stream, validate final extension + declared MIME + binary format, atomically commit to a new random storage key, then begin the database transaction. Revalidate Professional rowversion, add `PrivateFile(PROFESSIONAL_PHOTO)`, update `PhotoFileId`, add success audit, save, and commit. On any pre-commit or database failure, discard temporary and new final bytes and leave the previous reference untouched.

- [ ] **Step 3: Implement post-commit old-file cleanup**

After the new reference commits, attempt old-byte deletion. Only if bytes were removed, delete old metadata in a separate safe operation. If either cleanup step fails, keep the valid new professional state and log only old `PrivateFile.Id` plus correlation ID. Do not add a worker.

- [ ] **Step 4: Implement DELETE photo**

Validate token first. Clear the reference and commit the audit transaction. Then apply the same bytes-first, metadata-second cleanup. No photo with a current token is a no-op; a stale token remains `RESOURCE_MODIFIED`.

- [ ] **Step 5: Implement GET photo**

Require `Operations`. Validate Professional, current PhotoFile link, metadata, purpose, physical presence, and stored length. Return 404 for missing Professional/photo and `503 PHOTO_UNAVAILABLE` for metadata/storage inconsistency. Set stored validated MIME, `X-Content-Type-Options: nosniff`, `Content-Disposition: inline`, and `Cache-Control: private, no-store`.

- [ ] **Step 6: Run and commit**

Run: `dotnet test tests/GestaoPredio.IntegrationTests/GestaoPredio.IntegrationTests.csproj --filter "ProfessionalPhotoTests|ProfessionalPhotoFailureTests"`

Commit: `feat: add professional photo operations`

---

### Task 13: Extend the React API layer and money handling

**Files:**
- Modify: `recepcaototem/ClientApp/src/api/client.ts`
- Modify: `recepcaototem/ClientApp/src/api/client.test.ts`
- Create: `recepcaototem/ClientApp/src/api/modules.ts`
- Create: `recepcaototem/ClientApp/src/api/modules.test.ts`
- Create: `recepcaototem/ClientApp/src/features/rooms/money.ts`
- Create: `recepcaototem/ClientApp/src/features/rooms/money.test.ts`

**Interfaces:**
- Consumes: existing same-origin `fetch` client and `/api/auth/csrf` contract.
- Produces: typed module DTOs, `apiClient.get/post/put/delete/putMultipart`, `ApiError`, `parseRoomRate(string)`, and `formatBrl(number)`.

- [ ] **Step 1: Write failing client tests**

Assert typed GET with `AbortSignal`, CSRF-aware POST/PUT/DELETE, multipart PUT without manually overriding its boundary, stable API error decoding, and CSRF retry only when the token itself is invalid. Mutations are not auto-aborted; callers prevent duplicate submission and reload after uncertain outcomes.

- [ ] **Step 2: Extend `apiClient`**

Keep same-origin credentials and in-memory CSRF. Add query serialization, `put`, `delete`, and multipart methods. Return an `ApiError` containing HTTP status, stable code, and safe message.

- [ ] **Step 3: Write failing money tests**

Cover strict Brazilian form parsing, invalid grouping/three decimal digits, BRL display, and round-trip values `0`, `0.01`, `0.10`, `100.99`, and `9999999999999.99` as JSON numbers. Assert requests never contain currency strings and do not transform a value unnecessarily before resubmission.

- [ ] **Step 4: Implement money helpers**

Use a strict form parser with the application maximum and `Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' })`. Do not add financial calculations or claim IEEE-754 decimal exactness.

- [ ] **Step 5: Run and commit**

Run: `npm test -- --run src/api/client.test.ts src/api/modules.test.ts src/features/rooms/money.test.ts`

Working directory: `recepcaototem/ClientApp`

Commit: `feat: add modules web api client`

---

### Task 14: Replace the Professionals mock screen with the real workflow

**Files:**
- Rewrite: `recepcaototem/ClientApp/src/pages/admin/Professionals.tsx`
- Create: `recepcaototem/ClientApp/src/pages/admin/Professionals.test.tsx`
- Create: `recepcaototem/ClientApp/src/features/professionals/ProfessionalForm.tsx`
- Create: `recepcaototem/ClientApp/src/features/professionals/ProfessionalPhotoEditor.tsx`
- Create: `recepcaototem/ClientApp/src/features/professionals/ProfessionalUserLink.tsx`
- Create: `recepcaototem/ClientApp/src/hooks/useDebouncedValue.ts`
- Modify: `recepcaototem/ClientApp/src/styles.css`

**Interfaces:**
- Consumes: Task 13 API client/DTOs and all Professional endpoints from Tasks 6, 7, 9, and 12.
- Produces: API-backed `Professionals`, `ProfessionalForm`, `ProfessionalPhotoEditor`, `ProfessionalUserLink`, and `useDebouncedValue` components.

- [ ] **Step 1: Write failing UI tests**

Cover initial loading, empty/error/zero-result states, 300–400 ms debounce, page reset on search/status changes, stale read cancellation, server-driven pagination, create/edit/status, current token propagation, conflict message plus reload, E.164 display/input behavior, photo preview and object URL revocation, upload/remove, and no room field or AppStore import.

Use role-aware tests: Admin and Gerente see cadastral/photo actions; only Admin sees the link-account action and endpoints. The general screen displays only `hasLinkedUser`, never linked email/user ID.

- [ ] **Step 2: Implement list and cadastral dialogs**

Preserve the existing shell and visual language. Source all data from `/api/admin/professionals`; do not filter locally as the source of truth. Keep record identity strictly by GUID. Use explicit activate/deactivate actions and refresh the row/token after success.

- [ ] **Step 3: Implement photo and Identity dialogs**

Accept `.jpg,.jpeg,.png,.webp`; send the last concurrency token; revoke preview object URLs on replace/unmount. Admin eligible-user search is paged and separate from the general Professional DTO. On `RESOURCE_MODIFIED`, explain that another operation changed the record and reload it without auto-merge.

- [ ] **Step 4: Run and commit**

Run: `npm test -- --run src/pages/admin/Professionals.test.tsx`

Commit: `feat: connect professionals screen to api`

---

### Task 15: Replace the Rooms mock screen with the real workflow

**Files:**
- Rewrite: `recepcaototem/ClientApp/src/pages/admin/Rooms.tsx`
- Create: `recepcaototem/ClientApp/src/pages/admin/Rooms.test.tsx`
- Create: `recepcaototem/ClientApp/src/features/rooms/RoomForm.tsx`
- Modify: `recepcaototem/ClientApp/src/styles.css`

**Interfaces:**
- Consumes: Task 13 room API DTOs/money helpers and Task 8 endpoints.
- Produces: API-backed `Rooms` and `RoomForm` components with no occupancy, lease, or Professional coupling.

- [ ] **Step 1: Write failing UI tests**

Cover server pagination/search/status, debounce and stale reads, create/edit/status, BRL display, strict rate form conversion to JSON numbers, all approved round-trip money values, name conflict distinct from concurrency conflict, new token after success, and no occupancy/professional/lease/AppStore concepts.

- [ ] **Step 2: Implement the real Rooms screen**

Render approved fields only: name, description, hourly/daily rates, status, timestamps as useful, and actions. Preserve the card/panel styling but remove occupied/available claims and direct professional linkage. Use `/api/admin/rooms` exclusively.

- [ ] **Step 3: Run and commit**

Run: `npm test -- --run src/pages/admin/Rooms.test.tsx`

Commit: `feat: connect rooms screen to api`

---

### Task 16: Isolate development mocks and disable out-of-scope production modules

**Files:**
- Create: `recepcaototem/ClientApp/src/components/ModuleUnavailable.tsx`
- Create: `recepcaototem/ClientApp/src/components/ModuleUnavailable.test.tsx`
- Create: `recepcaototem/ClientApp/src/dev/DevelopmentAppStore.tsx`
- Move: `recepcaototem/ClientApp/src/store/AppStore.tsx` → `recepcaototem/ClientApp/src/dev/AppStore.tsx`
- Move: `recepcaototem/ClientApp/src/data/mock.ts` → `recepcaototem/ClientApp/src/dev/mock.ts`
- Modify: `recepcaototem/ClientApp/src/main.tsx`
- Modify: `recepcaototem/ClientApp/src/App.tsx`
- Modify: `recepcaototem/ClientApp/src/pages/Reception.tsx`
- Modify: `recepcaototem/ClientApp/src/pages/admin/Dashboard.tsx`
- Modify: `recepcaototem/ClientApp/src/pages/admin/Leases.tsx`
- Modify: `recepcaototem/ClientApp/src/pages/admin/Visits.tsx`
- Modify: `recepcaototem/ClientApp/src/pages/admin/Settings.tsx`
- Create: `recepcaototem/ClientApp/src/production-isolation.test.tsx`
- Create: `recepcaototem/ClientApp/scripts/verify-production-bundle.mjs`
- Modify: `recepcaototem/ClientApp/package.json`

**Interfaces:**
- Consumes: current route shell and completed API-backed Professionals/Rooms components.
- Produces: `ModuleUnavailable`, development-only AppStore entry point, and `verify-production-bundle.mjs` enforcing the production module graph.

- [ ] **Step 1: Write failing environment-isolation tests**

Assert Professionals/Rooms have no AppStore imports. In production mode, Dashboard, Reception, Leases, Visits, and Settings render `Módulo ainda não disponível`; routes/navigation remain. Assert AppStoreProvider is absent, localStorage mock keys are untouched, and no external env/toggle can enable mocks.

- [ ] **Step 2: Move mocks behind a static development boundary**

Place all mock imports in `src/dev`. Load the development provider and demonstrative route components only through an `import.meta.env.DEV` branch that Vite can remove. The production branch uses `ModuleUnavailable` directly. Do not import `src/dev/mock.ts` from any module shared with production.

- [ ] **Step 3: Add a structural bundle verifier**

Generate the production manifest/metafile and assert the production entry graph has no `src/dev`, `AppStore`, or mock dataset module. Also assert known operational mock records do not appear in emitted JS and no production code writes the mock localStorage keys. Avoid treating a blind text scan alone as proof; inspect the module graph and then use content checks as defense in depth.

- [ ] **Step 4: Run and commit**

Run: `npm test -- --run src/production-isolation.test.tsx src/components/ModuleUnavailable.test.tsx`

Run: `npm run build`

Run: `node scripts/verify-production-bundle.mjs`

Commit: `feat: isolate demo modules from production`

---

### Task 17: Generate SQL artifacts and update operational documentation

**Files:**
- Create: `artifacts/sql/ProfessionalsAndRooms.sql`
- Create: `artifacts/sql/idempotent-current.sql`
- Create: `artifacts/sql/SHA256SUMS.txt`
- Modify: `README.md`
- Modify: `PROJECT_CONTEXT.md`
- Modify: `docs/operations/authentication-deployment.md`
- Modify: `docs/operations/configuration.md`
- Create: `docs/operations/professionals-rooms-production-migration.md`
- Modify: `tests/GestaoPredio.IntegrationTests/MigrationSafetyTests.cs`
- Modify: `tests/GestaoPredio.IntegrationTests/PublishContentsTests.cs`

**Interfaces:**
- Consumes: migration `ProfessionalsAndRooms`, completed application, and production bundle verifier.
- Produces: reviewed forward/idempotent SQL plus hashes, migration/deployment runbook, and publish-content safety assertions.

- [ ] **Step 1: Generate, never apply, SQL**

Run:

```powershell
dotnet ef migrations script 20260904235115_AuthenticationAndProvisioning ProfessionalsAndRooms --project src/GestaoPredio.Infrastructure/GestaoPredio.Infrastructure.csproj --startup-project recepcaototem/recepcaototem.csproj --output artifacts/sql/ProfessionalsAndRooms.sql
dotnet ef migrations script --idempotent --project src/GestaoPredio.Infrastructure/GestaoPredio.Infrastructure.csproj --startup-project recepcaototem/recepcaototem.csproj --output artifacts/sql/idempotent-current.sql
```

Compute SHA-256 for both files into `SHA256SUMS.txt`. Do not connect to any database during script generation.

- [ ] **Step 2: Review SQL and encode destructive-operation checks**

Confirm the forward script contains only additive tables, columns, constraints, and indexes. Search case-insensitively outside comments/generated migration-history guards for `DROP`, `TRUNCATE`, `DELETE`, `ALTER DATABASE`, `COLLATE`, cascade actions, and changes to AspNet tables. Fail tests on any unapproved occurrence. Document that production executes only reviewed forward SQL and never migration `Down`, `database update <previous>`, or startup migration.

- [ ] **Step 3: Document required external settings and permissions**

Document `Storage__PrivateFilesPath` and optional `Storage__ProfessionalPhotoMaxBytes` (default 5 MiB, hard cap 10 MiB), private path placement outside IIS `wwwroot` and publish directory, and Modify permission only for `SOPH-SISPONTO\LumisApi` on that private root. Do not prescribe increased SQL, IIS, firewall, binding, or application-pool permissions. State that any production change requires a separate reviewed procedure and approval.

- [ ] **Step 4: Extend publish-content tests**

Assert the IIS publish contains API/runtime and React production assets, excludes AdminCli, source maps, `src/dev`, mock datasets, SQL scripts, private files, and secrets, and contains no mechanism that can re-enable mocks.

- [ ] **Step 5: Run and commit**

Run: `dotnet test tests/GestaoPredio.IntegrationTests/GestaoPredio.IntegrationTests.csproj --filter "MigrationSafetyTests|PublishContentsTests"`

Commit: `docs: add modules migration and deployment procedure`

---

### Task 18: Full local verification and review

**Files:**
- Modify only files required by failures found during verification.
- Create: `artifacts/verification/professionals-rooms-summary.md`

**Interfaces:**
- Consumes: every prior task, both test databases, SQL artifacts, and local publish output.
- Produces: the final local verification record and review-ready branch, stopping before all production operations.

- [ ] **Step 1: Run formatting and static checks**

Run:

```powershell
dotnet format recepcaototem.sln --verify-no-changes
npm run build
```

Working directory for npm: `recepcaototem/ClientApp`.

- [ ] **Step 2: Run all backend tests against guarded test databases**

Run: `dotnet test recepcaototem.sln --no-restore`

Before accepting results, capture the resolved database catalogs from test fixture diagnostics and verify they match only `GestaoPredioAuthTests*` and `GestaoPredioModulesTests*`, never `GestaoPredioDB`.

- [ ] **Step 3: Run all React tests**

Run: `npm test -- --run`

Expected: all tests pass with no unhandled promise rejection or object-URL leak warning.

- [ ] **Step 4: Generate a local IIS publish**

Run:

```powershell
dotnet publish recepcaototem/recepcaototem.csproj -c Release -o artifacts/publish/LumisApi
```

Inspect the manifest and re-run the production bundle verifier. Confirm AdminCli and every dev/mock artifact are absent.

- [ ] **Step 5: Request code review and fix findings with TDD**

Use `superpowers:requesting-code-review`. Classify each finding against the approved spec, reproduce valid defects with a failing test, make the smallest correction, and re-run the focused then full suites.

- [ ] **Step 6: Write the verification summary**

Record exact build result, passed/failed test totals, SQL artifact hashes, destructive-operation analysis, publish file manifest, confirmation that AdminCli/mocks are absent, and the commits included. State clearly that no production database, IIS site, server, firewall, binding, permission, or bootstrap operation was performed.

- [ ] **Step 7: Commit final verification artifacts**

Commit: `test: verify professionals and rooms delivery`

Stop before production. Present the verified artifacts and wait for explicit approval of a separately reviewed production migration/deployment procedure.



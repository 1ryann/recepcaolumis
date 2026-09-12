# Lumis — Professional Area Completion & Professional Photo

**Status:** especificação final para revisão. Nenhuma implementação, migration, push, deploy ou alteração de banco nesta etapa.

**Branch:** `codex/reception-backend` (worktree `.worktrees/reception-backend`). HEAD no momento da escrita: `f88fcc2c79f0da898ae06b135b4e232d7f165398`.

**Escopo:** finalizar a Área do Profissional — Dashboard, Agenda, Disponibilidade, Atendimentos, Reservas, Locações, Financeiro, Meu Perfil — substituindo todos os placeholders "Estamos preparando esta área" por páginas reais sobre dados reais, e adicionar o autoatendimento de perfil + foto do profissional (WhatsApp, descrição, foto).

**Princípio arquitetural (não negociável):** reutilizar o backend existente sempre que possível. Nova infraestrutura só para lacunas reais. A inspeção abaixo mostra que a maior parte do que este documento precisa **já existe e funciona** — o trabalho novo é menor do que o pedido original presumia.

**Fora de escopo (explícito):** chat cliente/profissional; prontuário; documentos; emissão de recibos; pagamentos online; integração Google/Outlook Calendar; edição de contrato de locação pelo profissional; notificações internas complexas; white/light mode; qualquer status paralelo aos já existentes no domínio; calendário mensal complexo na Agenda (fase futura); qualquer operação remota (push, deploy, migration aplicada) — esta spec não autoriza nenhuma delas.

---

## 1. Estado atual investigado — backend

Tabela de contratos reais, com todos os parâmetros de filtro/paginação já suportados. **Nenhum destes é substituído; todos são reutilizados como estão**, salvo onde marcado "extensão aditiva".

| Módulo | Rota real | Auth | Filtros já suportados | Arquivo |
|---|---|---|---|---|
| Reservas | `GET /api/professional/reservations` | `"Professional"` | `page`, `pageSize` (1-100), `status` (`all\|PENDING\|APPROVED\|REJECTED\|CANCELLED`), `from`, `to`, `orderBy` (`asc\|desc`, padrão `desc`) | `recepcaototem/Features/Reservations/ProfessionalReservationEndpoints.cs:21-82` |
| Reservas (detalhe) | `GET /api/professional/reservations/{id:guid}` | `"Professional"` | — | idem, 22, 84-102 |
| Reservas (nova) | `POST /api/professional/reservations` | `"Professional"` | body `RequestReservationRequest(RoomId, StartAt, EndAt)` | idem, 23, 104-157 |
| Reservas (remarcar) | `POST /api/professional/reservations/{id}/reschedule-request` | `"Professional"` | body `RescheduleReservationRequest(StartAt, EndAt, ConcurrencyToken)` | idem, 24, 195-276 |
| Reservas (cancelar) | `POST /api/professional/reservations/{id}/cancel-request` | `"Professional"` | body `ReservationConcurrencyRequest(ConcurrencyToken)` | idem, 25, 195-276 |
| Atendimentos | `GET /api/professional/visits` | `"Professional"` | `page`, `pageSize`, `status`, `from`, `to` | `recepcaototem/Features/Visits/VisitEndpoints.cs:32, 45-61` |
| Atendimentos (detalhe) | `GET /api/professional/visits/{id:guid}` | `"Professional"` | — | idem, 33 |
| Atendimentos (iniciar) | `POST /api/professional/visits/{id}/start` | `"Professional"` | body `VisitConcurrencyRequest(ConcurrencyToken)` | idem, 34, 232-250 |
| Atendimentos (encerrar) | `POST /api/professional/visits/{id}/end` | `"Professional"` | body `VisitConcurrencyRequest(ConcurrencyToken)` | idem, 35, 232-250 |
| Atendimentos (cancelar) | `POST /api/professional/visits/{id}/cancel` | `"Professional"` | body `VisitConcurrencyRequest(ConcurrencyToken)` | idem, 36, 232-250 |
| Locações | `GET /api/professional/leases` | `"Professional"` | `page`, `pageSize` (**sem `status` hoje** — ver §9) | `recepcaototem/Features/Leases/ProfessionalLeaseEndpoints.cs:14, 19-52` |
| Locações (detalhe) | `GET /api/professional/leases/{id:guid}` | `"Professional"` | — | idem, 15, 54-73 |
| Financeiro | `GET /api/professional/finance/charges` | `"Professional"` | `page`, `pageSize`, `status` (`all\|PENDING\|OVERDUE\|PAID\|CANCELLED`), `referenceFrom`, `referenceTo` (`DateOnly?`) | `recepcaototem/Features/Finance/FinanceEndpoints.cs:26, 37-44` |
| Financeiro (detalhe) | `GET /api/professional/finance/charges/{id:guid}` | `"Professional"` | — | idem, 27, 91-103 |
| Perfil (leitura) | `GET /api/professional/me` | `"Professional"` | — (retorna objeto anônimo hoje, ver §12) | `recepcaototem/Features/Professionals/ProfessionalProfileEndpoints.cs:13-28` |
| Perfil (foto, leitura) | `GET /api/professional/me/photo` | `"Professional"` | — | idem, 29-38 |
| Disponibilidade | módulo completo já implementado (`INHERIT_GLOBAL`/`CUSTOM`, múltiplos intervalos, `effectiveDays`, exceções, fail-closed) | `"Professional"` | — | `docs/superpowers/specs/2026-09-07-professional-availability-design.md` (spec original) |

**Conclusão da tabela:** Reservas, Atendimentos, Locações, Financeiro e a leitura de Disponibilidade **já têm todo o backend necessário**. O trabalho pendente nesses cinco módulos é **100% frontend** (substituir `ProfessionalPlaceholder` por páginas reais consumindo os endpoints acima).

### 1.1 Enums reais (não inventar nomenclatura paralela)

- `ReservationStatus` (`src/GestaoPredio.Domain/Reservations/ReservationStatus.cs`): `Pending, Approved, Rejected, Cancelled` — inglês, serializado em maiúsculas.
- `VisitStatus` (`src/GestaoPredio.Domain/Visits/VisitStatus.cs:3-9`): `Waiting, InService, Ended, Cancelled` → wire `WAITING`, `IN_SERVICE`, `ENDED`, `CANCELLED`. **Não existe `AGUARDANDO`/`EM_ATENDIMENTO`/`ENCERRADA` no código.** A máquina de estados real (`src/GestaoPredio.Domain/Visits/Visit.cs:26-94`): `Arrive→Waiting`, `StartService` (requer `Waiting`) `→InService`, `End` (requer `InService`) `→Ended`, `Cancel` (de `Waiting`/`InService`) `→Cancelled`, mais `Correct(...)` admin-only.
- `LeaseLifecycleState`: `Open, EndingPending, Ended, Cancelled` (interno). O status **exposto na API já é computado e em português**: `AGENDADA, ATIVA, ENCERRAMENTO_PENDENTE, ENCERRADA, CANCELADA` (`Lease.GetOperationalStatus(now).ToContract()`, `LeaseEndpoints.cs:19`). Esta inconsistência de idioma entre módulos (Reservas/Atendimentos em inglês, Locações em português) **já existe hoje e não é escopo desta spec corrigir**.
- `FinancialChargeStatus`: `Pending = 1, Paid = 2, Cancelled = 3`. **Não existe membro `Overdue` no enum** — `"OVERDUE"` é calculado só na resposta da API quando `Pending && DueDate < hoje` (`FinanceEndpoints.cs:182`, `FinancialCharge.IsOverdue`).

### 1.2 Infra compartilhada a reutilizar

- **`WhatsAppNormalizer.TryNormalize(string? input, out string canonical)`** (`src/GestaoPredio.Domain/Professionals/WhatsAppNormalizer.cs`) — já usado pelo cadastro público de profissional. Aceita E.164 (`+55...`) ou número BR formatado com 10-11 dígitos; normaliza para `+55<nacional>`.
- **Limite de `Description`**: 500 caracteres, rejeita `<`/`>` — já aplicado em `Professional.NormalizeDescription` (`Professional.cs:119-126`) e replicado em `ProfessionalInput.TryValidate` (`ProfessionalContracts.cs:36-66`). **Reutilizar a mesma regra**, não redefinir o limite.
- **Padrão de rate limiter**: `ProfessionalPresenceRateLimiter` (`recepcaototem/Features/Professionals/ProfessionalPresenceRateLimiter.cs:11-39`) — classe com dois buckets `PartitionedRateLimiter<string>` (IP + identificador com hash SHA-256), configurados via `RateLimiting:<Bucket>IpPermitLimit`, `RateLimiting:<Bucket>IdentifierPermitLimit`, `RateLimiting:<Bucket>WindowSeconds`. **O novo limiter de upload de foto segue exatamente esta forma.**
- **`IStrictModuleRequest`** (`recepcaototem/Features/Common/StrictBody.cs:7-21`) — marcador que rejeita campos JSON desconhecidos via `JsonUnmappedMemberHandling.Disallow`. Todo DTO de request novo desta spec implementa esta interface — é o mecanismo que garante que `PUT /api/professional/me` **rejeita** `professionalId`/`name`/`profession`/qualquer campo de foto simplesmente por não estarem declarados no tipo.
- **`ConcurrencyToken.Encode(uint)` / `TryDecode(string?, out uint)`** (`recepcaototem/Features/Common/ConcurrencyToken.cs`) — codec Base64 de `Professional.Version` (uint). Reutilizado por todo endpoint de mutação nesta spec.
- **Upload multipart**: único precedente no código é `ProfessionalPhotoEndpoints.ReadUploadAsync` (`ProfessionalPhotoEndpoints.cs:241-304`) — parsing manual via `MultipartReader` com exatamente duas partes nomeadas (`file`, `concurrencyToken`), **sem** `IFormFile`/model binding. O novo endpoint self-serve segue o mesmo parsing, não introduz `[FromForm]`.
- **Resolução do profissional autenticado**: hoje duplicada (LINQ idêntico) em 5 arquivos (`ProfessionalReservationEndpoints.cs:159-167`, `VisitEndpoints.cs:361-364`, `ProfessionalLeaseEndpoints.cs:32-36`, `FinanceEndpoints.cs:186-191`, `ProfessionalProfileEndpoints.cs:15-20`). Esta spec **não exige** consolidar isso num helper único — é dívida técnica pré-existente, fora do escopo funcional pedido. Fica registrado como melhoria oportunista opcional para o plano decidir.

---

## 2. Estado atual investigado — frontend

`recepcaototem/ClientApp/src/App.tsx:54-63`, sob `<Route path="/profissional" element={<ProfessionalShell />}>`:

| Rota | Componente atual | Estado |
|---|---|---|
| index (`/profissional`) | `ProfessionalDashboard` | **real** |
| `agenda` | `ProfessionalAgenda` | **real** (lista simples; vira a base da Agenda com views Hoje/Semana, §6) |
| `reservas` | `ProfessionalPlaceholder title="Reservas"` | placeholder |
| `atendimentos` | `ProfessionalPlaceholder title="Atendimentos"` | placeholder |
| `disponibilidade` | `ProfessionalAvailability` | **real**, completo |
| `locacoes` | `ProfessionalPlaceholder title="Locações"` | placeholder |
| `financeiro` | `ProfessionalPlaceholder title="Financeiro"` | placeholder |
| `perfil` | `ProfessionalPlaceholder title="Meu perfil"` | placeholder |

`ProfessionalPlaceholder` (`ProfessionalHome.tsx:262`) é o texto literal "Estamos preparando esta área" citado no pedido — **5 rotas** o usam hoje: Reservas, Atendimentos, Locações, Financeiro, Meu Perfil.

`ProfessionalShell` (`ProfessionalHome.tsx:21-86`) já busca `/api/professional/me` + reservas + visitas no mount e expõe via `Outlet context`; já renderiza sidebar/drawer, topbar, `professional-*` classes sobre o shell Lumis escuro (`lumis-shell`/tokens `--lumis-*`). **Este shell é reaproveitado por todas as páginas novas** — nenhuma página cria seu próprio wrapper de layout.

Classes CSS já existentes e reaproveitáveis (prefixo `professional-*`, todas escuras/Lumis): `professional-shell`, `professional-sidebar`, `professional-nav`, `professional-topbar`, `professional-content`, `professional-kpi-grid`/`-card`, `professional-agenda-*`, `professional-section`, `professional-card`, `professional-grid`, `professional-detail`, `professional-empty`, `professional-loading`. Genéricas já escuras: `panel`/`data-table`/`status-badge`/`empty-state` (mesma família usada no Admin Dashboard já migrado para Lumis).

---

## 3. Divergências encontradas entre o pedido original e o código real

Esta seção existe porque a instrução foi explícita: parar e reportar em vez de inventar solução quando há conflito. Nenhuma das divergências abaixo bloqueia a fase — todas têm resolução por reutilização, listada ao lado.

| # | Pedido original assumia | Realidade encontrada | Resolução adotada nesta spec |
|---|---|---|---|
| D1 | Nenhum endpoint de perfil profissional existe | `GET /api/professional/me` e `GET /api/professional/me/photo` **já existem** | Adicionar somente `PUT /api/professional/me` (não `PUT /api/professional/profile` — segue o prefixo `/me` já estabelecido) |
| D2 | Nenhum armazenamento de foto existe; propunha `PhotoFileName`, `PhotoVersion`, `PhotoUpdatedAt` (3 colunas novas) + `IProfessionalPhotoStorage`/`FileSystemProfessionalPhotoStorage` novos | Mecanismo completo já existe: `Professional.PhotoFileId (Guid?)` → `PrivateFile` (`Id`, `StorageKey`, `MimeType`, `Length`, `Purpose`, `CreatedAt`), atrás de `IPrivateFileStorage`/`FileSystemPrivateFileStorage` já com o ciclo stage→commit→cleanup-on-failure pedido | **Nenhuma coluna nova.** `PhotoFileId` já é o "nome de arquivo interno seguro" (não é nome de arquivo, é um Guid — mais seguro ainda). `PrivateFile.Id` (novo a cada upload) já serve como token de versão. `Professional.UpdatedAt` (já bumped por `SetPhoto`/`RemovePhoto`) já serve como timestamp. Ver §13. |
| D3 | Locações/Financeiro do profissional não existem no backend | Ambos **já existem**, corretamente `scoped` à identidade autenticada (`ApplicationUserId`), sem aceitar `professionalId` do cliente | Reutilizar integralmente; só frontend novo |
| D4 | Cache versionado (`?v=N`, `immutable, max-age=31536000`) para a foto no Totem | Já existe um endpoint público (`GET /api/totem/professionals/{id}/photo`, `AllowAnonymous`), mas com `Cache-Control: public, max-age=300` (decisão deliberada documentada na spec `2026-09-09`, para equilibrar atualização e performance do carrossel) | Ver §13.5 — trocar para cache-buster versionado é uma mudança aditiva de 1 linha no call site existente (o helper `ProfessionalPhotoStreaming.StreamAsync` já recebe `cacheControl` por parâmetro), não uma reescrita. Resolve "aparece imediatamente" melhor do que o TTL de 5 min atual. |
| D5 | Erros propostos: `INVALID_IMAGE`, `IMAGE_TOO_LARGE`, `INVALID_WHATSAPP`, `DESCRIPTION_TOO_LONG` | Convenção real já usa códigos únicos e genéricos: `INVALID_PROFESSIONAL_PHOTO` (400, qualquer falha de validação de imagem), `INVALID_PROFESSIONAL` (400, qualquer falha de validação de Nome/Profissão/WhatsApp/Descrição), `PHOTO_UNAVAILABLE` (503, falha de storage) | Reutilizar os 3 códigos existentes em vez de criar 4 novos granulares. Ver §16. |
| D6 | Layout de storage proposto: `/data/private/professional-photos/{professionalId}/{generated-id}.webp` | Layout real: `{Storage:PrivateFilesPath}/files/{32-hex-guid}` — chave plana, sem subpasta por entidade, sem extensão no nome, purpose/dono controlado só no banco (`PrivateFile.Purpose` + `Professional.PhotoFileId`) | Reutilizar o layout real — na prática mais seguro (nenhum `professionalId` aparece em caminho de arquivo) |
| D7 | Locações: filtro `status` esperado no request | Endpoint real só aceita `page`/`pageSize` hoje | Extensão aditiva: acrescentar `status?` opcional ao endpoint existente (ver §9) — não é endpoint novo, é querystring nova numa rota já real |
| D8 | Nenhuma biblioteca de processamento de imagem no projeto | Confirmado: nenhuma (`ImageSharp`/`SkiaSharp`/etc. ausentes em todos os `.csproj`). O validador atual (`ProfessionalPhotoValidator`, parsers manuais JPEG/PNG/WebP) só extrai dimensões/valida magic bytes — **não redimensiona nem recomprime** | Esta é a única lacuna real de infraestrutura desta spec. Ver §13.3 — escolha de biblioteca fica para o plano, conforme já instruído. |

---

## 4. Componentes de frontend compartilhados

Só extrair o que já se repete de fato. Avaliação concreta por componente sugerido no pedido:

| Componente | Extrair? | Justificativa |
|---|---|---|
| `ProfessionalPageHeader` | Sim | Título + descrição + eyebrow se repete idêntico nas 6 páginas novas/reformadas — hoje cada `ProfessionalPlaceholder` já centraliza isso parcialmente; formalizar como componente único usado por todas |
| `ProfessionalSurface` | Não como componente novo — **reutilizar `.professional-card`/`.panel`** já existentes no CSS. Não introduzir uma segunda classe de card concorrente. |
| `ProfessionalEmptyState` | Não como componente novo — **reutilizar `<EmptyState>`** (`src/components/PageElements.tsx`, já usado no Admin Dashboard escuro) |
| `ProfessionalFilterBar` | Sim, mas mínimo — um wrapper de layout (flex-wrap) para os poucos filtros de Reservas/Locações/Financeiro; sem lógica de estado própria |
| `ProfessionalTable` | Não como componente novo — **reutilizar `.data-table`** já existente e já escurecido; cada página passa suas próprias colunas |
| `StatusBadge` | Sim — hoje `.status-badge` existe como classe CSS mas cada página monta o `<span>` na mão com classes diferentes por status; um componente `<StatusBadge tone="..." label="..."/>` fino elimina repetição real entre Reservas/Atendimentos/Locações/Financeiro, cada um com seu próprio mapa `status→tom` |

Não criar um design system novo. O objetivo é 2 componentes genuinamente novos (`ProfessionalPageHeader`, `StatusBadge`) + 1 wrapper de layout (`ProfessionalFilterBar`), todo o resto reaproveita classes/componentes já escuros.

---

## 5. Dashboard

Já real (`ProfessionalDashboard`, `ProfessionalHome.tsx:138-254`). **Nenhuma mudança funcional nesta fase.** Se o self-review de UI encontrar alguma inconsistência visual pontual (ex.: um card ainda usando token errado), corrigir como ajuste de CSS isolado, não como redesenho.

---

## 6. Agenda

**Backend:** nenhum novo. Reutilizar `GET /api/professional/reservations?from&to&orderBy=asc` e `GET /api/professional/visits?from&to`.

**Frontend:** composição pura de dados reais, sem calendário mensal.

Views:
- **Hoje** — `from`/`to` = início/fim do dia civil do profissional; junta `Reservation` (status `Approved`, futuras ou em curso) com `Visit` (quando existir, via `ReservationId` ou correlação de horário+sala) para derivar o estado operacional exibido.
- **Semana** — mesma composição com `from`/`to` = semana corrente; agrupada por dia.

Colunas exibidas: horário, cliente (nome — ver nota de PII abaixo), sala, estado operacional.

**Derivação do estado operacional (não criar status paralelo — mapear os enums reais):**

| Situação real | Estado exibido |
|---|---|
| `Reservation.Status == Approved`, `StartAt` futuro, sem `Visit` correspondente | Agendado |
| `Visit.Status == Waiting` | Aguardando |
| `Visit.Status == InService` | Em atendimento |
| `Visit.Status == Ended` | Encerrado |
| `Visit.Status == Cancelled` ou `Reservation.Status == Cancelled` | Cancelado |

Nota: o nome do cliente não é exposto pelo DTO de reserva do profissional hoje (`ReservationResponse` não tem campo de cliente — ver contrato em §1). **Se a Agenda precisar exibir nome do cliente**, isso é uma extensão aditiva de `ReservationResponse` (novo campo `CustomerName?`, projetado a partir de `Reservation.CustomerId` já existente) a confirmar no plano — não um novo endpoint. Alternativa sem mudança de contrato: exibir `VisitorName` de `Visit` quando o atendimento já existir, e "Cliente" genérico enquanto só há reserva. Decisão de UX fica para o plano.

---

## 7. Reservas

**Backend:** nenhum novo. Reutilizar `GET /api/professional/reservations` (paginação + filtros já existentes) e as duas ações já reais (`.../reschedule-request`, `.../cancel-request`).

**Frontend:** tabela (`.data-table`) com filtros por `status` e intervalo de data (`ProfessionalFilterBar`). Ações por linha:
- **Remarcar** → chama `POST /api/professional/reservations/{id}/reschedule-request` (já existe) — abrir em drawer/modal escuro reaproveitando `.modal-*` já escurecido para o contexto do Totem/kiosk (ver trabalho de tema escuro já feito no Admin Dashboard) ou um painel lateral simples.
- **Cancelar** → `POST /api/professional/reservations/{id}/cancel-request` (já existe).
- **Nenhuma outra ação** é exibida — não há endpoint de aprovação/rejeição pelo lado do profissional (isso é fluxo administrativo), então nenhum botão correspondente aparece.

Detalhe (drawer/modal): campos do `ReservationResponse` já retornado — sala, horário, status, `ConcurrencyToken` (necessário para as duas ações).

---

## 8. Atendimentos

**Backend:** nenhum novo. Reutilizar `GET /api/professional/visits` e `POST .../start`, `.../end`, `.../cancel` (todos já existem e já validam propriedade via `MutateOwned`, `VisitEndpoints.cs:232-250`).

**Frontend:** três seções/filtros sobre a mesma lista:
- **Aguardando** — `status=WAITING`
- **Em atendimento** — `status=IN_SERVICE`
- **Encerrados hoje** — `status=ENDED`, `from`/`to` = dia corrente

Ações por item, condicionadas ao estado real (não adicionar rótulo de ação que não tem endpoint):
- `Waiting` → botão "Iniciar atendimento" → `POST /start`
- `InService` → botão "Encerrar atendimento" → `POST /end`
- Qualquer estado ativo → "Cancelar" → `POST /cancel` (mantém paridade com o que a API já permite)

Nomes de status a exibir ao usuário (rótulo, não o enum): `WAITING` → "Aguardando", `IN_SERVICE` → "Em atendimento", `ENDED` → "Encerrado", `CANCELLED` → "Cancelado" — tradução só na camada de apresentação, o enum de wire continua em inglês.

---

## 9. Locações

**100% read-only**, conforme pedido — nenhuma ação de escrita é renderizada nesta página.

**Backend:** reutilizar `GET /api/professional/leases` (já existe, já scoped ao profissional autenticado via `ApplicationUserId`, nunca aceita `professionalId` do cliente — confirmado em `ProfessionalLeaseEndpoints.cs:32-36`).

**Extensão aditiva (não é endpoint novo):** o endpoint hoje só aceita `page`/`pageSize`. Adicionar parâmetro opcional `status` (mesmos valores computados que já saem na resposta: `AGENDADA|ATIVA|ENCERRAMENTO_PENDENTE|ENCERRADA|CANCELADA`), filtrando no servidor sobre o `GetOperationalStatus` já calculado. Filtro por texto/`status` inexistente hoje é a única lacuna real deste módulo.

**Frontend:** tabela com sala, período (início/fim), status, valor (`ContractedRate`), e as demais informações já presentes em `ProfessionalLeaseResponse` (`Id, TenantName, RoomId, RoomName, Mode, ContractedRate, BillingStartAt, BillingDueDay, OccupancyStartAt, OccupancyEndAt, Status`). Nenhum botão de editar/excluir/renovar/trocar sala.

---

## 10. Financeiro

**100% read-only.**

**Backend:** reutilizar integralmente `GET /api/professional/finance/charges` (já suporta `page`, `pageSize`, `status`, `referenceFrom`, `referenceTo` — cobre exatamente o que o pedido original listava como filtro mínimo, nenhuma extensão necessária).

**Frontend:**
- Resumo no topo: valor em aberto (soma de `FinalAmount` onde status efetivo é `PENDING`/`OVERDUE`), próximo vencimento (menor `DueDate` entre os em aberto).
- Tabela: competência (`ReferencePeriodStart`–`ReferencePeriodEnd`), vencimento (`DueDate`), valor (`FinalAmount`), status (rótulo a partir do `Status` já computado pela API, incluindo `OVERDUE`).
- Nenhuma ação de escrita.

---

## 11. Disponibilidade

**Backend:** nenhuma mudança — módulo já completo (`INHERIT_GLOBAL`/`CUSTOM`, múltiplos intervalos por dia, `effectiveDays`, exceções somente redutoras, fail-closed sem `OperatingHours` — tudo já implementado, ver spec original `2026-09-07-professional-availability-design.md`).

**Frontend:** `ProfessionalAvailability.tsx` já é real e funcional (`AvailabilityEditor` + `ExceptionsEditor`, com tratamento de conflito de concorrência). Trabalho desta fase é só de **coerência visual** com o shell profissional — garantir que usa os mesmos tokens `--lumis-*`/classes `professional-*` que as demais páginas, sem redesenhar a lógica de edição.

---

## 12. Meu Perfil

Campos e regras de edição (exatamente como aprovado):

| Campo | Profissional pode editar? |
|---|---|
| Nome | Não (somente leitura) |
| Especialidade/Profissão | Não (somente leitura) |
| WhatsApp | Sim |
| Descrição | Sim (máx. 500, sem `<`/`>` — regra já existente, reaproveitada) |
| Foto | Sim — ver §13 |

### 12.1 Leitura — extensão aditiva do endpoint existente

`GET /api/professional/me` **já existe** mas retorna hoje um objeto anônimo `{ Name, Profession, Description, HasPhoto, PhotoUrl }` **sem `WhatsApp`**. Como o formulário de edição precisa pré-popular o WhatsApp atual, este é o único campo a acrescentar à resposta (aditivo, sem quebrar consumidores atuais). Formalizar como um DTO nomeado:

```
ProfessionalProfileResponse(
  string Name,
  string Profession,
  string? Description,
  string WhatsApp,
  bool HasPhoto,
  string? PhotoUrl,
  string ConcurrencyToken
)
```

`ConcurrencyToken` também é aditivo — necessário para o `PUT` abaixo (codificado via `ConcurrencyToken.Encode(professional.Version)`, mesmo padrão de todo o resto do sistema).

### 12.2 Escrita — endpoint novo

```
PUT /api/professional/me
Auth: "Professional"
```

Request (`IStrictModuleRequest` — rejeita qualquer campo além destes três):

```
ProfessionalProfileUpdateRequest(
  string WhatsApp,
  string? Description,
  string ConcurrencyToken
)
```

Nunca aceita `ProfessionalId`, `Name`, `Profession`, `PhotoFileId`, ou qualquer variante — esses campos simplesmente não existem no tipo, então `JsonUnmappedMemberHandling.Disallow` os rejeita na desserialização antes de qualquer lógica de negócio rodar.

Validação: reutilizar exatamente `WhatsAppNormalizer.TryNormalize` e a mesma regra de `Description` (≤500, sem `<`/`>`) já aplicada em `Professional.NormalizeDescription`/`ProfessionalInput.TryValidate`. Falha de validação → `400 INVALID_PROFESSIONAL` (mesmo código já usado pelo endpoint admin — não introduzir `INVALID_WHATSAPP`/`DESCRIPTION_TOO_LONG` como códigos novos).

Concorrência: mesmo padrão de `ConcurrencyToken.TryDecode` + `db.Entry(professional).Property(x => x.Version).OriginalValue` já usado em `ProfessionalPhotoEndpoints`/`ProfessionalReservationEndpoints` — token inválido/expirado → `409` (mesmo formato de conflito já usado nesses endpoints, ex. `ProfessionalEndpoints.Modified()`).

Resolução do profissional atual: mesmo padrão `ApplicationUserId == userId && IsActive` já usado nos outros 5 lugares (§1.2) — sem introduzir um sexto padrão diferente.

---

## 13. Foto do profissional

### 13.1 O que já existe e é 100% reutilizado

- Campo `Professional.PhotoFileId (Guid?)` — já existe, não migra.
- Entidade `PrivateFile` (`Id`, `StorageKey`, `MimeType`, `Length`, `Purpose`, `CreatedAt`) — já existe, `Purpose` já restrito a `PrivateFilePurposes.ProfessionalPhoto`.
- `IPrivateFileStorage` (`StageAsync`/`CommitAsync`/`OpenStagedReadAsync`/`OpenReadAsync`/`DeleteAsync`/`DiscardAsync`) + implementação `FileSystemPrivateFileStorage` — já implementa exatamente o ciclo de vida seguro pedido (stage em `.staging/`, commit atômico via `File.Move` para `files/{guid-hex}`, nunca apaga o arquivo antigo antes do novo estar persistido).
- Config `Storage__PrivateFilesPath` (seção `Storage`, já em uso em produção/Railway Volume) e `Storage__ProfessionalPhotoMaxBytes` (default 5 MiB, teto rígido 10 MiB em `PrivateFileStorageOptions.MaximumProfessionalPhotoBytes`) — já batem com o limite de 5 MB pedido.
- Validador `ProfessionalPhotoValidator`/`IProfessionalPhotoValidator` — já rejeita por magic-bytes reais (não confia em extensão/Content-Type), já valida dimensões (1-4096 px, até 16.777.216 px totais) para `.jpg/.jpeg/.png/.webp`.
- Endpoints admin `PUT`/`DELETE`/`GET /api/admin/professionals/{id}/photo` (`ProfessionalPhotoEndpoints.cs`) — já fazem staging→validação→commit→transação DB→cleanup do arquivo antigo, com rollback seguro em qualquer falha antes do commit.
- Endpoint público `GET /api/totem/professionals/{id}/photo` (`AllowAnonymous`) — já existe, já retorna 404 quando não há foto/profissional inativo/purpose divergente, 503 só para falha real de storage.
- `TotemProfessionalCardDto`/`TotemProfessionalCard` — **já tem o campo `photoUrl`** (aditivo, já implementado na spec `2026-09-09`). O carrossel já faz fallback para iniciais em `photoUrl == null` ou `onError`. **Este requisito do pedido original já está pronto**, nada a fazer no Totem além do ajuste de cache (§13.5).

### 13.2 O que é novo — endpoints self-serve

Extrair a lógica de mutação hoje presa em `ProfessionalPhotoEndpoints.Put`/`Delete` (staging, validação, transação, cleanup) para um helper `internal static` análogo ao já existente `ProfessionalPhotoStreaming` (que já foi extraído de `ProfessionalPhotoEndpoints.Get` para ser compartilhado entre admin e Totem). Nome sugerido: `ProfessionalPhotoMutation` (`recepcaototem/Features/Professionals/ProfessionalPhotoMutation.cs`), com uma assinatura que recebe o `Professional` já resolvido e autorizado — o endpoint admin passa o profissional resolvido por `{id:guid}`, o endpoint self-serve passa o profissional resolvido pela identidade autenticada. **Mesmo código, duas portas de entrada — não duas implementações.**

Novas rotas (mesmo prefixo `/api/professional/me` já estabelecido pelo `GET` existente):

```
POST   /api/professional/me/photo      Content-Type: multipart/form-data
DELETE /api/professional/me/photo
Auth: "Professional" (ambas)
```

`POST` — mesmas duas partes multipart já usadas pelo admin (`file`, `concurrencyToken`), mesmo parsing manual via `MultipartReader` (`ReadUploadAsync`, reaproveitado). **Não recebe `professionalId` em nenhum lugar** (nem rota, nem body, nem query) — o profissional é sempre resolvido pela identidade autenticada, igual ao padrão de `GET /api/professional/me`.

`DELETE` — mesmo padrão, corpo só com `concurrencyToken` (JSON, `ProfessionalConcurrencyRequest` já existe implicitamente no formato usado por Visits/Reservations — reaproveitar o mesmo shape).

### 13.3 Novo de verdade: normalização para WebP 512×512

Esta é a única lacuna real de infraestrutura. Hoje o validador só confirma que os bytes são um JPEG/PNG/WebP válido dentro de limites de dimensão — **não redimensiona nem recomprime**. Para atender "produzir WebP 512×512" é necessário:

1. Manter o validador atual como primeira barreira (magic-bytes reais, rejeita arquivo falso/corrompido, sem mudança).
2. Adicionar uma etapa de normalização **depois** da validação e **antes** do `StageAsync`/commit: decodificar a imagem validada, redimensionar/recortar para 512×512 (crop central se a proporção recebida não for 1:1 — o crop 1:1 do frontend já deveria ter garantido isso, mas o servidor não confia no resultado enviado, conforme exigido), recodificar como WebP.
3. **Biblioteca concreta a decidir no plano**, após inspeção de compatibilidade com o deploy .NET/Railway atual (ex.: `SixLabors.ImageSharp` é a opção mais comum para .NET puro sem dependências nativas do sistema operacional, o que costuma ser mais seguro em containers Railway do que bindings nativos tipo `SkiaSharp`/`libvips` — mas a escolha final e a validação de que não compromete o build/deploy ficam para a fase de planejamento, conforme já instruído).

### 13.4 Ciclo de vida do upload (reutilizado, não redesenhado)

Já é exatamente o pedido, implementado em `FileSystemPrivateFileStorage` + `ProfessionalPhotoEndpoints.Put`:
1. Autenticar profissional (via `"Professional"` policy).
2. Validar arquivo (magic bytes + dimensões).
3. Normalizar para WebP 512×512 (novo, §13.3).
4. `StageAsync` grava em área temporária.
5. `CommitAsync` move atomicamente para o armazenamento definitivo, gerando nova chave.
6. Dentro de uma transação de banco: cria novo `PrivateFile`, atualiza `Professional.PhotoFileId` para o novo, registra auditoria (`PROFESSIONAL_PHOTO_UPLOADED`/`PROFESSIONAL_PHOTO_REPLACED` — códigos já existentes, reaproveitados).
7. Commit da transação.
8. Só então tenta apagar o arquivo físico antigo (`DeleteAsync`) — falha aqui vira **warning de log**, não reverte a operação (arquivo antigo fica órfão, produto continua funcionando) — comportamento já implementado hoje no endpoint admin, reaproveitado tal e qual.

Se qualquer etapa 1-6 falhar: a foto anterior continua intacta e servindo normalmente (o `PhotoFileId` só é trocado no passo 6, dentro da transação).

### 13.5 Remoção

`DELETE` — mesma ordem: remove a referência lógica (`Professional.RemovePhoto`, zera `PhotoFileId`, dentro de transação com auditoria `PROFESSIONAL_PHOTO_REMOVED`, código já existente) **antes** de tentar apagar o arquivo físico. Falha na limpeza física → warning, não quebra a remoção lógica. Após a remoção, o Totem volta a mostrar iniciais no próximo carregamento do carrossel (já é o comportamento existente do fallback `onError`/`photoUrl == null`).

### 13.6 Cache/versionamento da URL pública

Divergência já registrada em D4. Mudança proposta (aditiva, no call site, não no helper):

- `GET /api/totem/professionals/{id}/photo` continua existindo como está.
- O DTO do carrossel passa a montar `photoUrl` como `/api/totem/professionals/{id}/photo?v={PhotoFileId}` (o próprio `PhotoFileId`, que já muda a cada upload/troca, funciona como token de versão — **nenhuma coluna nova**).
- O endpoint passa a ignorar a querystring `v` para qualquer lógica de negócio (ela existe só para cache-busting do browser) e o `cacheControl` passado ao `ProfessionalPhotoStreaming.StreamAsync` nessa chamada específica muda de `"public, max-age=300"` para `"public, max-age=31536000, immutable"` — seguro porque a URL só se repete enquanto `PhotoFileId` não mudar; uma troca de foto gera uma URL nova, então o navegador nunca reaproveita bytes antigos (isso atende "aparece imediatamente" melhor do que o TTL de 5 min atual, sem quebrar o endpoint admin, que mantém `private, no-store`).
- Remoção de foto: `photoUrl` volta a `null` no DTO (comportamento já existente) — não há "versão" a invalidar porque a própria ausência de `photoUrl` já faz o frontend cair no fallback de iniciais.

---

## 14. Contratos exatos — resumo dos DTOs novos

```
GET /api/professional/me  →  ProfessionalProfileResponse
  { name, profession, description, whatsApp, hasPhoto, photoUrl, concurrencyToken }

PUT /api/professional/me  ←  ProfessionalProfileUpdateRequest : IStrictModuleRequest
  { whatsApp, description, concurrencyToken }
  → 200 ProfessionalProfileResponse (atualizado) | 400 INVALID_PROFESSIONAL | 409 (conflito de concorrência, formato já existente)

POST /api/professional/me/photo   (multipart: file, concurrencyToken)
  → 200 { hasPhoto: true, photoUrl, concurrencyToken }
  | 400 INVALID_PROFESSIONAL_PHOTO | 401 | 403 PROFESSIONAL_PROFILE_NOT_LINKED | 409 | 429 | 503 PHOTO_UNAVAILABLE

DELETE /api/professional/me/photo  ←  { concurrencyToken }
  → 200 { hasPhoto: false, photoUrl: null, concurrencyToken }
  | 400 | 401 | 403 | 409

GET /api/professional/leases?page&pageSize&status?   (extensão aditiva do endpoint real)

GET /api/totem/professionals  →  photoUrl agora inclui ?v={PhotoFileId} quando houver foto
```

Nenhum destes reintroduz `professionalId` como entrada — todos resolvem a identidade pelo token de autenticação, igual ao padrão já estabelecido pelos 5 endpoints existentes citados em §1.2.

---

## 15. Rate limiting

Novo `ProfessionalPhotoUploadRateLimiter`, mesma forma de `ProfessionalPresenceRateLimiter` (§1.2): dois buckets (IP + identificador do profissional, hash SHA-256), configuráveis via:

```
RateLimiting:PhotoUploadIdentifierPermitLimit = 10
RateLimiting:PhotoUploadIpPermitLimit = 20
RateLimiting:PhotoUploadWindowSeconds = 600
```

Atende "≈10 uploads / 10 minutos / profissional" com o mesmo mecanismo já usado em produção, sem nova biblioteca.

---

## 16. Erros previstos (reaproveitando códigos reais)

| Código | Onde já existe | Uso nesta spec |
|---|---|---|
| `INVALID_PROFESSIONAL` | `ProfessionalEndpoints.cs:207-208` | `PUT /api/professional/me` com WhatsApp/descrição inválidos |
| `INVALID_PROFESSIONAL_PHOTO` | `ProfessionalPhotoEndpoints.cs:20` | Upload de foto inválida/corrompida/oversized (self-serve reaproveita o mesmo código do admin) |
| `PHOTO_UNAVAILABLE` | `ProfessionalPhotoEndpoints.cs:21` / `ProfessionalPhotoStreaming.cs:57` | Falha real de storage em leitura ou escrita |
| `INVALID_CONCURRENCY_TOKEN` | `ProfessionalEndpoints.cs:200-201` | Token malformado em qualquer mutação desta spec |
| `PROFESSIONAL_PROFILE_NOT_LINKED` | `ProfessionalProfileEndpoints.cs:25` (já existe) | Usuário com role profissional sem vínculo — reaproveitado por todos os endpoints novos `/api/professional/me*` |
| 429 (sem corpo específico definido ainda) | Padrão dos demais rate limiters do projeto | Excesso de upload |

Nenhum código novo é necessário. Não introduzir `INVALID_IMAGE`, `IMAGE_TOO_LARGE`, `INVALID_WHATSAPP`, `DESCRIPTION_TOO_LONG`, `STORAGE_ERROR`, `PROFILE_NOT_FOUND` como estava no rascunho original — todos têm equivalente real acima.

---

## 17. Segurança e concorrência

- Toda rota de perfil/foto exige `"Professional"` (policy real, string literal, mesmo padrão de todo o resto do sistema — não introduzir uma classe de constantes nova só para isto).
- Nenhum endpoint aceita `professionalId` vindo do cliente para determinar de quem é a foto/perfil — sempre resolvido por `ApplicationUserId` da identidade autenticada, replicando o padrão já usado 5 vezes no código.
- Admin nunca ganha rota de escrita de foto de outro profissional além da já existente `/api/admin/professionals/{id}/photo` (mantida como está — é a via administrativa, separada da self-serve; a spec não retira essa capacidade do admin, só não permite que ela seja usada para "enviar/trocar/remover" a foto de um profissional **pelo profissional errado**, o que já não é possível pois cada via checa sua própria autorização).
- Concorrência: `ConcurrencyToken`/`Professional.Version` já garante que um upload concorrente não pode fazer o banco apontar para um arquivo removido — o `db.Entry(professional).Property(x => x.Version).OriginalValue` já existente falha a transação se a versão mudou entre o `GET` e o `PUT`/upload, forçando novo carregamento no frontend.
- Frontend desabilita "Salvar foto" enquanto uma requisição de upload está em voo (estado local simples, sem necessidade de lock server-side adicional — o servidor já é a proteção real via `ConcurrencyToken`).
- Mecanismos de concorrência existentes de Reservations/Visits/Leases/Finance (todos já usam o mesmo `ConcurrencyToken`/`Version`) permanecem inalterados — nenhuma mudança nesta spec os toca.

---

## 18. Modelo de dados / migration

**Nenhuma migration é necessária para a foto do profissional.** `Professional.PhotoFileId` já existe; `PrivateFile.Id`/`CreatedAt` já cobrem versionamento e timestamp. As colunas `PhotoFileName`/`PhotoVersion`/`PhotoUpdatedAt` do rascunho original **não devem ser criadas** — seriam dados paralelos e redundantes ao que já existe (ver D2).

**Nenhuma migration é necessária para Reservas/Atendimentos/Locações/Financeiro/Disponibilidade** — todos os campos exibidos já existem nas entidades reais.

A única mudança de schema desta fase inteira, se o plano confirmar a extensão de filtro do §9, é **comportamental** (novo parâmetro de querystring num endpoint existente) — não é uma migration de banco.

Se, durante o plano, surgir a necessidade real de um campo novo (ex.: `CustomerName` projetado em `ReservationResponse` para a Agenda, §6), qualquer migration decorrente será **puramente aditiva** (nova coluna nullable ou novo campo de projeção sem alterar tabela), seguindo a convenção de nomes já usada (`yyyyMMddHHmmss_PascalCaseDescriptiveName`, últimas migrations reais: `20260908210951_ProfessionalPresenceAndRescheduling`, `20260910085909_CheckInManualCode`, `20260910215630_TotemBookingHandoff`). Nenhuma migration será gerada nesta etapa (só na fase de implementação, mediante autorização).

---

## 19. Crop frontend

Fluxo: selecionar imagem → modal "Ajustar foto" → crop 1:1 (arrastar + zoom, mouse e touch) → preview circular → salvar. Sem editor de imagens complexo (sem filtros, sem rotação livre, sem múltiplos aspectos). O crop é só para UX — o servidor sempre revalida e renormaliza (§13.3), nunca confia no resultado enviado.

Upload concorrente: botão "Salvar foto" desabilitado enquanto uma chamada estiver em andamento (estado local `uploading`, já é o padrão usado em outros formulários do projeto, ex. `loading` em `ChangePassword`/`CustomerRegister`).

Crop com touch: usar os mesmos handlers de ponteiro unificados (`onPointerDown`/`onPointerMove`/`onPointerUp`) já usados em `TotemProfessionalCarousel.tsx` para o drag do carrossel — mesma técnica, componente novo e isolado, sem reutilizar o componente do carrossel em si (só a técnica de pointer events).

---

## 20. Responsividade

- Desktop (1920×1080, 1366×768): sidebar existente do `ProfessionalShell`, tabelas completas em Reservas/Locações/Financeiro.
- Tablet/mobile: drawer existente do `ProfessionalShell`. Agenda e Atendimentos priorizam lista/cards (já é o padrão dos componentes `professional-agenda-*` existentes). Reservas/Locações/Financeiro: tabela com `overflow-x` controlado ou lista de cards equivalente — decisão de qual das duas fica para o plano, seguindo o que já existe hoje no Admin (`table-scroll`).
- Toques mínimos de 44px — mesmo padrão já auditado no sweep de responsividade anterior desta branch.
- Filtros (`ProfessionalFilterBar`) quebram linha em telas estreitas (flex-wrap).
- Crop: drag funcional em touch (ver §19).

---

## 21. Testes obrigatórios

### Backend

- Profissional A não consegue ler/alterar perfil ou foto de B (403/404, nunca vaza dado de B).
- `POST /api/professional/me/photo` sem autenticação → 401.
- Usuário autenticado sem vínculo profissional → `403 PROFESSIONAL_PROFILE_NOT_LINKED`.
- Arquivo > 5 MB rejeitado (`INVALID_PROFESSIONAL_PHOTO`).
- Imagem falsa/corrompida (magic bytes inválidos) rejeitada.
- `.jpg`/`.png`/`.webp` válidos aceitos.
- Upload bem-sucedido produz WebP 512×512 real (decodificar o resultado e checar dimensões/formato).
- Troca de foto muda `PhotoFileId` (novo `PrivateFile.Id`) — usado como prova de "versão incrementada".
- Remoção limpa `PhotoFileId` (fica `null`) e o Totem para de servir foto (404).
- Falha simulada no meio do processamento/storage preserva a foto anterior intacta (o `PhotoFileId` antigo continua válido e servindo).
- `GET /api/totem/professionals/{id}/photo` não aceita path/nome de arquivo arbitrário — só serve o `PhotoFileId` já registrado no profissional.
- Profissional sem foto → 404 em `GET /api/totem/professionals/{id}/photo`.
- DTO do Totem sem foto → `photoUrl: null`; com foto → URL com `?v=` presente.
- `GET /api/professional/leases` retorna só as locações do profissional autenticado (nunca aceita/usa um `professionalId` alternativo vindo do request).
- `GET /api/professional/finance/charges` idem, scoped ao próprio profissional.
- `PUT /api/professional/me` rejeita corpo contendo `professionalId`/`name`/`profession`/`photoFileId` (falha de desserialização por `IStrictModuleRequest`, não erro de negócio).
- `PUT /api/professional/me` com WhatsApp inválido → `400 INVALID_PROFESSIONAL`; com descrição > 500 ou contendo `<`/`>` → mesmo código.

### Frontend

- Nenhuma das 5 páginas (Reservas, Atendimentos, Locações, Financeiro, Meu Perfil) renderiza mais "Estamos preparando esta área".
- Agenda exibe dados reais de `reservations`/`visits` (não mock).
- Reservas lista dados reais e as duas ações (remarcar/cancelar) chamam os endpoints reais.
- Atendimentos exibe os três agrupamentos com os estados reais (`WAITING`/`IN_SERVICE`/`ENDED`) e as ações condicionadas ao estado.
- Locações é somente leitura (nenhum controle de edição renderizado).
- Financeiro é somente leitura, mostra resumo + tabela com status computado (incluindo `OVERDUE`).
- Disponibilidade mantém todo o comportamento existente (regressão zero — reutilizar os testes já existentes do módulo).
- Perfil lê os dados reais (`GET /api/professional/me`) e pré-popula o formulário.
- Atualização de WhatsApp/descrição chama `PUT` real e reflete o retorno.
- Crop/zoom funcionam (mouse); crop funciona em touch (evento sintético de pointer).
- Upload de foto funciona fim a fim (mock de API nos testes de componente).
- Remover foto funciona e a UI volta ao estado "sem foto".
- Fallback de iniciais no Totem continua funcionando quando `photoUrl` é `null` ou a imagem falha (`onError`) — teste de regressão explícito, já que este comportamento já existe e não pode quebrar.
- Loading/erro tratados em todas as páginas novas (mesmo padrão de `ProfessionalDashboard`/`ProfessionalAvailability` já existentes).
- Swipe/touch do carrossel de profissionais no Totem não regride (reexecutar a suíte existente de `TotemProfessionalCarousel.test.tsx` sem alterações).
- Nenhuma das páginas novas renderiza um painel claro fora do padrão Lumis (checagem visual manual + inspeção de que só classes `professional-*`/`lumis-*`/`panel`/`data-table` já escurecidas são usadas).

---

## 22. Migration / rollout (procedimento, não execução)

Nenhuma etapa abaixo é executada nesta fase — só a spec está sendo criada.

1. Gates locais completos (backend `dotnet test`, frontend `vitest`, `tsc -b`, `vite build`, production bundle verifier, `git diff --check`).
2. Se o plano confirmar necessidade de alguma migration realmente nova (ex.: campo de cliente na Agenda, §6) — gerar o SQL exato via `dotnet ef migrations script`, revisar antes de qualquer operação remota. Caso contrário, **nenhuma migration é gerada** (a foto não precisa de uma, conforme §18).
3. Revisar o SQL (se houver) antes de qualquer operação remota.
4. Aplicar migration (se houver) somente em Supabase staging, mediante autorização explícita.
5. Validar schema em staging.
6. Garantir que o diretório do Railway Volume (`Storage__PrivateFilesPath`) já está montado e gravável — já é infraestrutura existente, só confirmar que continua válida.
7. Push autorizado da branch.
8. Deploy Railway staging.
9. Smoke completo dos módulos novos (Reservas/Atendimentos/Locações/Financeiro/Perfil) com dados reais de staging.
10. Testar upload de foto real em staging.
11. Confirmar atualização imediata no Totem (URL versionada, sem esperar cache).
12. Testar remoção de foto → iniciais voltam no Totem.
13. Só depois considerar PR/`main`.

---

## 23. Critérios de aceite

A fase só está concluída quando:

- Zero páginas "Estamos preparando esta área" nas 8 rotas profissionais.
- Dashboard, Agenda, Disponibilidade — já funcionais, confirmados sem regressão.
- Reservas, Atendimentos, Locações, Financeiro, Meu Perfil — funcionais sobre os endpoints reais listados em §1 (mais as duas extensões aditivas: `status` em Locações, `PUT`/foto em Perfil).
- Nome/Profissão somente leitura; WhatsApp/Descrição editáveis via `PUT /api/professional/me`.
- Upload/crop/zoom/troca/remoção de foto funcionando fim a fim.
- Só o próprio profissional autenticado manipula sua foto (nunca por `professionalId` de request).
- Foto aparece imediatamente no Totem (URL versionada por `PhotoFileId`).
- Remover foto restaura iniciais no Totem.
- Nenhuma tela clara fora do padrão Lumis nas 8 rotas.
- Desktop/tablet/mobile utilizáveis (sidebar/drawer existentes, toques ≥44px).
- Testes obrigatórios (§21) verdes, backend e frontend.
- Staging passa o smoke real descrito em §22.

**Cenário obrigatório de homologação:**
```
Professional → Meu Perfil → seleciona foto → crop/zoom → salva → abre Totem → nova foto aparece
Professional → remove foto → Totem → iniciais aparecem novamente
```

---

## 24. Fora de escopo (repetido para clareza)

Chat cliente/profissional; prontuário; documentos; emissão de recibos; pagamentos online; Google/Outlook Calendar; edição de contrato de locação pelo profissional; notificações internas complexas; white/light mode; qualquer status paralelo aos enums reais listados em §1.1; calendário mensal complexo na Agenda; qualquer nova classe de constantes de policy/resolver de identidade (registrado como melhoria opcional, não obrigatória); qualquer operação remota nesta etapa de spec.

---

## 25. Self-review (executado antes do commit)

- **TODO/TBD/placeholder:** nenhum encontrado no texto final — os dois pontos de decisão explicitamente deferidos ao plano (biblioteca de imagem em §13.3; exposição de nome do cliente na Agenda em §6; representação mobile de tabela em §20) estão marcados como decisão de plano, não como lacuna aberta na arquitetura.
- **Contradições:** verificado que nenhuma seção reintroduz `professionalId` como entrada em endpoint algum, em nenhum lugar do documento.
- **Endpoints duplicados:** nenhum — todos os endpoints "novos" desta spec (`PUT /api/professional/me`, `POST`/`DELETE /api/professional/me/photo`, extensão de `status` em `/api/professional/leases`) foram checados contra o inventário do §1 e confirmados como não-redundantes.
- **Permissões:** todas as rotas novas usam a policy real `"Professional"`; nenhuma aceita id de profissional alheio.
- **Ambiguidade funcional:** nenhum requisito ficou sem endpoint/campo real associado — onde faltava um dado (nome do cliente na Agenda), foi marcado explicitamente como decisão de plano, não implementado silenciosamente como suposição.
- **Migration:** confirmado puramente aditiva onde existir (§18) — e, para o caso principal (foto), confirmado que **não há migration nenhuma**, o que é uma redução de risco em relação ao pedido original.
- **Recursos externos:** nenhum introduzido além da biblioteca de imagem (única lacuna real, decisão adiada para o plano conforme instruído) — nenhuma nova dependência de storage, nenhum serviço externo, nenhum SaaS.

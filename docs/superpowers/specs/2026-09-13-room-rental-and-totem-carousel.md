# Lumis — Totem Coverflow Rework, Native-Scroll Swipe, and Public Room Rental

**Status:** especificação para revisão — revisão pontual (2026-09-13) aplicada após aprovação conceitual. Nenhuma implementação, migration, alteração de Supabase/Railway, push, merge ou deploy nesta etapa.

**Branch:** `codex/reception-backend` (worktree `.worktrees/reception-backend`). HEAD no momento da escrita: `0256854da39e88bf40f7c5d64181088043b9b38a`.

**Revisão pontual (2026-09-13):** além dos refinamentos de touch, disponibilidade, DTOs públicos, fotos, snapshot estruturado, QR e configuração do WhatsApp, esta revisão conecta `RoomRentalInquiry` ao fluxo real de criação de `Lease`. O Admin reutiliza o modal existente de “Nova locação”; o interesse passa somente de `New` para `Converted`, com `LeaseId` e `ConvertedAt`, na mesma transação que cria a locação. A migration continua única.

**Escopo:**
1. Reavaliar a arquitetura de touch do carrossel do Totem — trocar o drag customizado (Pointer Events + `scrollLeft` manual) por scroll horizontal **nativo** do navegador com `scroll-snap`, mantendo o efeito visual de coverflow. Motivo: o fix anterior (touch-action/user-select no card) não resolveu em celular físico.
2. Reforçar a composição visual da tela `/totem/profissionais`: carrossel maior, card central com mais peso visual, palco deslocado para baixo, evitando o grande vazio inferior relatado.
3. Tornar "Alugar sala" funcional: catálogo público de salas com disponibilidade derivada de `Room`+`Lease`, galeria de fotos por sala, formulário de interesse que persiste e continua no WhatsApp do Financeiro, e conversão posterior no Admin pelo formulário real de “Nova locação”.

**Princípio arquitetural (não negociável):** reutilizar o que já existe. A investigação abaixo mostra exatamente o que já existe, o que precisa de extensão aditiva e o que é genuinamente novo — nenhuma tabela, storage ou fluxo paralelo é criado onde algo real já resolve o problema.

**Fora de escopo (explícito, nesta spec):**
- Qualquer fluxo de pagamento online da locação.
- CRM de interesses (funil, atribuição, follow-up automatizado) — só detalhes e a transição `New → Converted` produzida pela criação de uma locação.
- Edição manual ou transições adicionais de status do interesse; `Converted` só pode resultar da criação transacional de `Lease`.
- Alterar `professionalId`, dados, disponibilidade, handoff, autenticação ou criação de reservas do fluxo de profissionais já existente.
- Reintroduzir qualquer mock/dado fixo no bundle de produção.
- CPF/CNPJ no formulário de interesse.
- Aplicar a migration, tocar Supabase/Railway, ou fazer push/deploy — esta spec não autoriza nenhum desses.

---

## 1. Estado atual investigado — backend

### 1.1 `Room` (`src/GestaoPredio.Domain/Rooms/Room.cs`)

```csharp
public sealed class Room {
    public Guid Id; public string Name; public string NormalizedName; public string? Description;
    public decimal HourlyRate; public decimal DailyRate; public bool IsActive;
    public DateTimeOffset CreatedAt, UpdatedAt; public uint Version;
}
```
Sem campo de foto/galeria. `RoomConfiguration.cs`: tabela `Rooms`, `Name` maxlen 100, `NormalizedName` maxlen 200 (índice único `UX_Rooms_NormalizedName`), `Description` maxlen 1000, rates `HasPrecision(18,2)` com CHECK `>= 0`, `Version` é row-version (`xmin`).

Endpoints reais (`recepcaototem/Features/Rooms/RoomEndpoints.cs`, grupo `/api/admin/rooms`, `RequireAuthorization("Operations")`):

| Verbo | Rota | Filtro |
|---|---|---|
| GET | `/api/admin/rooms` (paginado: `page,pageSize,status,search`) | — |
| GET | `/api/admin/rooms/{id:guid}` | — |
| POST | `/api/admin/rooms` | `AntiforgeryFilter` |
| PUT | `/api/admin/rooms/{id:guid}` | `AntiforgeryFilter` |
| POST | `/api/admin/rooms/{id}/activate` \| `/deactivate` | `AntiforgeryFilter` |

DTOs (`RoomContracts.cs`): `RoomResponse(Id, Name, Description, HourlyRate, DailyRate, IsActive, CreatedAt, UpdatedAt, ConcurrencyToken)`. Erros: `ROOM_NAME_ALREADY_EXISTS` (409), `INVALID_CONCURRENCY_TOKEN` (400), `RESOURCE_MODIFIED` (409), validação de input via `RoomInput.TryValidate`.

**Não existe hoje nenhuma rota pública/anônima para salas** — só o CRUD admin acima. `roomsApi` no frontend (`api/modules.ts:218-235,492,542-557`) só é usado pela Admin.

### 1.2 `Lease` (`src/GestaoPredio.Domain/Leases/Lease.cs`) — fonte de verdade da disponibilidade

```csharp
public sealed class Lease {
    public Guid Id, TenantId, ProfessionalId, RoomId;
    public LeaseMode Mode;                       // Monthly=1, Daily=2, Hourly=3
    public decimal ContractedRate;
    public DateTimeOffset BillingStartAt; public int? BillingDueDay;
    public DateTimeOffset OccupancyStartAt; public DateTimeOffset? OccupancyEndAt;
    public LeaseLifecycleState LifecycleState;   // Open=1, EndingPending=2, Ended=3, Cancelled=4
    public int? MonthlyAnchorDay; public DateTimeOffset? MaterializedThroughAt;
    public DateTimeOffset CreatedAt, UpdatedAt; public uint Version;

    public LeaseOperationalStatus GetOperationalStatus(DateTimeOffset now); // Scheduled/Active/EndingPending/Ended/Cancelled
}
```
**Não existe campo `EndDate` nem `IsActive` bool.** A "disponibilidade" é sempre **computada**, nunca lida de uma coluna de status, via `Lease.GetOperationalStatus(now)` (já existe, `Lease.cs:62-72`):
- `LifecycleState` em `Cancelled`/`Ended`/`EndingPending` → retorna o mesmo valor.
- Senão: `now < OccupancyStartAt` → `Scheduled`; `OccupancyEndAt` definido e `now >= OccupancyEndAt` → `EndingPending`; senão → `Active`.

`LeaseConfiguration.cs` já garante FKs `Room`/`Professional`/`Tenant` (`DeleteBehavior.NoAction`) e índice `IX_Leases_Room_State_Start` — a consulta "leases de uma sala por estado" já é indexada, nenhum índice novo necessário para o catálogo.

### 1.3 `PrivateFile` / `IPrivateFileStorage` — infraestrutura de arquivo a reutilizar

`IPrivateFileStorage` (`src/GestaoPredio.Application/Abstractions/IPrivateFileStorage.cs`):
```csharp
Task<StagedPrivateFile> StageAsync(Stream source, long maximumBytes, CancellationToken ct);
Task<string> CommitAsync(StagedPrivateFile staged, CancellationToken ct);
Task<Stream> OpenStagedReadAsync(StagedPrivateFile staged, CancellationToken ct);
Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct);
Task<bool> DeleteAsync(string storageKey, CancellationToken ct);
Task DiscardAsync(StagedPrivateFile staged, CancellationToken ct);
```
Implementação `FileSystemPrivateFileStorage` (singleton), config em `Storage:PrivateFilesPath`/`Storage:ProfessionalPhotoMaxBytes`.

`PrivateFile` (`src/GestaoPredio.Domain/Files/PrivateFile.cs:16-40`):
```csharp
public static PrivateFile Create(string storageKey, string mimeType, long length, string purpose, DateTimeOffset createdAt)
```
**Bloqueio real encontrado:** `Create` hoje **rejeita qualquer `purpose` diferente de `PrivateFilePurposes.ProfessionalPhoto`** (`PrivateFile.cs:26-29`) — `if (!StringComparer.Ordinal.Equals(purpose, PrivateFilePurposes.ProfessionalPhoto)) throw ArgumentException`. E a tabela tem o mesmo bloqueio no banco: `PrivateFileConfiguration.cs:13` — `CK_PrivateFiles_Purpose: "Purpose" = 'PROFESSIONAL_PHOTO'`. **Os dois precisam mudar** (ver §6.1) para aceitar uma nova finalidade `ROOM_PHOTO` — isto não é "reaproveitar como está", é uma extensão aditiva real e pequena.

`PrivateFilePurposes` (`src/GestaoPredio.Domain/Files/PrivateFilePurposes.cs`): hoje só `ProfessionalPhoto = "PROFESSIONAL_PHOTO"`.

Template ponta-a-ponta a copiar: `ProfessionalPhotoMutation.cs` (stage → valida → normaliza → stage do normalizado → commit → grava `PrivateFile` + audit dentro de uma transação → em caso de troca, apaga o arquivo anterior best-effort). `ProfessionalPhotoEndpoints.cs` mostra o roteamento (`GET`/`PUT`/`DELETE` sob `RequireAuthorization("Operations")`, `AntiforgeryFilter` nas mutações). `IProfessionalPhotoValidator`/`IImageNormalizer` (este último usa `SixLabors.ImageSharp` **3.1.11** — versão já fixada nesta branch por licenciamento, ver commit `829fae5`/`840fa8e`) são genéricos o suficiente para reuso direto — **não são específicos de profissional**, apesar do nome. Decisão: reutilizar as mesmas interfaces/implementações tal como estão; renomeá-las é cosmético e fica fora de escopo.

### 1.4 Convenções transversais a reutilizar (sem exceção)

- `IStrictModuleRequest` (`Features/Common/StrictBody.cs`) — todo DTO de request novo implementa isto.
- `ConcurrencyToken.Encode(uint)`/`TryDecode(string?, out uint)` — todo campo `concurrencyToken` novo usa isto.
- Códigos de erro: a convenção do projeto (repetida em toda spec anterior) é **não inventar vocabulário novo** — reaproveitar `INVALID_CONCURRENCY_TOKEN` (400), `RESOURCE_MODIFIED` (409), `TOO_MANY_REQUESTS` (429), `ROOM_NAME_ALREADY_EXISTS` (409, já existe), e cunhar **apenas** os poucos códigos genuinamente novos e necessários: `INVALID_ROOM_PHOTO` (400, espelha `INVALID_PROFESSIONAL_PHOTO`), `PHOTO_UNAVAILABLE` (503, já existe, reutilizado), `ROOM_PHOTO_LIMIT_REACHED` (400, condição nova e acionável — "remova uma foto antes de adicionar outra"), `INVALID_ROOM_RENTAL_INQUIRY` (400, validação do formulário público).
- Rate limiter: classe dedicada por superfície, dois buckets `PartitionedRateLimiter<string>` (IP + identificador com hash SHA-256), config via `RateLimiting:<Nome>IpPermitLimit`/`IdentifierPermitLimit`/`WindowSeconds`, registrada como singleton em `Program.cs`. Template exato: `CustomerPublicRateLimiter.cs` (leitura pública) e `ProfessionalPhotoUploadRateLimiter.cs` (upload).
- Endpoint público anônimo: `.AllowAnonymous()` por rota (sem `MapGroup` com auth), como em `TotemEndpoints.cs`. **Importante:** existe um catch-all em `Program.cs` — `app.Map("/api/{**path}", () => Results.NotFound()).RequireAuthorization();` — então toda rota pública nova precisa do `.AllowAnonymous()` explícito, do contrário cai no catch-all autenticado.
- Antiforgery: só em mutações autenticadas (cookie de sessão existe). Endpoints públicos do Totem **nunca** carregam `AntiforgeryFilter` — a proteção contra abuso ali é o rate limiter por IP, não CSRF (não há sessão para proteger). O novo `POST` de interesse de locação segue este mesmo padrão: público, `.AllowAnonymous()`, sem antiforgery, com rate limiter dedicado.
- `ApplicationDbContext` hoje **não tem** `RoomPhotos` nem `RoomRentalInquiries` — confirmado por leitura direta de todos os `DbSet<>` (`ApplicationDbContext.cs:20-42`). Duas tabelas novas, uma migration.
- Migrations ativas em `src/GestaoPredio.Infrastructure/Persistence/Migrations/PostgreSql/` (é o provider real, `SqlServerLegacy/` não é mais o alvo). Última: `20260910215630_TotemBookingHandoff`. Convenção de nome: `<timestamp>_<NomeDaFeature>` (não por entidade — ex. `OperatingHoursAndRoomBlocks` já uniu dois conceitos numa migration). Nome proposto aqui: `RoomPhotosAndRentalInquiries`.

---

## 2. Estado atual investigado — frontend (Totem)

### 2.1 Carrossel atual (`recepcaototem/ClientApp/src/features/totem/TotemProfessionalCarousel.tsx`, pós-round anterior)

- Um único `<div role="listbox" className="totem-carousel-viewport">` com `overflow-x: auto`, `scroll-snap-type: x mandatory`, **`touch-action: none`** — o navegador está proibido de fazer scroll nativo aqui. O componente captura `pointerdown/move/up/cancel/leave` e move `viewport.scrollLeft -=` manualmente, e commita a troca de card via threshold (`SWIPE_COMMIT_PX = 40`).
- `data-offset` (distância do centro, clampado a ±2) dirige o coverflow via CSS `transform`/`opacity` — **isto continua útil e é reaproveitado tal como está** (não é o mecanismo problemático).
- Card = `<button role="option">`, com `touch-action:none`/`user-select:none`/`-webkit-touch-callout:none` (fix do round anterior — não removeu o bug em celular físico do usuário).
- `onScroll` deriva `activeIndex` do card mais próximo do centro via `offsetLeft`/`clientWidth` — **este pedaço (achar o card centrado a partir da posição de scroll) é exatamente o que sobrevive** na nova arquitetura; só troca a fonte do movimento (nativo em vez de `pointermove` manual).

### 2.2 Composição/CSS atual (`styles.css`, seção "Totem professional carousel")

- `.totem-professionals-inner` — coluna flex `justify-content: center` dentro de `.totem-professionals` (`flex:1`, `100dvh` menos a topbar). Card: `flex-basis: clamp(200px, 22vw, 264px)` (mobile: `clamp(180px, 74vw, 240px)`), sem `min-height` explícita no viewport do carrossel além de `padding-block: clamp(16px, 3vh, 40px)`.
- **Causa raiz do "carrossel pequeno/vazio embaixo" relatada pelo usuário:** os `clamp()` têm teto baixo (264px de largura, ~330-400px de altura incluindo padding) que não cresce com a altura real de uma tela grande de Totem — em um monitor alto, o bloco título+carrossel fica centralizado mas ocupa uma fração pequena da altura, sobrando vazio simétrico acima e abaixo. Não é um bug de posicionamento (`justify-content:center` já centraliza), é um **teto de tamanho baixo demais para telas de Totem reais**.

### 2.3 Referência de coverflow "original" já existente no código (não em produção)

`Reception.tsx` (`src/pages/Reception.tsx`, tela legada só em `import.meta.env.DEV`, não usada em produção) tem `.lumis-gallery-stage`/`.lumis-gallery-card.position-N`: `perspective()+rotateY()+rotateZ()` por posição fixa (0 a 4), `touch-action: pan-x pan-y` (deixa o navegador rolar nativamente) + `user-select: none`. **Confirma que o padrão "scroll nativo + coverflow visual" já foi usado com sucesso neste mesmo código-base antes** — a spec de touch abaixo (§3) é uma volta a esse padrão, adaptado ao carrossel atual (não à paleta clara).

---

## 3. Nova arquitetura de touch — scroll nativo + `scroll-snap`, reaproveitando a derivação existente

**Decisão (aprovada pelo usuário, revisada em 2026-09-13):** o navegador controla o movimento do dedo. A derivação de "qual card está centralizado" **já existe, já funciona e não foi a causa do bug** — continua sendo o `onScroll` + `requestAnimationFrame` atual, sem trocar por `IntersectionObserver`. Só o mecanismo de **arrasto** (o que hoje compete com o navegador pelo gesto) é removido. Não trocar código funcional sem necessidade.

### 3.1 O que muda — e o que NÃO muda

| Hoje | Novo |
|---|---|
| `touch-action: none` no viewport e no card | `touch-action: pan-x pan-y` (viewport e card) — permite explicitamente pan horizontal **e vertical** nativos. Nunca usar um valor que bloqueie pan vertical (ex. `pan-x` sozinho, ou `none`): o scroll vertical da página precisa continuar funcionando com o dedo sobre o carrossel. Este é literalmente o mesmo valor já usado em `.lumis-gallery-stage` (`styles.css`, referência do §2.3) — não é um valor novo no código-base. |
| `onPointerDown/Move/Up/Cancel/Leave` movendo `scrollLeft` manualmente | **Removidos por completo.** O elemento volta a ser um scroller nativo comum (`overflow-x: auto`, `scroll-snap-type: x mandatory`, `scroll-behavior: smooth` só para navegação por teclado/seta/clique/`scrollIntoView`). |
| `setPointerCapture`/`releasePointerCapture` | **Removidos** — não há captura de ponteiro num scroller nativo. |
| `didDragRef`/`pointerActiveRef`/`dragOriginXRef`/`dragLastXRef`/`dragTravelRef`/`DRAG_THRESHOLD_PX`/`SWIPE_COMMIT_PX`/`SWIPE_CARD_PX` (heurística de threshold para distinguir tap de swipe) | **Removidos.** O navegador já suprime nativamente o `click` sintético de um elemento quando o gesto que terminou ali foi na verdade um scroll/drag além de um pequeno limiar — é comportamento padrão de qualquer scroller nativo (mouse ou touch), não precisa de heurística própria em cima disso. |
| `onScroll` + `requestAnimationFrame`, medindo `offsetLeft`/`clientWidth` de cada card para achar o mais próximo do centro (`TotemProfessionalCarousel.tsx`, função `onScroll` atual) | **Mantido exatamente como está**, sem modificação de lógica — é o pedaço correto e já correto do componente. Continua sendo a única fonte de `activeIndex` durante o scroll, disparado pelo evento `scroll` nativo do próprio elemento (que agora é gerado pelo navegador em vez de por `viewport.scrollLeft -=` manual — o handler não sabe nem precisa saber a diferença). |
| `centre(index)` via `viewport.scrollTo({ left: card.offsetLeft - ..., behavior })` (cálculo manual de offset) | Pode continuar exatamente como está (cálculo de `offsetLeft` já funciona e é independente do mecanismo de arrasto) **ou** ser simplificado para `card.scrollIntoView({ behavior, inline: 'center', block: 'nearest' })` — troca cosmética opcional, decisão de implementação, não é uma exigência desta spec. |

### 3.2 Por que isto resolve a classe de bug encontrada

A investigação da rodada anterior mostrou que o card tinha `touch-action`/`user-select` computados como `auto` (não herdando a restrição pretendida do viewport), e que a lógica JS de arrasto em si respondia corretamente a uma sequência sintética de Pointer Events — ou seja, o problema vivia na arbitragem nativa de gesto do navegador competindo com o JS, não na lógica de derivação do card ativo. **Remover o arrasto customizado elimina essa competição por construção**: só existe UM dono do gesto de movimento (o navegador), e o React volta a fazer só o que sempre fez bem — ler o resultado do scroll via `onScroll`, não tentar produzi-lo.

### 3.3 O que continua igual

- `data-offset` + CSS de coverflow (tilt/escala/opacidade por distância do centro) — inalterado.
- `onScroll` + `requestAnimationFrame` derivando `activeIndex` do card mais próximo do centro — inalterado (ver §3.1).
- Clique num card lateral → `setActive(index)` → centraliza (cálculo atual ou `scrollIntoView`, ver §3.1) — inalterado no comportamento observável.
- Clique no card **já central** → `onContinue()` (handoff) — inalterado.
- Setas prev/next, dots, teclado (`ArrowLeft/Right/Home/End`) — inalterados.

### 3.4 Testes — o que dá para testar em jsdom e o que precisa de validação real

jsdom não implementa scroll real nem `scroll-snap` de forma observável, mas **isto já era verdade hoje** — o `onScroll` atual já é escrito para não quebrar sob geometria zerada (`measurable` guard, comentário original: "Guarded so it neither throws nor NaNs under zero geometry, and it never scrolls"). Nenhum polyfill/mock novo de observação de scroll é necessário, porque a lógica de derivação não muda.

- **jsdom/RTL:** clique em card lateral centraliza (mock de `scrollTo`/`scrollIntoView`, como já é feito hoje); clique no card já central chama `onContinue`; teclado continua funcionando; `onScroll` com geometria mockada (via `Object.defineProperty` em `offsetLeft`/`offsetWidth`/`clientWidth`, técnica já usada nos testes existentes) atualiza `activeIndex` corretamente — tudo isso já é testável hoje e continua sendo, sem infraestrutura de teste nova.
- **Removidos porque testam um mecanismo que deixa de existir:** os testes que fixam `SWIPE_COMMIT_PX`/`didDragRef`/sequência de `pointerdown→pointermove→pointerup` como *mecanismo de arrasto* (ex. "a touch swipe moves the active card left and right", "a long touch fling can jump more than one card", "a tiny touch drag stays a tap"). Não são "quebrados e ignorados" — são apagados junto com o código que testavam, porque o navegador passa a ser o responsável pelo gesto de swipe em si.
- **Não testável em jsdom, por natureza, porque virou comportamento nativo do navegador:** o gesto de swipe/scroll físico. Isto é uma consequência desejada da mudança (menos superfície de risco em JS), não uma lacuna de cobertura a preencher com mock — validado por teste manual real (§3.5).

### 3.5 Validação real obrigatória (não pode ser considerada resolvida sem isto)

Igual ao round anterior: esta ferramenta de navegador não gera toque real de hardware. A homologação final do swipe **só conta com teste em celular físico do usuário** — swipe esquerda/direita, sequência de swipes, swipe curto/longo, tap em lateral/central, scroll vertical da página, ausência de seleção de texto/callout, imagem não é arrastada, carrossel não trava. Isto é repetido aqui de propósito: é a condição de aceite explícita do usuário para este item, e a spec não deve implicar que a arquitetura nova "resolve" sozinha sem essa validação.

---

## 4. Composição visual — carrossel maior, palco mais abaixo

**Decisão de abordagem (conforme pedido explícito do usuário):** não usar `position:absolute`/`top` mágico/`translateY` arbitrário. Resolver via estrutura de flex/grid, `padding`, `gap`, `min-height` do estágio.

### 4.1 Diagnóstico confirmado (não é posicionamento, é dimensionamento)

`.totem-professionals-inner` já é `display:flex; flex-direction:column; justify-content:center` dentro de um `flex:1` que ocupa a altura da tela menos a topbar — ou seja, o bloco (título + carrossel + dots) **já está centralizado verticalmente**. O "vazio embaixo" percebido é o efeito de um bloco **pequeno demais** (por causa do teto baixo dos `clamp()`) dentro de um espaço disponível grande, centralizado — sobra igual em cima e embaixo, mas como o título fica perto do topo da tela normalmente essa distribuição não é percebida como "centralizada", é percebida como "o carrossel está pequeno e alto".

### 4.2 Direção da correção (sem números cegos — validar visualmente na implementação)

- **Aumentar o teto do `clamp()` do card central** — hoje `flex-basis: clamp(200px, 22vw, 264px)`; teto novo na faixa **300–340px** de largura como o usuário pediu, condicionado a não estourar o `max-width: 980px` de `.totem-professionals-inner` com os vizinhos parcialmente visíveis somados (então o `clamp()` do meio — o valor em `vw` — também precisa subir, não só o teto).
- **Altura do card**: hoje a altura é implícita (conteúdo + padding), sem `min-height`. Introduzir `min-height` no card ativo (faixa 420–470px sugerida) via `.totem-carousel-card.is-active`, mantendo os laterais menores (proporcionais, ex. ~85-90% da altura do central) — isto por si só empurra o card central a ocupar mais espaço vertical **sem tocar em posicionamento**, e a foto interna (`.totem-carousel-photo`) cresce junto (hoje `clamp(84px, 9vw, 120px)`, teto também deve subir).
- **`min-height` do estágio** (`.totem-carousel-viewport` ou um wrapper novo `.totem-carousel-stage`) na faixa **480–560px** em desktop, para que a área tenha espaço de sobra para a perspectiva/rotação/sombra dos cards laterais sem cortar (`overflow` do pai precisa continuar visível o suficiente para a leve `rotateY`/`scale` não gerar clipping — conferir `.totem-professionals-inner`/`.totem-professionals` não têm `overflow:hidden` direto sobre o carrossel; só `.totem-professionals` tem, no nível da tela inteira, que é onde já se conta com margem).
- **Deslocar o bloco para baixo dentro do espaço já centralizado**: em vez de tirar o `justify-content:center` (que quebraria a resposta a diferentes alturas de tela), usar `padding-top` proporcional maior que `padding-bottom` no container do carrossel (ex. `padding-block: clamp(24px, 6vh, 56px) clamp(12px, 3vh, 28px)` como ponto de partida) ou um `gap` maior entre o título e o carrossel do que entre o carrossel e os dots — desloca o centro de massa visual para baixo sem sair do fluxo normal (flex), preservando responsividade.
- **Mobile:** não replicar os mesmos números. Card central `width: min(78–84vw, <teto ainda a validar>)` com altura proporcional (a proporção largura:altura do desktop mantida, não um número fixo à parte). Vizinhos parcialmente visíveis nas bordas (já existe via `padding-inline` calculado a partir do `flex-basis` — a fórmula `padding-inline: max(12px, calc(50% - (largura-do-card / 2)))` já usada precisa só refletir o novo `flex-basis` maior). CTA "Alugar sala" já empilha abaixo do título em `<720px` (regra existente, mantida).
- **Critério de aceite visual** (não numérico): o card central deve "dominar a composição" olhando a tela inteira, não só a área do carrossel — comparável em peso visual ao título acima dele. Validar nas 4 resoluções já usadas nas rodadas anteriores (1920×1080, 1366×768, 768×1024, 390×844) com captura de tela real antes de considerar pronto.

### 4.3 Escopo explícito desta seção

Só CSS/estrutura de layout — nenhuma mudança de comportamento, DTO ou rota. Roda junto com a implementação do §3 (mesma revisão de arquivo), porque ambas tocam `TotemProfessionalCarousel.tsx`/`styles.css`, mas são preocupações independentes e devem ser commitadas ordenadamente (recomendo: um commit para a troca de mecanismo de touch com os testes RED→GREEN, um commit separado para o ajuste visual, já que o usuário pediu para não misturar as duas fases na *investigação* — aqui na *spec* mantenho a mesma separação por clareza de revisão).

---

## 5. Disponibilidade — `Room` + `Lease`, sem checkbox manual

**Fonte de verdade:** só leases reais, nunca um campo editável à parte. Reaproveita `Lease.GetOperationalStatus(now)` (já existe, §1.2) — nenhuma mudança no domínio de `Lease` é necessária para isto.

### 5.1 Regra exata (revisada 2026-09-13 — `Active` E `Scheduled` são bloqueantes)

Para cada `Room` ativa (`IsActive = true`), buscar seus `Lease`s e calcular `GetOperationalStatus(agora)` para cada um. Define-se **lease bloqueante** = status operacional `Active` **ou** `Scheduled` (contratos já assinados/agendados para o futuro contam tanto quanto os já em vigor — cobre também o caso de múltiplos contratos consecutivos para a mesma sala, ex. um `Active` que termina e um `Scheduled` que já começa em seguida).

1. **Nenhum lease bloqueante** → sala é `AVAILABLE_NOW` ("Disponível agora").
2. **Existe pelo menos um lease bloqueante, e TODOS os bloqueantes têm `OccupancyEndAt` definido** → sala é `AVAILABLE_SOON`. `AvailableFrom` = o **maior** `OccupancyEndAt` entre todos os leases bloqueantes, **+ 1 dia** (cobre a cadeia de contratos consecutivos: se há um `Active` terminando em 30/09 e um `Scheduled` já começando depois dele e terminando em 15/11, `AvailableFrom` é 16/11, não 01/10 — a sala só fica de fato livre depois do último bloqueio da cadeia).
3. **Qualquer lease bloqueante sem `OccupancyEndAt`** (contrato por prazo indeterminado, `Active` ou `Scheduled`) → sala é `OCCUPIED` — **não aparece no catálogo público**, independentemente do que os outros leases bloqueantes digam (um único contrato indeterminado já torna a disponibilidade futura desconhecida).
4. Leases com status `EndingPending`, `Ended` ou `Cancelled` nunca são bloqueantes (inalterado desta revisão — só a inclusão de `Scheduled` ao lado de `Active` mudou).

Isto substitui a decisão da versão anterior desta spec, que tratava `Scheduled` como não-bloqueante — revertida a pedido explícito do usuário.

### 5.2 DTO exposto publicamente (revisado 2026-09-13 — sem preço)

```csharp
public enum PublicRoomAvailabilityStatus { AvailableNow, AvailableSoon }
public sealed record PublicRoomCard(
    Guid Id, string Name, string? Description,
    PublicRoomAvailabilityStatus Availability, DateOnly? AvailableFrom, // null quando AvailableNow
    string? CoverPhotoUrl);
public sealed record PublicRoomDetail(
    Guid Id, string Name, string? Description,
    PublicRoomAvailabilityStatus Availability, DateOnly? AvailableFrom,
    IReadOnlyList<string> PhotoUrls); // ordenadas por SortOrder, capa primeiro
```
**`HourlyRate`/`DailyRate` removidos dos dois DTOs públicos** (existiam na primeira versão desta spec; removidos por decisão explícita do usuário). O fluxo público é de **interesse em locação**, não de contratação/preço online — o valor da tarifa é informação comercial tratada pelo Financeiro depois do contato via WhatsApp, não exibida no Totem. `RoomResponse` (Admin, `RoomContracts.cs`, §1.1) **continua expondo `HourlyRate`/`DailyRate` normalmente** — nada muda no lado autenticado.

Nenhum campo de `Lease`/`Tenant`/valores de contrato vaza para o DTO público (privacidade dos locatários atuais) — só o rótulo de disponibilidade e a data.

### 5.3 Endpoint

`GET /api/totem/rooms` (lista, `.AllowAnonymous()`, `CustomerPublicRateLimiter` reaproveitado tal como em `TotemEndpoints.Professionals`) — filtra `IsActive=true` e `OCCUPIED` fora da resposta, ordena por `NormalizedName`. `GET /api/totem/rooms/{id:guid}` (detalhe, mesmo rate limiter) — 404 se a sala não existe/está inativa/está `OCCUPIED` (não expõe salas ocupadas nem por link direto).

---

## 6. `RoomPhoto` — galeria por sala

### 6.1 Extensão necessária em `PrivateFile`/`PrivateFilePurposes` (pré-requisito)

- `PrivateFilePurposes.cs`: adicionar `public const string RoomPhoto = "ROOM_PHOTO";`.
- `PrivateFile.Create` (`PrivateFile.cs:26-29`): trocar a comparação única por um conjunto permitido — `if (purpose is not (PrivateFilePurposes.ProfessionalPhoto or PrivateFilePurposes.RoomPhoto)) throw ...`. Mudança pequena, mas é uma mudança real no domínio compartilhado — **não pular esta etapa achando que "já dá pra reaproveitar como está"**.
- `PrivateFileConfiguration.cs:13`: CHECK constraint vira `"Purpose" IN ('PROFESSIONAL_PHOTO', 'ROOM_PHOTO')` — via migration (não é alteração manual de schema, é o que a migration desta feature faz).

### 6.2 Entidade nova `RoomPhoto`

Diferente de `Professional` (uma foto, `PhotoFileId` nullable direto na entidade), sala precisa de **galeria** (até 8 fotos) — não cabe como coluna única. Entidade nova, mesmo padrão de agregado simples usado no resto do domínio:

```csharp
public sealed class RoomPhoto {
    public Guid Id { get; }
    public Guid RoomId { get; }
    public Guid PrivateFileId { get; }
    public int SortOrder { get; }      // 0-based, define ordem de exibição
    public bool IsCover { get; }       // exatamente uma true por RoomId quando existem fotos
    public DateTimeOffset CreatedAt { get; }

    public static RoomPhoto Attach(Guid roomId, Guid privateFileId, int sortOrder, bool isCover, DateTimeOffset occurredAt);
    public void Reorder(int sortOrder);
    public void SetCover(bool isCover);
}
```
EF config nova `RoomPhotoConfiguration.cs`: FK `RoomId → Rooms(Id)` `DeleteBehavior.Cascade` (apagar a sala apaga suas fotos — a sala em si não tem hoje um fluxo de exclusão física, só desativação, então isto é defensivo), FK `PrivateFileId → PrivateFiles(Id)` `DeleteBehavior.Restrict` (o storage cleanup é feito explicitamente pelo código, como já acontece hoje com foto de profissional — nunca deixar o banco apagar o `PrivateFile` sozinho e deixar um arquivo órfão em disco), índice `IX_RoomPhotos_Room_SortOrder (RoomId, SortOrder)`, **índice único parcial** `UX_RoomPhotos_Room_Cover ON (RoomId) WHERE "IsCover" = true` (Postgres suporta `HasFilter` no `HasIndex` do EF Core) — garante no banco, não só na aplicação, que nunca existam duas capas para a mesma sala.

Limite de 8 fotos: validado na aplicação antes do `INSERT` (`db.RoomPhotos.CountAsync(x => x.RoomId == roomId) >= 8` → `ROOM_PHOTO_LIMIT_REACHED`), não em CHECK constraint (contagem não é expressável em CHECK simples no Postgres sem trigger, e trigger é desproporcional aqui).

### 6.2.1 Invariantes explícitos (adicionados 2026-09-13 — cada um precisa de teste próprio, ver §12)

- **Primeira foto enviada vira capa automaticamente** — `Attach(...)` chamado com `isCover: true` quando `db.RoomPhotos.CountAsync(x => x.RoomId == roomId) == 0` no momento do upload; qualquer foto seguinte entra com `isCover: false`.
- **Quando existem fotos, exatamente UMA é capa** — garantido em dois níveis: aplicação (toda troca de capa desmarca a anterior antes de marcar a nova, na mesma transação) e banco (`UX_RoomPhotos_Room_Cover`, índice único parcial `WHERE "IsCover"`). Uma sala sem nenhuma foto não tem capa (estado válido, não um erro).
- **Excluir uma foto que não é capa não altera a capa** — `SortOrder` das fotos restantes é recompactado (ver abaixo), `IsCover` de nenhuma outra linha muda.
- **Excluir a foto que é capa promove automaticamente a próxima foto ordenada** — a foto sobrevivente com o menor `SortOrder` remanescente vira a nova capa, na mesma transação da exclusão (nunca duas operações separadas que deixem a sala momentaneamente sem capa observável por outra requisição).
- **Excluir a última foto de uma sala deixa a sala sem capa** — não é um erro, é o estado natural de "sala sem fotos" (`CoverPhotoUrl`/`PhotoUrls` nos DTOs públicos ficam vazios/nulos).
- **`SortOrder` fica sempre contíguo `0..N-1`** — toda exclusão recompacta os índices das fotos restantes (ex. excluir a foto de `SortOrder=1` de um conjunto `{0,1,2,3}` resulta em `{0,1,2}`, nunca `{0,2,3}`); todo `reorder` também produz `0..N-1` por construção (ver abaixo).
- **`reorder` exige exatamente todos os IDs atuais da sala** — o corpo `{ orderedPhotoIds: Guid[] }` é validado contra o conjunto real de `RoomPhoto.Id` daquela sala: tamanho diferente, ID ausente do conjunto atual, ID duplicado no array, ou ID pertencente a **outra** sala → `400 INVALID_ROOM_PHOTO` (reaproveitado; não é um upload inválido, mas é a mesma classe de "requisição de foto malformada", evita cunhar mais um código só para isto). Só com a validação de conjunto completa e exata a operação escreve `SortOrder = posição no array` para cada foto, dentro de uma transação.
- **Troca de capa é transacional e nunca viola `UX_RoomPhotos_Room_Cover` mesmo momentaneamente** — dentro da mesma transação de banco: `UPDATE` a capa antiga para `IsCover = false` **antes** do `UPDATE` que marca a nova capa como `IsCover = true` (nunca as duas em paralelo/nulas ao mesmo tempo); como é uma única transação, nenhuma outra conexão consegue observar um estado intermediário com zero ou duas capas — o índice único parcial garante isso mesmo se a ordem dos `UPDATE`s dentro da transação for trocada por engano durante a implementação.

### 6.3 Endpoints admin (`/api/admin/rooms/{id}/photos`, `RequireAuthorization("Operations")`)

| Verbo | Rota | Filtro | Corpo |
|---|---|---|---|
| GET | `/{roomId}/photos` | — | — (lista ordenada) |
| POST | `/{roomId}/photos` | `AntiforgeryFilter` | multipart (`file`), mesmo parsing manual de `ProfessionalPhotoMutation.ReadUploadAsync` adaptado (sem `concurrencyToken` de sala aqui — anexar uma foto não é uma edição da `Room` em si, é adicionar uma linha filha; não precisa de controle de concorrência otimista na `Room`) |
| DELETE | `/{roomId}/photos/{photoId}` | `AntiforgeryFilter` | — |
| PUT | `/{roomId}/photos/reorder` | `AntiforgeryFilter` | `{ orderedPhotoIds: Guid[] }` — reescreve `SortOrder` de todas as fotos da sala numa transação |
| POST | `/{roomId}/photos/{photoId}/cover` | `AntiforgeryFilter` | — (define esta como capa; desmarca a anterior na mesma transação) |

Reaproveita `IProfessionalPhotoValidator`/`IImageNormalizer` (webp, mesmo limite de tamanho — novo `Storage:RoomPhotoMaxBytes`, sugestão de default igual ao de profissional, 5 MB, ajustável). Streaming de leitura (pública, para o catálogo) segue o padrão de `TotemEndpoints.ProfessionalPhoto`/`ProfessionalPhotoStreaming.StreamAsync`, cache `"public, max-age=31536000, immutable"`, URL versionada por `PhotoFileId` (`?v=<id>`) igual ao já usado — mesma lição do bug de cache do Totem já corrigido nesta branch.

### 6.4 UI Admin (`Rooms.tsx` + novo `RoomPhotoManager`)

Cada `room-admin-card` ganha um botão **"Gerenciar fotos"** (ícone `Image`/`Images` do `lucide-react`, mesmo estilo de `secondary-button` já usado para "Editar") abrindo um `Modal` novo (`size="large"`, mesmo componente `Modal` já usado por `RoomForm`) com: grade de miniaturas (drag-and-drop simples de reordenação **ou**, mais barato de implementar e testar, botões "mover para cima/baixo" por foto — decisão de implementação, não bloqueia a spec), botão "Definir como capa" por foto, botão remover por foto, input de upload (mesmo padrão de `label` envolvendo `<input type="file" style="display:none">` já usado em `ProfessionalProfile.tsx`), contador "N/8 fotos" desabilitando o upload ao atingir o limite.

---

## 7. `RoomRentalInquiry` — interesse de locação

### 7.1 Entidade

```csharp
public sealed class RoomRentalInquiry {
    public Guid Id { get; }
    public Guid RoomId { get; }
    public string FullName { get; }                              // obrigatório
    public string WhatsApp { get; }                               // normalizado via WhatsAppNormalizer.TryNormalize (já existe)
    public string ProfessionOrCompany { get; }                    // obrigatório ("profissão ou empresa")
    public string? Note { get; }                                  // observação opcional
    public PublicRoomAvailabilityStatus PresentedAvailabilityStatus { get; } // snapshot ESTRUTURADO, não texto
    public DateOnly? PresentedAvailableFrom { get; }              // snapshot ESTRUTURADO, null quando AvailableNow
    public RoomRentalInquiryStatus Status { get; }                // New = 1, Converted = 2
    public Guid? LeaseId { get; }                                  // null até a conversão
    public DateTimeOffset? ConvertedAt { get; }                    // null até a conversão
    public DateTimeOffset CreatedAt { get; }

    public static RoomRentalInquiry Create(Guid roomId, string fullName, string whatsApp,
        string professionOrCompany, string? note,
        PublicRoomAvailabilityStatus presentedAvailabilityStatus, DateOnly? presentedAvailableFrom,
        DateTimeOffset occurredAt);

    public void Convert(Guid leaseId, DateTimeOffset occurredAt);
}
```
**Revisão 2026-09-13 — snapshot estruturado, não texto:** a versão anterior desta spec persistia `PresentedAvailability` como `string` já formatada em PT-BR (ex. `"Disponível a partir de 01/10/2026"`). Isto foi corrigido: o snapshot grava **dado**, não **apresentação** — `PresentedAvailabilityStatus` (o mesmo enum `PublicRoomAvailabilityStatus` do §5.2) + `PresentedAvailableFrom` (nullable, populado só quando o status é `AvailableSoon`). O texto em português — "Disponível agora" / "Disponível em breve — a partir de 01/10/2026" — é formatado **só** em três lugares que consomem esses dois campos, nunca persistido: a listagem do Admin (§7.5), a resposta do endpoint de criação (§7.3), e a mensagem do WhatsApp (§8.3). Isto evita, por exemplo, que uma correção futura de formato de data ou uma tradução exijam migrar dados históricos — o dado bruto nunca muda de forma, só a sua apresentação.

**Sem CPF/CNPJ** (explícito no pedido). `Status` possui somente `New` e `Converted`; não há edição manual, CRM ou outros estados. `Convert(leaseId, occurredAt)` exige `leaseId` válido, aceita somente `New`, grava `LeaseId`, `ConvertedAt` normalizado para UTC/microssegundos e muda o status para `Converted`; uma segunda conversão falha. O interesse preserva para auditoria o `RoomId` originalmente solicitado, mesmo que o Admin escolha outra sala no contrato. Sem `Version`: a corrida é resolvida no banco por atualização condicional dentro da transação de criação do Lease (§7.6), não por um formulário genérico de edição.

EF config `RoomRentalInquiryConfiguration.cs`: FK `RoomId → Rooms(Id)` `DeleteBehavior.NoAction`; FK nullable `LeaseId → Leases(Id)` também `DeleteBehavior.NoAction`; índice `IX_RoomRentalInquiries_Room_CreatedAt` para listagem e índice parcial `IX_RoomRentalInquiries_Status_CreatedAt` sobre `Status = 'NEW'` para a fila operacional. `LeaseId` não precisa de índice único adicional: existe uma única linha de inquiry e a atualização condicional `Status = NEW` impede que ela produza dois contratos; a FK garante que um valor gravado sempre aponte para um Lease existente.

### 7.2 Validação

`RoomRentalInquiryInput.TryValidate` (mesmo padrão estático de `RoomInput.TryValidate`): `FullName` obrigatório (maxlen 200), `WhatsApp` via `WhatsAppNormalizer.TryNormalize` (já existe, aceita E.164 ou BR formatado — reutilizado tal como está), `ProfessionOrCompany` obrigatório (maxlen 200), `Note` opcional (maxlen 500, mesmo limite de `Description` de profissional, mesma regra de rejeitar `<`/`>` já aplicada em `Professional.NormalizeDescription` — reaproveitar a mesma validação, não reinventar). Falha → `INVALID_ROOM_RENTAL_INQUIRY` (400).

### 7.3 Endpoint público

`POST /api/totem/rooms/{roomId}/rental-inquiries` — `.AllowAnonymous()`, corpo `RoomRentalInquiryRequest(FullName, WhatsApp, ProfessionOrCompany, Note?) : IStrictModuleRequest` (a `RoomId` vem da rota, não do corpo — nunca confiar em id vindo do cliente quando já está na URL, mesmo padrão usado em toda a API). Rate limiter dedicado (**novo**, não reaproveitar `CustomerPublicRateLimiter` diretamente — mesma razão que levou a um `ProfessionalPhotoUploadRateLimiter` próprio: é uma escrita pública, merece limite próprio e mais restritivo que leitura): `RoomRentalInquiryRateLimiter`, config `RateLimiting:RoomRentalInquiryIpPermitLimit`/`IdentifierPermitLimit` (identificador = WhatsApp normalizado, hash SHA-256, mesmo padrão)/`WindowSeconds`.

Fluxo do handler:
1. Rate limit.
2. Carrega `Room` (404 se não existe/inativa/`OCCUPIED` pela regra do §5.1).
3. Recalcula a disponibilidade da sala **no momento do POST** (mesma lógica do §5.1) para obter `(status, availableFrom)` — **nunca confiar em nada vindo do cliente sobre disponibilidade** (nem é pedido no corpo do request, por isso), e persiste esse par estruturado em `PresentedAvailabilityStatus`/`PresentedAvailableFrom` (§7.1) — nunca um texto já formatado.
4. Valida o corpo (`RoomRentalInquiryInput.TryValidate`).
5. Persiste `RoomRentalInquiry` + `AuditEntry` (`ROOM_RENTAL_INQUIRY_CREATED`) numa transação.
6. Formata o par `(status, availableFrom)` em texto PT-BR **neste momento**, só para compor a mensagem do WhatsApp (§8.3) e o campo de exibição da resposta — o texto formatado nunca é gravado, só devolvido/usado ali. Monta a URL do WhatsApp e retorna:
```csharp
public sealed record RoomRentalInquiryResult(Guid InquiryId, string WhatsappUrl, string PresentedAvailabilityLabel);
```
`PresentedAvailabilityLabel` é o texto pronto ("Disponível agora" / "Disponível em breve — a partir de 01/10/2026") — o frontend da tela de sucesso (§7.4) o exibe diretamente, sem reformatar datas no cliente.

### 7.4 Frontend — do formulário à tela "Continue no WhatsApp" (revisado 2026-09-13)

Rota nova `/totem/salas` (catálogo, agrupado em "Disponíveis agora" / "Disponíveis em breve" — duas seções, mesma tela) e `/totem/salas/:id` (detalhe: galeria de fotos, disponibilidade — sem tarifa, §5.2 —, botão "Tenho interesse" abrindo o formulário — inline na mesma tela ou modal, decisão de implementação).

**Substituído nesta revisão:** a versão anterior desta spec propunha `window.open(whatsappUrl, '_blank')` direto após o submit, como única experiência. Isto foi rejeitado — o fluxo principal acontece **dentro do Totem físico**, onde não existe "abrir uma aba nova" da forma como existe num desktop comum, e o visitante precisa continuar a conversa no **próprio celular dele**, não no quiosque.

**Fluxo revisado:**
1. Botão "Falar com o Financeiro" no formulário → `await roomRentalInquiryApi.create(roomId, input)` → resposta `RoomRentalInquiryResult { inquiryId, whatsappUrl, presentedAvailabilityLabel }` (§7.3).
2. Sucesso → navega para uma tela nova, `/totem/salas/:id/interesse` (ou um estado local da mesma página — decisão de implementação; a rota própria facilita retomar/voltar de forma consistente com o padrão já usado em `/totem/handoff`), **"Continue no WhatsApp"**.
3. Essa tela reaproveita o padrão de QR já existente em `TotemHandoff.tsx` (`recepcaototem/ClientApp/src/pages/TotemHandoff.tsx:1,36,77-84`): mesma biblioteca `qrcode` (`QRCode.toDataURL(alvo, QR_OPTS)`), mesma paleta de opções (`{ margin: 1, width: 320, color: { dark: '#181818', light: '#ffffff' } }`), gerada client-side a partir do **`whatsappUrl` retornado pelo backend** (não de uma URL interna da Lumis como no handoff de agendamento — aqui o QR aponta direto para o `wa.me`). Mostra:
   - Título "Continue no WhatsApp".
   - O QR Code do `whatsappUrl`.
   - Texto explicativo: "Escaneie para continuar no seu celular" (mesmo tom de "Escaneie para continuar seu agendamento" já usado).
   - `presentedAvailabilityLabel` e o nome da sala, para contexto.
   - Botão **"Abrir WhatsApp"** — `window.open(whatsappUrl, '_blank')` (ou `window.location.href` se a validação no dispositivo físico mostrar que abas não funcionam bem no navegador do kiosk — decisão de implementação a validar no aparelho real, não bloqueia a spec). Este botão é o caminho relevante quando alguém acessa esta tela **do próprio celular** (ex. um link direto, ou um cenário fora do kiosk) — no Totem físico, o caminho esperado é o visitante escanear o QR com o telefone dele.
4. **Sem polling/status/expiração** — diferente do handoff de agendamento (`TotemHandoff.tsx`), não há um "lado Lumis" que precise saber se a conversa de WhatsApp foi concluída (o WhatsApp não reporta de volta para o sistema). A tela é estática após gerar o QR: só o texto, o QR e os botões de ação/voltar. Não reaproveitar a parte de `TotemHandoff.tsx` que faz `totemApi.pollHandoff`/countdown/`expiresAt` — só a geração do QR em si é reaproveitada, não a máquina de estados de handoff completa.
5. Botão secundário para voltar ao catálogo (`/totem/salas`) ou ao início (`/totem`) — mesma convenção de "Escolher outro profissional"/"← Voltar" já usada nas outras telas do Totem.

### 7.5 Admin — "Interesses de locação"

A rota própria `/admin/interesses-locacao` mantém os interesses separados dos contratos e oferece listagem paginada mais detalhe, ambos com `RequireAuthorization("Operations")`: `GET /api/admin/room-rental-inquiries` e `GET /api/admin/room-rental-inquiries/{id:guid}`. A resposta inclui interessado, WhatsApp, sala originalmente solicitada, disponibilidade apresentada a partir do snapshot estruturado, profissão/empresa, observação, data, `Status`, `LeaseId` e `ConvertedAt`.

Para `New`, as ações são **Ver detalhes** e **Criar locação**. Para `Converted`, são **Ver detalhes** e **Ver locação**. Não existe botão de editar status. “Criar locação” navega para `/admin/locacoes?inquiryId={id}`; `Leases.tsx` lê o interesse e abre o mesmo modal já usado pelo botão “Nova locação”. “Ver locação” navega para `/admin/locacoes?leaseId={leaseId}` e abre o detalhe já existente. Não há segundo formulário de contrato.

O prefill é deliberadamente restrito:
- `RoomId` do interesse preenche inicialmente o select de Sala, mas o Admin pode trocá-lo; o `RoomId` original do inquiry nunca é reescrito.
- O contrato real de `Tenant` contém somente `Name` e `Kind`. Se ainda não existir Tenant, “Novo locatário” recebe apenas `FullName` como nome inicial; o Admin confirma `INDIVIDUAL` ou `LEGAL_ENTITY`. WhatsApp não pertence a Tenant e `ProfessionOrCompany` não vira nome/tipo automaticamente. Depois de criar, o fluxo existente seleciona o novo `tenantId` no modal.
- `ProfessionalId` nunca é inferido de `ProfessionOrCompany`; o modelo não contém vínculo inequívoco, então o Admin seleciona.
- Modalidade, valor contratado, vencimento, início da cobrança, início e fim da ocupação não são inferidos do inquiry; o modal preserva seus defaults atuais e todos esses campos continuam sob controle do Admin. A disponibilidade apresentada aparece apenas como contexto e não preenche datas ou condições.

### 7.6 Conversão transacional pelo fluxo real de Lease

O contrato real atual é `POST /api/admin/leases`, protegido por `Operations` + antiforgery, com `CreateLeaseRequest(TenantId, ProfessionalId, RoomId, Mode, ContractedRate, BillingStartAt, BillingDueDay, OccupancyStartAt, OccupancyEndAt)`. O handler já abre uma transação, adquire locks de Tenant/Room/Professional, valida recursos, reconcilia leases vencidos, verifica disponibilidade/conflitos, chama `Lease.Create`, materializa `LeaseOccurrence`, registra `LEASE_CREATED` e salva. Não será criado endpoint paralelo que duplique essas regras.

A extensão mínima adiciona `Guid? RoomRentalInquiryId` ao `CreateLeaseRequest`. Quando ausente, o comportamento atual permanece. Quando presente, o mesmo handler:
1. verifica que o inquiry existe e está `New`; não exige que o `request.RoomId` seja igual ao `inquiry.RoomId`, pois a troca de sala é permitida;
2. executa todas as validações e regras existentes e salva o Lease, ocorrências e auditoria dentro da transação já aberta;
3. ainda na mesma transação, faz atualização condicional do inquiry com predicado `Id = RoomRentalInquiryId AND Status = NEW`, gravando `Status = CONVERTED`, `LeaseId = lease.Id` e `ConvertedAt = now`;
4. se a atualização afetar zero linhas, faz rollback de toda a transação e responde `409 ROOM_RENTAL_INQUIRY_ALREADY_CONVERTED`; assim, uma corrida nunca deixa um segundo Lease persistido;
5. faz commit somente após Lease e inquiry estarem consistentes.

Inquiry inexistente retorna `404`; inquiry já convertido retorna `409` com o mesmo código. A FK `LeaseId → Leases(Id)` impede `Converted` apontando para contrato inexistente. Um CHECK constraint exige `Status = NEW` com `LeaseId`/`ConvertedAt` nulos ou `Status = CONVERTED` com ambos preenchidos, inclusive quando a atualização condicional não passa pelo método de domínio. A resposta do `POST /api/admin/leases` continua `LeaseResponse`, permitindo ao frontend abrir o detalhe criado. Um `AuditEntry` adicional `ROOM_RENTAL_INQUIRY_CONVERTED` registra inquiry e Lease sem duplicar `LEASE_CREATED`.

---

## 8. WhatsApp do Financeiro — configuração, nunca hardcode

### 8.1 Onde mora o número (revisado 2026-09-13 — regras de obrigatoriedade explícitas)

Novo `appsettings.json`/`appsettings.Development.json`, seção nova mirando o padrão já usado por `Storage`/`AccessControl`:
```json
"Whatsapp": { "FinanceiroPhoneNumber": "" }
```
**Nunca commitar um número real** em nenhum dos dois arquivos versionados — o valor em `appsettings.json`/`appsettings.Development.json` fica sempre `""` (mesma convenção já usada por `Storage:PrivateFilesPath`, que também é `""` no arquivo versionado e só ganha valor real via ambiente).

Bind via `IOptions<WhatsappOptions>` + `IValidateOptions<WhatsappOptions>` (mesmo padrão de `PrivateFileStorageOptionsValidator`), com a regra de obrigatoriedade dependente do ambiente:
- **Development** (appsettings versionado, valor `""`): a aplicação **não pode falhar ao subir** por causa disto — rodar localmente sem o número configurado é o caso normal para quem não está trabalhando neste fluxo especificamente. A validação de obrigatoriedade (`ValidateOnStart()`) é condicionada a `!builder.Environment.IsDevelopment()`, ou o endpoint de criação de interesse retorna um erro claro em runtime (ex. `503` com um código como `ROOM_RENTAL_WHATSAPP_NOT_CONFIGURED`) se chamado sem o número configurado, em vez de derrubar a aplicação inteira.
- **Staging/Production**: **obrigatório** quando o fluxo de "Alugar sala" estiver habilitado — `ValidateOnStart()` sem a condição acima nesses ambientes, falhando o start da aplicação se o valor estiver vazio/mal formado. Evita descobrir em produção, só quando o primeiro visitante tentar usar o botão, que ninguém configurou o número.
- **Testes** (unit/integração `dotnet test`): usam sua própria configuração de teste (um valor de WhatsApp de teste fixo, nunca o real, injetado via `IOptions<WhatsappOptions>` fake ou um `appsettings.Testing.json` não versionado com dado fictício) — nunca dependem do `appsettings.Development.json` estar preenchido nem falham por ele estar vazio.

Em staging/produção real, o valor entra via variável de ambiente (`Whatsapp__FinanceiroPhoneNumber`, convenção padrão do ASP.NET Core para override de config aninhada) — **não é tocado nesta spec nem por esta etapa de implementação**. Qualquer alteração futura de variável de ambiente no Railway exige autorização explícita do usuário antes de ser feita, exatamente como já estabelecido nas rodadas anteriores desta mesma branch (parar e reportar antes de mudar env vars) — esta spec não presume essa autorização automaticamente só porque descreve o nome da variável.

### 8.2 Quem monta a URL do WhatsApp

**Decisão:** o **backend** monta a URL completa (`https://wa.me/<numero>?text=<mensagem-urlencoded>`) e devolve pronta na resposta do `POST` de interesse (§7.3, passo 6). O frontend nunca vê o número cru nem monta a URL — só recebe `whatsappUrl` e abre. Isto é o que garante literalmente, por construção, que o número "não é hardcoded no React": ele nunca chega ao bundle do cliente como valor conhecido de antemão, só como parte de uma resposta de API específica de uma sala/interesse já registrado.

### 8.3 Template da mensagem (servidor, PT-BR, mesmo teor do exemplo do usuário)

Um único helper (`RoomAvailabilityFormatter.Format(status, availableFrom) : string`, ou nome equivalente decidido na implementação) produz o texto ("Disponível agora" / "Disponível em breve — a partir de dd/MM/yyyy") a partir do par estruturado — **a mesma função é chamada nos três lugares que exibem esse texto** (resposta do POST, listagem do Admin, mensagem do WhatsApp abaixo), nunca reimplementada em paralelo:

```
Olá! Tenho interesse em alugar uma sala na Lumis.

Sala: {room.Name}
Disponibilidade: {RoomAvailabilityFormatter.Format(inquiry.PresentedAvailabilityStatus, inquiry.PresentedAvailableFrom)}
Nome: {inquiry.FullName}
WhatsApp: {inquiry.WhatsApp}
Profissão/Empresa: {inquiry.ProfessionOrCompany}
Observação: {inquiry.Note ?? "—"}
```
Montada como `string`, então `Uri.EscapeDataString(...)` no parâmetro `text` — nenhuma biblioteca nova, `System.Uri` já cobre isto.

---

## 9. Endpoints públicos novos — tabela resumo

| Verbo | Rota | Rate limiter | Corpo/DTO |
|---|---|---|---|
| GET | `/api/totem/rooms` | `CustomerPublicRateLimiter` (reaproveitado) | → `PublicRoomCard[]` |
| GET | `/api/totem/rooms/{id:guid}` | idem | → `PublicRoomDetail` |
| GET | `/api/totem/rooms/{id:guid}/photos/{photoId:guid}` | idem (streaming de imagem, cache longo, mesmo padrão de foto de profissional) | binário |
| POST | `/api/totem/rooms/{id:guid}/rental-inquiries` | `RoomRentalInquiryRateLimiter` (novo) | `RoomRentalInquiryRequest` → `RoomRentalInquiryResult(InquiryId, WhatsappUrl, PresentedAvailabilityLabel)` |

Todas `.AllowAnonymous()`, todas registradas junto de `MapTotemEndpoints` (ou uma extensão nova `MapTotemRoomEndpoints`, mantendo `TotemEndpoints.cs` do tamanho atual — decisão de organização de arquivo, não afeta contrato).

---

## 10. Migration necessária (uma só, não aplicada nesta etapa)

Nome proposto: `RoomPhotosAndRentalInquiries` (pasta `PostgreSql`, timestamp no momento da implementação).

Conteúdo:
1. Alterar `CK_PrivateFiles_Purpose` para `"Purpose" IN ('PROFESSIONAL_PHOTO', 'ROOM_PHOTO')`.
2. Criar tabela `RoomPhotos` (`Id` PK, `RoomId` FK→`Rooms` cascade, `PrivateFileId` FK→`PrivateFiles` restrict, `SortOrder int`, `IsCover bool`, `CreatedAt`), índice `IX_RoomPhotos_Room_SortOrder`, índice único parcial `UX_RoomPhotos_Room_Cover WHERE "IsCover"`.
3. Criar tabela `RoomRentalInquiries` (`Id` PK, `RoomId` FK→`Rooms` no-action, `FullName`, `WhatsApp`, `ProfessionOrCompany`, `Note` nullable, `PresentedAvailabilityStatus` (mesmo tipo/conversão de `PublicRoomAvailabilityStatus` usado no DTO — texto, não int, seguindo a convenção já usada para `LeaseLifecycleState`, §1.2), `PresentedAvailableFrom` (`date`, nullable), `Status` como `NEW`/`CONVERTED`, `LeaseId` nullable com FK→`Leases` no-action, `ConvertedAt` nullable e `CreatedAt`). Adicionar CHECK constraint que permita somente `NEW` com `LeaseId`/`ConvertedAt` nulos ou `CONVERTED` com ambos preenchidos, o índice `IX_RoomRentalInquiries_Room_CreatedAt` e o índice parcial `IX_RoomRentalInquiries_Status_CreatedAt WHERE \"Status\" = 'NEW'` para a fila operacional.

Zero mudança em tabelas existentes além do CHECK de `PrivateFiles` (nenhuma coluna nova em `Room`/`Lease`/`Professional`).

---

## 11. Segurança / rate limiting — resumo

| Superfície | Auth | Rate limit | Antiforgery |
|---|---|---|---|
| `GET /api/totem/rooms`, `/{id}`, `/{id}/photos/{photoId}` | anônimo | `CustomerPublicRateLimiter` | não (sem sessão) |
| `POST /api/totem/rooms/{id}/rental-inquiries` | anônimo | `RoomRentalInquiryRateLimiter` (novo, mais restritivo) | não (sem sessão) |
| `GET/POST/DELETE/PUT /api/admin/rooms/{id}/photos*` | `"Operations"` | não (mesma convenção do CRUD de salas hoje, que também não tem rate limiter dedicado) | sim, mutações |
| `GET /api/admin/room-rental-inquiries`, `/{id:guid}` | `"Operations"` | não (leitura autenticada) | não (GET) |
| `POST /api/admin/leases` com `RoomRentalInquiryId` opcional | `"Operations"` | não (mesma rota existente) | sim; criação e conversão atômicas |

Nenhuma alteração nas políticas de autorização existentes (`"Operations"`, `"Professional"` etc.) — só reuso.

---

## 12. Testes — plano

**Backend:**
- `RoomPhotoTests` (integração, mesmo padrão de `ProfessionalPhotoTests`): upload válido, upload inválido (`INVALID_ROOM_PHOTO`), limite de 8 (`ROOM_PHOTO_LIMIT_REACHED`), streaming público retorna a imagem certa com cache header público, e **um teste por invariante do §6.2.1**: primeira foto vira capa; segunda foto não mexe na capa; excluir foto não-capa preserva a capa e recompacta `SortOrder`; excluir a capa promove a próxima foto ordenada; excluir a última foto deixa a sala sem capa (`CoverPhotoUrl` nulo); `reorder` com conjunto de IDs incompleto/IDs duplicados/ID de outra sala → `400`; `reorder` válido produz `SortOrder` contíguo `0..N-1`; trocar de capa nunca deixa, mesmo momentaneamente, zero ou duas linhas com `IsCover=true` (teste de concorrência/transação, ex. duas trocas de capa disparadas em paralelo — a segunda deve falhar ou serializar, nunca violar `UX_RoomPhotos_Room_Cover`).
- `RoomAvailabilityTests` (unit, sobre a função pura que implementa §5.1, **regra revisada**): sem lease bloqueante → `AVAILABLE_NOW`; um lease `Active` com `OccupancyEndAt` → `AVAILABLE_SOON` com a data certa (`+1 dia`); um lease `Scheduled` futuro com `OccupancyEndAt` → `AVAILABLE_SOON` (fixa a regra revisada — `Scheduled` agora bloqueia); **dois leases bloqueantes consecutivos** (um `Active` terminando antes de um `Scheduled` que já começa em seguida, ambos com `OccupancyEndAt`) → `AVAILABLE_SOON` com `AvailableFrom` = o **maior** `OccupancyEndAt` dos dois, `+1 dia` (cobre "múltiplos contratos consecutivos" explicitamente); qualquer lease bloqueante (`Active` **ou** `Scheduled`) sem `OccupancyEndAt` → sala fora da lista, mesmo com outro lease bloqueante tendo `OccupancyEndAt` definido; lease `EndingPending`/`Ended`/`Cancelled` → nunca bloqueante, ignorado (inalterado).
- `RoomRentalInquiryTests` (integração): criação válida retorna `whatsappUrl` com o texto esperado (decodificar e comparar) e `presentedAvailabilityLabel` formatado corretamente; validação rejeita campos ausentes/whatsapp inválido; rate limit dispara 429; sala inativa/inexistente/`OCCUPIED` → 404; `PresentedAvailabilityStatus`/`PresentedAvailableFrom` persistidos refletem a disponibilidade real calculada no servidor no momento do POST (não confia em nada vindo do cliente); a entidade nunca grava texto formatado, só os dois campos estruturados; todo interesse público nasce `New`, com `LeaseId`/`ConvertedAt` nulos.
- `RoomRentalInquiryConversionTests` (integração): criar Lease sem inquiry preserva o comportamento atual; converter um inquiry `New` cria Lease/ocorrências/auditorias e grava `Converted`, `LeaseId` e `ConvertedAt` na mesma transação; escolher outra sala é permitido sem alterar o `RoomId` original do inquiry; falha de validação ou conflito de ocupação não cria Lease e deixa o inquiry `New`; ID inexistente retorna `404`; ID já convertido retorna `409 ROOM_RENTAL_INQUIRY_ALREADY_CONVERTED`; duas requisições concorrentes para o mesmo inquiry deixam exatamente um Lease persistido e uma resposta `409`; FK e CHECK impedem referências/estados inconsistentes.
- `PrivateFileTests` (unit, existente, estender): `PrivateFile.Create` aceita `ROOM_PHOTO` além de `PROFESSIONAL_PHOTO`; continua rejeitando qualquer outro valor.
- `PublicRoomContractsTests` (novo, unit): `PublicRoomCard`/`PublicRoomDetail` não têm `HourlyRate`/`DailyRate` — teste de reflexão simples sobre as propriedades do tipo, para travar a decisão do §5.2 contra reintrodução acidental futura.

**Frontend:**
- `TotemProfessionalCarousel.test.tsx`: remover **só** os testes que fixam o mecanismo de arrasto customizado (`SWIPE_COMMIT_PX`/`didDragRef`/sequência `pointerdown→pointermove→pointerup` como produtor do movimento); **manter e não reescrever** o teste que já cobre `onScroll` derivando `activeIndex` da geometria mockada — ele não muda. Manter/adaptar os testes de clique-lateral-centraliza, clique-central-continua, teclado, fallback de foto. Nenhum mock de `IntersectionObserver` é introduzido.
- `TotemRoomsCatalog.test.tsx`/`TotemRoomDetail.test.tsx` (novos): agrupamento "Disponíveis agora"/"Disponíveis em breve", rótulo de data correto, galeria renderiza `PhotoUrls` na ordem recebida, **nenhuma tarifa exibida** (assert negativo — `HourlyRate`/`DailyRate` não aparecem em lugar nenhum da tela pública), formulário valida campos obrigatórios, submissão bem-sucedida navega para a tela "Continue no WhatsApp" com o QR renderizado a partir do `whatsappUrl` retornado, erro de validação do backend aparece inline.
- `TotemRoomInterestSuccess.test.tsx` (novo): renderiza o QR (mock de `qrcode`, mesmo padrão de mock já usado em `TotemHandoff.test.tsx` se existir, ou o padrão equivalente), botão "Abrir WhatsApp" chama `window.open` com o `whatsappUrl` exato, **nenhum polling/intervalo é iniciado** (diferente de `TotemHandoff` — teste negativo garantindo que a tela não tenta chamar nenhum endpoint de status).
- `Rooms.test.tsx` (Admin, estender): botão "Gerenciar fotos" abre o modal, upload/remover/reordenar/capa chamam os endpoints certos, contador de limite.
- `RoomRentalInquiries.test.tsx` (Admin, novo): listagem e detalhe exibem os campos pedidos, disponibilidade formatada e os dois status; `New` oferece “Ver detalhes”/“Criar locação” e navega com `inquiryId`; `Converted` oferece “Ver detalhes”/“Ver locação” e navega com `leaseId`; paginação preservada.
- `Leases.test.tsx` (Admin, estender): `inquiryId` abre o modal existente de “Nova locação”, preenche a sala de forma editável e oferece `FullName` somente como nome inicial do fluxo existente de “Novo locatário”; não infere `ProfessionalId`, `Kind`, modalidade, valores ou datas; o submit envia `RoomRentalInquiryId`; `leaseId` abre o detalhe existente da locação.
- `modules.test.ts` (API frontend, estender): serialização opcional de `roomRentalInquiryId`, leitura do detalhe do inquiry e compatibilidade da criação de Lease sem inquiry.

**Gates de sempre** (sem mudança): `npx vitest run`, `npx tsc -b`, `npx vite build`, `node scripts/verify-production-bundle.mjs`, `git diff --check`, mais os testes de integração `dotnet test` cobrindo os arquivos novos do backend.

---

## 13. Resumo de decisões tomadas nesta spec (para revisão explícita, atualizado 2026-09-13)

1. **Lease `Scheduled` bloqueia disponibilidade tanto quanto `Active`** (§5.1) — revertido nesta revisão a pedido explícito do usuário; a versão anterior tratava só `Active` como bloqueante. `AvailableFrom` é o maior `OccupancyEndAt` entre todos os leases bloqueantes (cobre contratos consecutivos).
2. `IProfessionalPhotoValidator`/`IImageNormalizer` são reaproveitados tal como estão (nomes incluídos) — não renomear para algo genérico nesta fase.
3. `RoomPhoto` é uma tabela nova (não uma coluna em `Room`) porque é galeria (1-N), diferente do padrão de foto única de `Professional`. Invariantes de capa/ordenação detalhados explicitamente no §6.2.1, cada um com teste próprio.
4. `PrivateFile.Create` e o CHECK constraint de `Purpose` precisam de uma mudança real e pequena (não é reuso "de graça") para aceitar `ROOM_PHOTO`.
5. Limite de 8 fotos validado na aplicação, não em CHECK constraint no banco.
6. `RoomRentalInquiry` sem CPF/CNPJ e sem versão/concorrência otimista, com somente `New` e `Converted`. A conversão ocorre exclusivamente junto da criação de Lease; não há endpoint de edição manual de status. **O snapshot de disponibilidade é estruturado** (`PresentedAvailabilityStatus` + `PresentedAvailableFrom`), nunca um texto PT-BR persistido — o texto é formatado só na exibição (Admin, resposta do POST, mensagem do WhatsApp), sempre pela mesma função.
7. **`HourlyRate`/`DailyRate` removidos de `PublicRoomCard`/`PublicRoomDetail`** — o fluxo público é de interesse, não de preço/contratação online. `RoomResponse` (Admin) continua com os valores normalmente.
8. O backend monta e devolve a URL final do WhatsApp; o frontend nunca hardcoda nem monta essa URL.
9. **A experiência pós-interesse é uma tela própria "Continue no WhatsApp" com QR Code** (reaproveitando a geração de QR de `TotemHandoff.tsx`, sem a máquina de polling/status/expiração daquela tela, que não se aplica aqui) — não um `window.open` isolado como a versão anterior desta spec propunha.
10. Novo rate limiter dedicado só para o `POST` de interesse; leitura pública reaproveita `CustomerPublicRateLimiter` como o resto do Totem já faz.
11. **Arquitetura de touch do carrossel** muda de Pointer Events customizados para scroll nativo + `scroll-snap`, **reaproveitando a derivação `onScroll`+`requestAnimationFrame` já existente e correta** — `IntersectionObserver` foi removido desta spec por não ser necessário (a lógica atual de achar o card mais próximo do centro nunca foi a causa do bug). `touch-action: pan-x pan-y` explicitamente, nunca um valor que bloqueie pan vertical.
12. Ajuste visual (carrossel maior, palco mais para baixo) é resolvido por estrutura/`clamp()`/`gap`/`min-height`, nunca por `position:absolute`/`transform` arbitrário — conforme pedido explícito.
13. **Configuração do WhatsApp do Financeiro**: obrigatória via `ValidateOnStart()` só em staging/produção (não em Development, que não pode quebrar por appsettings versionado vazio); testes usam configuração própria, nunca dependem do valor real; número real nunca commitado; qualquer mudança futura de variável de ambiente no Railway exige autorização explícita separada.
14. **Conversão de interesse reutiliza o fluxo real de Lease**: o modal e o `POST /api/admin/leases` existentes recebem contexto opcional do inquiry. O Admin escolhe Tenant, Professional e termos; apenas sala e nome podem receber prefill seguro. Uma atualização condicional dentro da mesma transação garante uma única conversão, preserva o `RoomId` original e desfaz todo o Lease perdedor em caso de corrida.

---

## 14. O que esta spec explicitamente NÃO autoriza

Implementação de código, geração ou aplicação de migration, qualquer alteração em Supabase/Railway, push, merge ou deploy. Esta spec é o documento de entrada para uma etapa futura de `writing-plans` (plano tarefa-a-tarefa) — não é, ela mesma, uma autorização para começar a escrever código.

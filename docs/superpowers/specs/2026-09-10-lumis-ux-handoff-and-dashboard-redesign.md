# LUMIS — Evolução visual/UX: identidade, fundo animado, Totem check-in, handoff de agendamento, telas de login e redesenho dos 3 dashboards

> Rodada **arquitetural**. As decisões de produto/design já foram aprovadas pelo cliente. Esta SPEC apenas as reconcilia com o código real do branch, fecha ambiguidades técnicas e define contratos antes de qualquer implementação. **Nada foi implementado nesta etapa.** Após aprovação desta SPEC: `superpowers:writing-plans` → plano TDD → nova parada antes de implementar.
>
> **Revisão 2** (após aprovação conceitual): 5 ajustes obrigatórios incorporados — (1) atomicidade handoff+reserva na mesma transação, `/complete` removido; (2) etapa pública `claim` para `StartedAt` antes do login; (3) rate limits concretos e dedicados; (4) KPIs exatos server-side (sem contagem truncada no cliente); (5) design das 3 telas de login. Mais reforços: refresh do `/totem/handoff`, threat model dos tokens, confirmação fundo/BlurFade/carrossel.

- **Branch:** `codex/reception-backend` (worktree `.worktrees/reception-backend`)
- **HEAD local:** `9ccc95c56a41176e7dbdc407fb24ecd17c082c52` (`docs: spec for LUMIS UX round...`) sobre `165f2f1` (`fix(totem): touch swipe...`)
- **`main`:** `fb40fde` — intocada
- **Working tree:** limpa. `165f2f1` já publicado em `origin/codex/reception-backend`; o commit da spec é local.
- **Migration `20260910085909_CheckInManualCode`:** presente no branch e — conforme execução do cliente — já aplicada ao Supabase staging (`xpblbvrmljtvyltvvnpd`) com o segredo `CheckIn__ManualCodeHmacKey` provisionado no Railway staging. Esta rodada **não** mexe nessa migration.
- **Fix de swipe do carrossel (`165f2f1`):** já integrado em HEAD. Esta SPEC **não** o reabre nem o duplica; a seção 4 adiciona um reforço de compositing que o torna redundante **mas o fix JS permanece** (não é substituído por scroll nativo nesta rodada).

---

## 1. Estado atual investigado (contratos a preservar)

### 1.1 Autenticação e sessão

- Sessão **cookie HttpOnly** no servidor. `SessionProvider` (`src/auth/SessionProvider.tsx`) fala com `/api/auth/{csrf,login,session,logout,change-password}`. `apiClient` (`src/api/client.ts`) usa `credentials: 'same-origin'`, guarda o CSRF só em memória, dispara `lumis:unauthorized` em 401, revalida em `focus`/`pageshow` e via `BroadcastChannel('lumis-session')`. **O cliente nunca vê um token.**
- `session.login(email, password)` → `POST /api/auth/login` com `LoginRequest(string Email, string Password)`. **Não há parâmetro de "manter conectado"/`isPersistent`.**
- Endpoints de auth: `/api/auth/{csrf (anon), login (anon+antiforgery), session (ActiveUserPolicy), logout, change-password}`. **Não existe** endpoint de "esqueci minha senha" / reset de senha.
- `Login.tsx` é **um** componente com prop `audience: 'admin' | 'customer' | 'professional'` (rotas `/login`, `/cliente/login`, `/profissional/login`). Hoje é um layout de 2 painéis com copy de marketing, selo de confiança e ícones. `returnUrl` (só `customer`) via `safeCustomerReturnUrl`.
- `SessionProvider` embrulha o app inteiro (`main.tsx`). `ProtectedRoute`: latch `validatedOnce`; ramo anônimo → `/cliente/login?returnUrl=<encodeURIComponent(pathname+search)>` para `/cliente*`, `/login` para staff. O ternário literal e o `safeCustomerReturnUrl` (allowlist `/cliente`, `CUSTOMER_QUERY` que já aceita `handoff=<base64url>`) são **preservados sem alteração**.

### 1.2 Totem hoje

- Rotas **públicas** (fora de `ProtectedRoute`): `/totem` (`TotemEntry`), `/totem/check-in` (`TotemCheckIn`), `/totem/profissionais` (`TotemProfessionals`).
- `TotemEntry`: `LightRays` + `BlurFade` + dois `MagicCard as="button"` → `Tenho código` → `/totem/check-in`; `Não tenho código` → `/totem/profissionais`.
- `TotemProfessionals`: `totemApi.professionals()` (anônimo), 4 fases, carrossel; **hoje** `Continuar` faz `navigate('/cliente/agendar?professionalId=' + active.id)` → cai em `/cliente/login` se anônimo. **É esse passo que o handoff substitui.**
- `TotemProfessionalCarousel`: um único pointer-drag (touch+pen+mouse) com `setPointerCapture` + snap por deslocamento líquido; `.totem-carousel-viewport { touch-action: none }`. **Não alterar.**
- `TotemCheckIn`: `useQrScanner` (worker `qr-scanner`), aba manual com **um** `<input maxLength={6} inputMode="numeric" autoComplete="one-time-code">`, `resolveCheckIn` → preview → `confirmCheckIn` → Visit `WAITING` → auto-retorno em 12 s. Erros genéricos. HMAC, `allowUsed`, rate limiter — **preservados**.
- Magic UI em `src/features/totem/magic/`: `LightRays` (div `pointer-events:none` inline + CSS animando `background`/`transform`), `BlurFade` (`.totem-magic-fade` → `.is-in`; mantém **`filter: blur(0)` + `will-change: filter` permanentes** — ver §4), `MagicCard`, `BorderBeam`, `ProgressiveBlur` (`backdrop-filter`), `RippleButton`, `usePrefersReducedMotion`.

### 1.3 Booking do CUSTOMER hoje (lado celular do handoff)

- `/cliente/agendar` (`CustomerBooking.tsx`, rota `CustomerPolicy`): lê `?professionalId=`, pré-seleciona, `customerApi.availability(...)`, `customerApi.createReservation({ professionalId, startAt, endAt })` → `navigate('/cliente/agendamentos/:id')`.
- `POST /api/customer/reservations` (`CustomerSchedulingEndpoints.CreateReservation`) — estrutura **exata** hoje:
  ```
  customer = GetCustomer(principal)                       // 404 se não existe
  valida ProfessionalId / EndAt>StartAt                   // 400 INVALID_RESERVATION
  professional = Professionals.SingleOrDefault(ativo)     // 404
  roomIds = Rooms.ativas
  transaction = db.Database.BeginTransactionAsync
    resourceLock.AcquireAsync([], roomIds, [ProfessionalId])
    available = availability.FindAvailableRoomAsync(...)   // 409 se indisponível (transação descarta → rollback)
    reservation = Reservation.CreateApproved(roomId, ProfessionalId, StartAt, EndAt, userId, now, customer.Id)
    db.Reservations.Add(reservation)
    db.AuditEntries.Add("RESERVATION_CREATED")
    db.SaveChangesAsync
  transaction.CommitAsync
  return Created(reservation.ToResponse(...))
  ```
  `request` é `CustomerReservationRequest(Guid ProfessionalId, DateTimeOffset StartAt, DateTimeOffset EndAt) : IStrictModuleRequest`.
- `POST /api/customer/register` (`AllowAnonymous` + `AntiforgeryFilter`), `/api/auth/login` — **tudo no celular**.

### 1.4 Dashboards hoje

| Portal | Shell | Index | Tema |
|---|---|---|---|
| Profissional `/profissional` | `ProfessionalShell` (sidebar existe) | `ProfessionalDashboard` (métricas reais, hoje filtra client-side de listas `pageSize:50`) | **claro** |
| Cliente `/cliente` | `CustomerShell` (**topbar horizontal**) | `CustomerHome` (saudação + 2 CTAs + card "cadastro pronto") | **claro** |
| Admin `/admin` | `AdminLayout` (sidebar `LUMIS`, rotas reais) | **`ModuleUnavailable` — não existe dashboard** | **claro** |
| Recepção | — | `ReceptionMonitor` (`receptionApi.overview()` + `.visits()`, refresh 10 s) | **claro** |

- **Rotas CUSTOMER reais:** `/cliente`, `/cliente/agendamentos`, `/cliente/agendar`, `/cliente/agendamentos/:id`. **Não existem** `/cliente/reservas|check-in|profissionais|perfil`.
- **Rotas PROFISSIONAL:** `/profissional`, `/profissional/agenda` (real), `/profissional/disponibilidade` (real); `reservas|atendimentos|locacoes|financeiro|perfil` são `ProfessionalPlaceholder`.
- **Rotas ADMIN:** `/admin` (vazio) + `salas|profissionais|locacoes|reservas|visitas|recepcao|solicitacoes-profissionais|configuracoes` (reais).

### 1.5 Cliente HTTP frontend (`src/api/modules.ts`)

- Existe: `totemApi`, `customerApi` (inclui `issueCheckInToken` → `{ token, manualCode, expiresAt }`), `professionalReservationsApi` (query `{ status, page, pageSize }` — **sem `from`/`to`**), `professionalVisitsApi` (query aceita `from`/`to` + `PagedResponse.totalCount` exato), `professionalAvailabilityApi`, `professionalLeasesApi`, `receptionApi` (`overview`, `visits`, `startVisit`, `endVisit`).
- **Não existe** cliente para `GET /api/admin/dashboard` nem `GET /api/reception/professionals` (endpoints existem no backend).
- `qrcode@1.5.4` + `@types/qrcode` já são dependências; `CustomerReservationDetail.tsx` já renderiza `QRCode.toDataURL(...)` como `<img src="data:image/png...">` sob a CSP atual.

### 1.6 CSP atual (`recepcaototem/Program.cs`, `c89a5c3`)

```
default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self' data:;
img-src 'self' data: blob:; connect-src 'self'; media-src 'self' blob:; worker-src 'self' blob:;
object-src 'none'; base-uri 'self'; frame-ancestors 'none'
```

`img-src ... data:` cobre o QR de `qrcode`; `worker-src ... blob:` cobre `qr-scanner`. **Nenhuma alteração de CSP nesta rodada.**

### 1.7 Domínio / persistência

- DbSets relevantes: `Reservations`, `Visits`, `Customers`, `Professionals`, `Rooms`, `CheckInTokens`, `ProfessionalPresenceTokens`, `RescheduleTokens`, `AuditEntries`.
- Padrão "token efêmero hasheado" já existe 3× (`CheckInToken`, `RescheduleToken`, `ProfessionalPresenceToken`): `Id`, FK, `TokenHash byte[32]`, `IssuedAt/ExpiresAt`, `RevokedAt?`, `UsedAt?`, `Version` (xmin), config EF `bytea` + `timestamp with time zone` + índices únicos `UX_*_TokenHash`. **`TotemBookingHandoff` segue o mesmo padrão.**
- Rate limiters existentes: `CustomerPublicRateLimiter` e `ProfessionalPresenceRateLimiter` — **singletons**, fixed-window, partição por IP (`GetValue("RateLimiting:CustomerIpPermitLimit", 30)` / `60 s`) + partição por identificador hasheado (`15` / `60 s`). Registrados em `Program.cs` (`AddSingleton<...>()`). `CustomerPublicRateLimiter` é **compartilhado** por `/api/customer/register`, `/api/totem/immediate`, `/api/totem/customers/resolve`, `/api/totem/reservations`, `/api/totem/check-in/resolve`, `/api/totem/check-in/confirm` — **orçamento único de 30/IP/60 s para todos**. Um polling de 30/min esgotaria esse orçamento sozinho → o handoff precisa de limiter **dedicado**.
- `PostgreSqlDashboardReader.ReadAsync`: monta `DashboardCounts` com `CountAsync` por métrica; já tem em escopo `day = OperationalTimeZone.GetCivilDayInterval(operationalDate, tz)` (intervalo `StartAt`/`EndAt` do dia civil). Adicionar um `CountAsync` exato é trivial.

---

## 2. Design system LUMIS (tokens)

### 2.1 Paleta canônica (aprovada)

| Papel | Valor | Uso |
|---|---|---|
| `--lumis-bg` | `#181818` | fundo mais ao fundo de toda tela |
| `--lumis-surface` | `#1C1C1C` | superfície base |
| `--lumis-surface-2` | `#222222` | cards |
| `--lumis-surface-3` | `#272727` | card elevado / hover |
| `--lumis-border` | `#3D3D3D` | borda sólida |
| `--lumis-border-soft` | `rgba(255,255,255,.08)` | divisória discreta |
| `--lumis-border-strong` | `rgba(255,255,255,.22)` | borda de foco/ativo |
| `--lumis-text` | `#FFFFFF` | texto primário |
| `--lumis-text-dim` | `#F2F2F2` | texto primário suave |
| `--lumis-muted` | `#888888` | texto secundário |
| `--lumis-success` | `#3FB98C` | **somente** status/sucesso/disponível |
| `--lumis-warning` | `#E0A93B` | **somente** atenção/em atendimento |
| `--lumis-danger` | `#E27878` | erro/alerta |
| `--lumis-info` | `#5B9BD5` | **somente** se semanticamente necessário |

Sem neon, sem roxo/azul decorativo, sem gradiente colorido chamativo. Verde/âmbar/vermelho/azul **só** carregam significado.

### 2.2 Onde declarar

- Bloco único em `:root` no topo de `src/styles.css`, logo após o `:root` atual (que permanece — é o tema claro ainda usado por telas não migradas).
- **Nesta rodada** os aliases por-página (`--tk-*`, `--tc-*`, `--tp-*`, `--te-*`) **passam a apontar** para os `--lumis-*` no ponto de declaração atual (ex.: `--tk-bg: var(--lumis-bg)`), sem mudança visual. Componentes novos consomem `--lumis-*` direto.
- **Sem** refatoração destrutiva do CSS. Remoção dos aliases legados fica para rodadas futuras.

### 2.3 Logo

- Asset oficial único: `/lumis-logo-transparent.png`. Ao migrar o Customer para o DNA escuro, ele deixa de usar `/lumis-logo-dark.png` e passa a usar o mesmo asset dos demais. Sem recriar o logo por portal.

---

## 3. Fundo animado LUMIS

### 3.1 Componente compartilhado

`src/features/lumis/LumisBackground.tsx` (generalização do `LightRays` do Totem):

```tsx
export function LumisBackground({ intensity = 'default' }: { intensity?: 'default' | 'muted' }) {
  return (
    <div className="lumis-bg" data-intensity={intensity} aria-hidden="true" style={{ pointerEvents: 'none' }}>
      <span className="lumis-bg-ray" />
      <span className="lumis-bg-ray" />
      <span className="lumis-bg-ray" />
    </div>
  )
}
```

`src/features/lumis/LumisPageShell.tsx` (fino, opcional por tela): `<div className="lumis-shell"><LumisBackground .../><div className="lumis-shell-content">{children}</div></div>`.

### 3.2 CSS (só `transform`/`opacity`)

```css
.lumis-shell { position: relative; isolation: isolate; min-height: 100dvh; background: var(--lumis-bg); }
.lumis-shell-content { position: relative; z-index: 1; }
.lumis-bg { position: absolute; inset: 0; z-index: 0; overflow: hidden; pointer-events: none; }
.lumis-bg-ray {
  position: absolute; top: -20%; left: -30%; width: 42vw; height: 140%;
  background: linear-gradient(115deg, transparent 0%, rgba(255,255,255,.045) 50%, transparent 100%);
  transform: translate3d(-40vw, -10vh, 0) rotate(9deg);
  opacity: 0; will-change: transform, opacity;
  animation: lumis-ray-drift 26s linear infinite;
}
.lumis-bg-ray:nth-child(2) { top: 10%; width: 30vw; animation-duration: 34s; animation-delay: -9s; }
.lumis-bg-ray:nth-child(3) { top: -30%; width: 52vw; animation-duration: 19s; animation-delay: -15s; }
.lumis-bg[data-intensity="muted"] { opacity: .55; }
@keyframes lumis-ray-drift {
  0%   { transform: translate3d(-40vw, -10vh, 0) rotate(9deg); opacity: 0; }
  12%  { opacity: .5; }
  88%  { opacity: .5; }
  100% { transform: translate3d(120vw, 34vh, 0) rotate(9deg); opacity: 0; }
}
@media (prefers-reduced-motion: reduce) {
  .lumis-bg-ray { animation: none; opacity: .16; transform: translate3d(30vw, 10vh, 0) rotate(9deg); }
}
```

- Raios **nascem fora do viewport → cruzam devagar na diagonal → somem → o próximo entra** (delays negativos escalonados dão continuidade). Ciclo 19–34 s. Nunca rápido, nunca "protetor de tela".
- **Sem `filter` animado, sem `box-shadow` gigante, sem propriedade de layout.** `will-change` só nas propriedades animadas.
- `pointer-events: none` no container **e** inline. **Não interfere** com carrossel/swipe/scanner/formulários/seleção/scroll.

### 3.3 Onde aplicar

- Home `/`; Totem `/totem`, `/totem/profissionais`, `/totem/check-in`, `/totem/handoff`; **telas de login** (§26).
- Portais CUSTOMER (`/cliente`, `/cliente/agendar`, `/cliente/agendamentos`, `/cliente/agendamentos/:id`), PROFISSIONAL (`/profissional/*`), ADMIN (`/admin/*`) e Recepção — via shells, `intensity="muted"` em telas densas.
- **Não** aplicar com intensidade cheia atrás de: tabelas densas, modais, câmera do QR (`.totem-scan-frame` sólida `#000`), inputs, dropdowns. Fundo sempre `z-index: 0`; superfícies sólidas o bastante para legibilidade.

---

## 4. Performance / compositing (auditoria explícita)

### 4.1 Achado

`.totem-magic-fade` (BlurFade) hoje: `filter: blur(8px)` na entrada e, **após a entrada**, `.is-in` deixa `filter: blur(0)` + `will-change: opacity, transform, filter` **permanentes**. Um `filter` não-`none` num ancestral desabilita o scroll nativo por toque de um scroller aninhado no WebKit/Blink mobile — causa raiz do bug de swipe corrigido em `165f2f1` (contornado via pointer-drag JS + `touch-action: none`, **não** removido).

### 4.2 Correção desta rodada

- Ao terminar a transição de entrada do BlurFade, aplicar `filter: none; will-change: auto`. Implementação: `onTransitionEnd` (ou timeout ~550 ms) adiciona `.is-settled`:

```css
.totem-magic-fade.is-in.is-settled { filter: none; will-change: auto; }
```

- **O fix JS do carrossel (`165f2f1`) permanece.** O background novo funciona **junto** com ele; a "settle" só remove a causa raiz original e torna o contorno redundante — o pointer-drag **não** é substituído por scroll nativo nesta rodada.
- **Regra documentada:** nenhum ancestral de elemento com scroll/toque/carrossel/câmera do QR pode carregar `filter` não-`none` persistente nem `will-change: filter`. `LumisBackground` nunca usa `filter` animado. `ProgressiveBlur` usa `backdrop-filter` num elemento folha `pointer-events: none` fora da árvore de scroll — mantido.
- Teste (vitest, leitura de `styles.css`): `.totem-magic-fade.is-in.is-settled` zera `filter`/`will-change`; `.lumis-bg`/`.lumis-shell` não declaram `filter`.

---

## 5. Totem `/totem/check-in` — layout definitivo

### 5.1 Causa raiz

`.totem-kiosk { display: grid; grid-template-columns: minmax(300px, 400px) 1fr }` + `.totem-aside` quase vazia + `.totem-stage` centralizando `max-width: 520px` dentro do `1fr` gigante ⇒ conteúdo colado à esquerda, vazio enorme à direita, "gutter" de logo/relógio superdimensionado, câmera minúscula (`max-height: 44vh`), CTA pequeno.

### 5.2 Novo layout

- **Elimina** o grid de 2 colunas e a `.totem-aside`.
- **Topbar discreta** (`display: flex; justify-content: space-between; align-items: center; padding: clamp(14px,2.5vw,24px) clamp(16px,4vw,40px)`): esquerda `← Voltar` (≥44 px, foco visível, `--lumis-muted`→`--lumis-text`); centro wordmark `LUMIS` (~120–132 px); direita relógio + data em tamanho normal (`KioskClock`, **sem** `clamp(...44px)`).
- **Stage central** (`flex: 1; display: flex; align-items: center; justify-content: center; padding: clamp(24px,5vh,64px) clamp(16px,5vw,40px); overflow-y: auto`) com coluna `max-width: 560px; width: 100%; margin-inline: auto; text-align: center`: eyebrow `CHECK-IN` / `Confirme sua chegada` / `Escolha como deseja identificar seu agendamento` / abas `[Escanear QR] [Digitar código]` / então **QR** (câmera `width: min(70vw, 420px); aspect-ratio: 1/1` + `[Ativar câmera]`) ou **Código** (`Digite o código de 6 dígitos` + **6 caixas** + `[Confirmar]`).
- `.totem-kiosk { position: relative; overflow-x: hidden; min-height: 100dvh; display: flex; flex-direction: column; background: var(--lumis-bg) }` + `<LumisBackground />` primeira filha.

### 5.3 As 6 caixas

- Visual de 6 células, **um** modelo controlado (`token: string` até 6 dígitos). Aceitável: `<input>` overlay dirigindo 6 células, **ou** 6 `<input maxLength={1}>` com controlador. Preservado: `onlyDigits6`/`isComplete6`, `inputMode="numeric"`, `autoComplete="one-time-code"`, `maxLength` efetivo 6, **zero à esquerda**, `paste` preenchendo 6, `submitManual` só com `isComplete6`, botão `disabled` enquanto `!isComplete6 || loading`.
- `<label>`/`aria-label` "código de 6 dígitos" — `getByLabelText(/código de 6 dígitos/i)` deve casar **um** elemento (testes atuais).

### 5.4 Responsivo

1920×1080, 1366×768, tablet landscape/portrait, mobile: **sempre coluna única centralizada**. Câmera por `min()`. Nunca: conteúdo colado à borda, scroll horizontal, card cortado, câmera minúscula. Remove o `@media (max-width: 900px)` da `.totem-aside`.

### 5.5 Regras preservadas

`useQrScanner`, QR forte (worker), código manual de 6 dígitos, HMAC, `resolveCheckIn`/`confirmCheckIn`, `allowUsed` (resolve=false, confirm=true), preview, `Visit` `WAITING`, auto-retorno em 12 s, erros genéricos, CSP. Mudança é **de layout/componente em `TotemCheckIn.tsx` + `styles.css`**.

---

## 6. Carrossel — toque (já resolvido, preservar integralmente)

`165f2f1` integrado em HEAD: pointer-drag único (touch+pen+mouse) com `setPointerCapture` + snap por deslocamento líquido; `.totem-carousel-viewport { touch-action: none }`; testes de swipe touch. **Esta rodada não altera o componente nem os testes, e NÃO troca o pointer-drag por scroll touch nativo.** O único item relacionado é a "settle" do BlurFade (§4).

---

## 7. Totem booking handoff — arquitetura

### 7.1 Princípio

**O cliente não faz login nem cadastro no Totem.** Nenhuma conta CUSTOMER autenticada no quiosque. O Totem entrega a continuação para o **celular do visitante** via QR.

### 7.2 Fluxo (revisado — conclusão atômica, sem `/complete`)

```
/totem
  ├─ "Tenho código"      → /totem/check-in            (inalterado)
  └─ "Não tenho código"  → /totem/profissionais → seleciona profissional → "Continuar"
         → POST /api/totem/booking-handoffs { professionalId }
              → 201 { id, handoffToken, statusToken, expiresAt, professionalName, profession }
         → navigate('/totem/handoff', { state: { handoffId, statusToken, professionalName, profession, expiresAt } })

/totem/handoff  (rota pública, TotemHandoff — statusToken só em navigation state / memória)
  - "Continue no seu celular" + nome/profissão do profissional
  - QR (imagem) codificando  <ORIGIN>/cliente/agendar?handoff=<handoffToken>
  - "Escaneie para continuar seu agendamento"
  - "Aguardando conclusão..." + contagem regressiva (expiresAt - now, mm:ss)
  - "← Escolher outro profissional"
  - polling: POST /api/totem/booking-handoffs/{handoffId}/status { statusToken }  a cada 2 s

Celular  (scan abre o navegador do visitante em /cliente/agendar?handoff=<handoffToken>)
  1. Tela de login (anônimo)  OU  CustomerBooking (já autenticado) monta.
  2. Assim que uma tela pública que conhece o token roda (Login.tsx com audience="customer"
     e handoff no returnUrl, OU CustomerBooking no mount):
        POST /api/totem/booking-handoffs/claim { handoffToken }     (PÚBLICO, sem PII)
          → define StartedAt (1ª vez) + estende expiry: min(CreatedAt+20min, max(ExpiresAt, now+10min))
          → 200 { status: "STARTED", expiresAt }
  3. login/cadastro CUSTOMER — tudo no celular (returnUrl preserva ?handoff=).
  4. CustomerBooking autenticado:
        POST /api/customer/booking-handoffs/resolve { handoffToken }   (CustomerPolicy, read-only)
          → 200 { handoffId, professionalId, professionalName, profession, expiresAt }
          → 410 HANDOFF_EXPIRED  → UI: "convite expirou, escolha um profissional normalmente"
        professionalId pré-selecionado; usuário escolhe data/horário.
  5. POST /api/customer/reservations { professionalId, startAt, endAt, handoffToken }
        Dentro da MESMA transação de criação (§9.6):
          - valida handoff (PENDING, não expirado em `now`, professionalId == request.professionalId, hash confere)
          - cria Reservation (fluxo atual, intocado)
          - handoff.Complete(reservation.Id, now)  +  audit TOTEM_HANDOFF_COMPLETED
          - UM SaveChangesAsync + CommitAsync    → tudo, ou nada
        → 201 reservation
  6. celular navega para /cliente/agendamentos/{reservation.id}

Totem (próximo poll)
  - status == COMPLETED → tela ✓ ("Agendamento concluído!", profissional, data, hora,
    "Tudo certo por aqui.", "Retornando ao início...") → após ~6 s: navigate('/totem') + limpa tudo.
```

### 7.3 Dois tokens (nunca o mesmo)

| Token | Onde vive | Onde vai | Armazenamento |
|---|---|---|---|
| `handoffToken` | QR + URL do celular | `/cliente/agendar?handoff=`, `claim`, `resolve`, `createReservation` | `HandoffTokenHash = SHA256(bytes)` |
| `statusToken` | **só** navigation state / memória do Totem (nunca localStorage/sessionStorage/cookie/URL) | corpo do POST de `status`/`cancel` | `StatusTokenHash = SHA256(bytes)` |

- Ambos: `RandomNumberGenerator.GetBytes(32)`, base64url, **distintos**, hash em DB, **texto puro só na resposta de criação (§9.1)**, nunca logado, nunca auditado em claro, nunca expõe PII.
- Lookup por hash. Falha de decode / tamanho ≠ 32 / não encontrado / token errado → **mesmo** erro genérico `INVALID_HANDOFF` (sem oráculo de existência).

### 7.4 Cancelar / voltar / expirado

- "← Escolher outro profissional": `POST /api/totem/booking-handoffs/{id}/cancel { statusToken }` (`PENDING` → `EXPIRED`), limpa estado local, volta ao carrossel. Idempotente.
- Expirado (poll retorna `EXPIRED`): "Este QR Code expirou." + `[Gerar novo QR]` (cria handoff novo para o mesmo profissional) + `[Escolher outro profissional]`.

### 7.5 Refresh (F5) do `/totem/handoff`

- O `statusToken` vive **só** em navigation state / memória. **Não** é persistido para sobreviver a refresh.
- F5 em `/totem/handoff` sem `location.state.statusToken`: **não** tentar recuperar credencial. Renderizar tela mínima "Sessão encerrada." + `[Voltar ao início]`, **ou** `<Navigate to="/totem" replace />`. Limpar qualquer estado remanescente. (Decisão de implementação: `Navigate` automático para `/totem` — mais simples e coerente com "o Totem sempre volta ao início".)

### 7.6 Garantia de "Totem sem sessão CUSTOMER"

- Todos os endpoints do Totem (`/api/totem/*`, incluindo handoff `create`/`status`/`cancel`/`claim`) são `AllowAnonymous`. O Totem **nunca** chama endpoint `CustomerPolicy`, **nunca** `/api/auth/login`, **nunca** escreve `localStorage`/`sessionStorage`/cookie.
- Páginas `/totem/*` **não** leem `useSession()` para fluxo e **nunca** navegam para rota `ProtectedRoute`. `/totem/handoff` é pública. As **telas de login existem só para dispositivos pessoais** (§26); o Totem nunca navega para elas.
- Teste: ciclo completo de handoff no Totem não seta cookie de auth e não toca `localStorage`/`sessionStorage`.

---

## 8. Entidade / modelo do handoff

### 8.1 `TotemBookingHandoff` (`src/GestaoPredio.Domain/Customers/TotemBookingHandoff.cs`, `sealed class`)

| Campo | Tipo | Notas |
|---|---|---|
| `Id` | `Guid` | PK |
| `ProfessionalId` | `Guid` | FK → `Professionals`, `OnDelete(NoAction)` |
| `HandoffTokenHash` | `byte[]` (32) | índice único `UX_TotemBookingHandoffs_HandoffTokenHash` |
| `StatusTokenHash` | `byte[]` (32) | índice único `UX_TotemBookingHandoffs_StatusTokenHash` |
| `Status` | enum `TotemBookingHandoffStatus` (`smallint`) | `Pending=0`, `Completed=1`, `Expired=2` |
| `CreatedAt` | `DateTimeOffset` | `timestamp with time zone`, `TimestampNormalizer.ToUtcMicroseconds` |
| `ExpiresAt` | `DateTimeOffset` | idem |
| `StartedAt` | `DateTimeOffset?` | setado no 1º `claim` bem-sucedido |
| `CompletedAt` | `DateTimeOffset?` | setado na conclusão (dentro de `CreateReservation`) |
| `ReservationId` | `Guid?` | FK → `Reservations`, `OnDelete(NoAction)`, setado na conclusão |
| `Version` | `uint` | xmin rowversion — concorrência otimista da conclusão |

### 8.2 Fábrica / métodos

- `static Create(Guid professionalId, byte[] handoffTokenHash, byte[] statusTokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt)` — guardas: `professionalId != Guid.Empty`; cada hash 32 bytes; `expiresAt > createdAt`.
- `MarkStarted(DateTimeOffset at, TimeSpan graceWindow, DateTimeOffset hardCeiling)` — **chamado pelo `claim` público**, não pelo `resolve`. Só de `Pending`. Se `StartedAt == null`: `StartedAt = at`, `ExpiresAt = Min(hardCeiling, Max(ExpiresAt, at + graceWindow))`. Chamadas seguintes: no-op (sem re-extensão → sem sessão eterna).
- `Complete(Guid reservationId, DateTimeOffset at)` — só de `Pending`; `Status = Completed`, `ReservationId`, `CompletedAt = at`.
- `MarkExpired(DateTimeOffset at)` — só de `Pending`; `Status = Expired`. Cancelar reusa este estado (enum mínimo, sem `Cancelled`).
- `bool IsUsable(DateTimeOffset now) => Status == Pending && ExpiresAt > now`.

### 8.3 EF config (`TotemBookingHandoffConfiguration`)

`ToTable("TotemBookingHandoffs")`, `HasKey(Id)`, hashes `HasColumnType("bytea").IsRequired()`, timestamps `timestamp with time zone`, `Version.IsRowVersion()`, índices únicos nos dois hashes, `HasIndex(ProfessionalId)`, `HasIndex(ReservationId)`, `HasIndex(x => new { x.Status, x.ExpiresAt })` (limpeza futura). `HasOne<Professional>().WithMany().HasForeignKey(ProfessionalId).OnDelete(NoAction)`; idem `Reservation` opcional.

### 8.4 Constantes de janela

`HandoffWindows` (constantes no endpoint, como `AUTO_RESET_MS`): `Initial = TimeSpan.FromMinutes(5)`, `Grace = TimeSpan.FromMinutes(10)`, `HardCeiling = TimeSpan.FromMinutes(20)` (aplicado como `CreatedAt + HardCeiling`). Não vira config/segredo nesta rodada.

---

## 9. Endpoints

Endpoints do Totem: `AllowAnonymous`, **sem** `AntiforgeryFilter` (coerente com `/api/totem/check-in/*`), **com** rate limiting dedicado (§9.7). Erro sempre `ApiError(code, message)` genérico.

### 9.1 `POST /api/totem/booking-handoffs`  (Totem cria)

- Body `{ professionalId: Guid }`. Rate limit **CREATE** (§9.7).
- Valida profissional existente e `IsActive` → senão `404` genérico.
- Cria: `CreatedAt = now`, `ExpiresAt = now + 5 min`, dois tokens CSPRNG. Audita `TOTEM_HANDOFF_CREATED` (só `Id`/`ProfessionalId`/`CorrelationId`).
- `201` → `{ id, handoffToken, statusToken, expiresAt, professionalName, profession }`. **Única** ocorrência de token em claro. `professionalName`/`profession` já são públicos em `/totem/profissionais`.

### 9.2 `POST /api/totem/booking-handoffs/{id}/status`  (Totem faz polling)

> **Desvio deliberado do "GET" conceitual:** `statusToken` é credencial bearer; regra de privacidade proíbe credencial em query string/URL. Vira `POST` com `{ statusToken }` no corpo. Produto (polling ~2 s, autorizado só pelo `statusToken`) preservado.

- Body `{ statusToken: string }`. Rate limit **STATUS** (§9.7) — partição `{ip}:{id}`, orçamento próprio ≥ polling real.
- `statusToken` inválido/errado → `INVALID_HANDOFF` genérico.
- Expiração preguiçosa: `Pending && ExpiresAt <= now` → `MarkExpired(now)` + `SaveChanges`.
- Respostas:
  - `Pending` → `{ status: "PENDING", expiresAt }`
  - `Completed` → `{ status: "COMPLETED", professionalName, startAt, roomName }` — **sem** nome/telefone/email/CPF do cliente, `customerId`, `reservationId`, token ou detalhe interno. `roomName` é local físico do prédio (não PII).
  - `Expired` → `{ status: "EXPIRED" }`

### 9.3 `POST /api/totem/booking-handoffs/{id}/cancel`  (Totem abandona)

- Body `{ statusToken }`. Rate limit **CANCEL** (§9.7). `Pending` → `MarkExpired(now)` + audita `TOTEM_HANDOFF_CANCELLED`. Já terminal → `200` no-op. Token errado → `INVALID_HANDOFF`.

### 9.4 `POST /api/totem/booking-handoffs/claim`  (celular — 1ª marcação de `StartedAt`, PÚBLICO)

- `AllowAnonymous` (o visitante ainda pode **não** estar autenticado — este é o ponto). Body `{ handoffToken }`. Rate limit **CLAIM** (§9.7).
- O `handoffToken` forte é a autorização da ação.
- Valida: token confere → senão `400 INVALID_HANDOFF`. `Status == Pending && ExpiresAt > now` (expiry inicial) → senão `410 HANDOFF_EXPIRED`. Ambos genéricos.
- `MarkStarted(now, Grace = 10 min, HardCeiling = CreatedAt + 20 min)` + `SaveChanges`. Chamadas seguintes não estendem indefinidamente (no-op se `StartedAt` já setado).
- **Não retorna PII:** `200 { status: "STARTED", expiresAt }`. Sem `professionalId`, sem nome.
- Chamado por: `Login.tsx` (`audience === 'customer'` e `handoff` extraível do `returnUrl`, no `useEffect` de mount) **e** `CustomerBooking.tsx` (mount, antes do `resolve`, idempotente). Cobre tanto o desvio anônimo por login/cadastro quanto o caminho já-autenticado.

### 9.5 `POST /api/customer/booking-handoffs/resolve`  (celular — contexto de booking)

- `RequireAuthorization(CustomerPolicy)` + `AntiforgeryFilter`. Body `{ handoffToken }`. Rate limit **RESOLVE** (§9.7).
- **Read-only** (não muta estado — o `claim` já marcou `StartedAt`). `!IsUsable(now)` → `410 HANDOFF_EXPIRED`.
- `200` → `{ handoffId, professionalId, professionalName, profession, expiresAt }` para pré-seleção.

### 9.6 `POST /api/customer/reservations`  (extensão backward-compatible — conclusão atômica)

- Request estendido: `CustomerReservationRequest(Guid ProfessionalId, DateTimeOffset StartAt, DateTimeOffset EndAt, string? HandoffToken)`.
- **`HandoffToken` ausente/null → comportamento atual idêntico** (old clients continuam funcionando; `IStrictModuleRequest` aceita o campo novo opcional).
- **`HandoffToken` presente → dentro da MESMA unidade transacional da criação:**
  1. `handoff = await db.TotemBookingHandoffs.SingleOrDefaultAsync(x => x.HandoffTokenHash == SHA256(decode(HandoffToken)), ct)` — **tracked** (sem `AsNoTracking`).
  2. Se `handoff is null || handoff.Status != Pending || handoff.ExpiresAt <= now || handoff.ProfessionalId != request.ProfessionalId` → `400 INVALID_HANDOFF` genérico **antes** de adicionar a reserva (transação descarta → rollback, nada persistido).
  3. Se `handoff.Status == Completed` (retry de rede após sucesso) → `409 HANDOFF_ALREADY_USED` genérico (o cliente deve recarregar seus agendamentos; **não** cria segunda reserva).
  4. Cria `Reservation` exatamente como hoje (`Reservation.CreateApproved(...)`, `db.Reservations.Add`, audit `RESERVATION_CREATED`).
  5. `handoff.Complete(reservation.Id, now)` + `db.AuditEntries.Add("TOTEM_HANDOFF_COMPLETED")` (sem token/PII).
  6. **UM** `await db.SaveChangesAsync(ct)` (reserva + conclusão do handoff + audits juntos) + `await transaction.CommitAsync(ct)`.
  7. Concorrência: duas chamadas simultâneas com o mesmo token → a 2ª `SaveChangesAsync` lança `DbUpdateConcurrencyException` no `handoff.Version` → catch → `409` genérico, **e a reserva perdedora sofre rollback junto**.
- **Ou tudo acontece, ou nada acontece.** Sem regra de booking duplicada (a criação continua sendo a única do `CreateReservation`).
- **`POST /api/customer/booking-handoffs/{id}/complete` é REMOVIDO** — deixou de ser necessário.
- **Segurança (checklist do ajuste 1):** mesmo profissional (passo 2); handoff válido (passo 2); não concluído antes (passos 2–3); token correto (hash, passo 1); Reservation criada **pelo fluxo correspondente** — não há mais input `reservationId`, o cliente **não** pode informar um id antigo/arbitrário; a reserva é criada para `customer.Id` do principal autenticado. Idempotência via `409 HANDOFF_ALREADY_USED`.

### 9.7 Rate limiting — `TotemHandoffRateLimiter` (novo, dedicado)

Novo singleton em `recepcaototem/Features/Totem/TotemHandoffRateLimiter.cs`, espelhando o padrão de `ProfessionalPresenceRateLimiter` (fixed-window, `QueueLimit = 0`, `AutoReplenishment = true`), **orçamento separado** de `CustomerPublicRateLimiter`/check-in. Uma partição por endpoint:

| Ação | Partição | Limite (default) | Config key | Racional |
|---|---|---|---|---|
| **CREATE** `POST /booking-handoffs` | IP | **10 / 60 s** | `RateLimiting:HandoffCreateIpPermitLimit` | 1 handoff por visitante; 10/min/IP bloqueia script |
| **STATUS** `POST /{id}/status` | `{ip}:{id}` | **50 / 60 s** | `RateLimiting:HandoffStatusPermitLimit` | polling normal ≈ 30/min → 50 dá folga para jitter/retry, nunca 429 |
| **CANCEL** `POST /{id}/cancel` | IP | **15 / 60 s** | `RateLimiting:HandoffCancelIpPermitLimit` | baixo/moderado |
| **CLAIM** `POST /booking-handoffs/claim` | IP (do celular) | **20 / 60 s** | `RateLimiting:HandoffClaimIpPermitLimit` | público; idempotente (só 1 extensão real por token) |
| **RESOLVE** `POST /customer/booking-handoffs/resolve` | IP | **20 / 60 s** | `RateLimiting:HandoffResolveIpPermitLimit` | autenticado; guarda leve por simetria |

- Janela comum: `RateLimiting:HandoffWindowSeconds` (default `60`).
- 429 → `ApiError("TOO_MANY_REQUESTS", "Tente novamente mais tarde.")`, statusCode 429 (igual aos demais surface públicos).
- **O polling normal (a cada ~2 s, por toda a vida do handoff, até 20 min) nunca gera 429**: 50/60 s por `{ip}:{id}` > 30/60 s de tráfego real, e não compartilha orçamento com create/cancel/check-in/register.
- `ModulesApiFactory` (`UseEnvironment("Testing")`) sobe todos os `Handoff*PermitLimit` para `"10000"` (como já faz com `CustomerIpPermitLimit`); testes de rate limit isolam num host derivado via `WithConfig(...)` restaurando os defaults.

### 9.8 Rotas frontend novas / alteradas

- **Nova rota pública** `/totem/handoff` → `TotemHandoff` (`src/pages/TotemHandoff.tsx`), fora de `ProtectedRoute`. Carregada só via `navigate(..., { state })`; sem `state.statusToken` (refresh) → `<Navigate to="/totem" replace />` (§7.5).
- `/cliente/agendar` inalterada como rota; `CustomerBooking.tsx` ganha tratamento de `?handoff=` (claim idempotente + resolve + `handoffToken` no `createReservation`).
- `Login.tsx` (`audience === 'customer'`): `useEffect` de mount chama `claim` quando há `handoff` no `returnUrl`.
- `frontend-portals.test.ts`: "Continuar" do Totem não navega mais para `/cliente/login`; `/totem/handoff` anônima; o Totem nunca navega para telas de login.
- **Nenhuma** rota `/continuar` extra — o QR aponta direto para `/cliente/agendar?handoff=` (arquitetura mínima).

---

## 10. Segurança / tokens (resumo normativo)

1. `handoffToken` e `statusToken`: CSPRNG 32 bytes, base64url, **distintos**, hash SHA-256 em DB, texto puro só em §9.1.
2. Nenhum token em URL/query string de API. `statusToken` só em corpo de POST e só em navigation state / memória do Totem. `handoffToken` aparece na URL aberta pelo QR — ver threat model §27.
3. Erros genéricos e uniformes (`INVALID_HANDOFF` / `HANDOFF_EXPIRED` / `HANDOFF_ALREADY_USED`) — sem oráculo de existência.
4. `StartedAt` é marcado por `POST /api/totem/booking-handoffs/claim` **público** (o visitante pode não estar autenticado ao abrir o QR). `resolve` é read-only. Conclusão só via `CreateReservation` transacional.
5. Auditoria: só `Id`/`ProfessionalId`/`ReservationId`/`CorrelationId` — nunca token, nunca PII do cliente.
6. Resposta `COMPLETED` ao Totem: só `professionalName`, `startAt`, `roomName`. Tela pública.
7. Rate limiting dedicado em todos os endpoints de handoff (§9.7).
8. Expiração: inicial 5 min; **uma** extensão de carência para `now + 10 min` a partir do `claim` legítimo, teto absoluto `CreatedAt + 20 min`. Sem sessão eterna. Enforcement preguiçoso em `status`/`claim`/`resolve`/`CreateReservation`.
9. `/totem/*` nunca autentica CUSTOMER; o quiosque não guarda cookie/access/refresh/senha/email/sessão/localStorage/sessionStorage de cliente. Telas de login nunca são alcançadas pelo Totem.
10. `CreateReservation` com handoff: sem input `reservationId`; conclusão só da reserva criada na mesma transação, para o `customer` autenticado.

---

## 11. Polling

- `TotemHandoff`: `setInterval(2000)` → `POST .../{id}/status { statusToken }`. Limpa no unmount e em status terminal.
- `PENDING` → continua; atualiza a contagem pelo `expiresAt` retornado (reflete a carência do `claim` automaticamente).
- `COMPLETED` → para o polling; troca para ✓; `setTimeout(6000)` → `navigate('/totem')` + `reset()` (limpa `handoffId`, `statusToken`, dados do profissional, contagem, fase).
- `EXPIRED` → para o polling; `[Gerar novo QR]` + `[Escolher outro profissional]`.
- Erro de rede num poll: não quebra; após **5** falhas consecutivas mostra aviso suave com `[Tentar novamente]`; senão segue tentando.
- `prefers-reduced-motion` não afeta o polling (só a animação do spinner/contagem).
- **Sem WebSocket** (fora de escopo).
- **Refresh (F5):** `statusToken` só em navigation state → sem `state` a página redireciona para `/totem` (§7.5). Nunca recupera credencial de storage.

---

## 12. Conclusão (tela ✓ do Totem)

- Disparada **somente** quando `status == COMPLETED` (reserva realmente criada no celular, na transação atômica).
- Mostra: "Agendamento concluído!", nome do profissional, data, hora (`startAt`), "Tudo certo por aqui.", "Retornando ao início...".
- Após ~6 s: `navigate('/totem')` + limpeza total do estado efêmero (§14).

---

## 13. Expiração (regra única, documentada)

| Momento | `ExpiresAt` |
|---|---|
| Criação (`POST /api/totem/booking-handoffs`) | `CreatedAt + 5 min` |
| 1º `claim` do celular (`MarkStarted`, `StartedAt == null`) | `Min(CreatedAt + 20 min, Max(ExpiresAt, now + 10 min))` |
| `claim` subsequente | inalterado (no-op) |
| Sempre (`status`/`claim`/`resolve`/`CreateReservation`) | se `Pending && ExpiresAt <= now` → `Expired` |

- O Totem exibe `ExpiresAt - now` (mm:ss), relendo `expiresAt` de cada poll.
- Sem job em background. Regra: "5 min para começar; escaneou/`claim`, 10 min para terminar; nunca mais que 20 min no total".

---

## 14. Limpeza do quiosque

- Todo o estado do handoff vive em estado React / navigation state da subárvore `/totem/*`. `navigate('/totem')` (auto após ✓, "Escolher outro profissional", "Voltar", refresh sem state) desmonta → estado some. `reset()` explícito zera `handoffId`, `statusToken`, profissional, contagem, fase.
- **Nada** é escrito em `localStorage`/`sessionStorage`/cookie pelo fluxo do quiosque. O `statusToken` **não** é persistido para sobreviver a refresh.
- Teste: após um ciclo completo (criar → polling → COMPLETED → auto-retorno) e após um F5 em `/totem/handoff`, `localStorage`/`sessionStorage` intactos e sem cookie de auth.

---

## 15. Continuação no celular (CUSTOMER)

- Scan do QR abre `<ORIGIN>/cliente/agendar?handoff=<handoffToken>` no navegador do visitante.
- `ProtectedRoute` anônimo → `/cliente/login?returnUrl=%2Fcliente%2Fagendar%3Fhandoff%3D<token>` (`safeCustomerReturnUrl` já aceita `handoff=<base64url>` — nenhuma mudança em `returnUrl.ts`).
- **`claim` (público)** é chamado assim que uma tela que conhece o token roda:
  - `Login.tsx` (`audience === 'customer'`), no mount, se `handoff` está no `returnUrl` — cobre o desvio anônimo por login/cadastro (o `returnUrl` é repassado ao `/cliente/cadastro`, então o `claim` no primeiro `/cliente/login` cobre toda a jornada login↔cadastro).
  - `CustomerBooking.tsx`, no mount, antes do `resolve` — cobre o caminho já-autenticado (straight-through) e é idempotente se o `claim` já rodou.
- Celular: já logado / loga / cria conta — tudo no celular.
- `CustomerBooking.tsx` com `?handoff` (autenticado):
  1. `claim` idempotente (acima).
  2. `POST /api/customer/booking-handoffs/resolve { handoffToken }` → `professionalId` (+ nome/profissão) para pré-seleção; ignora `?professionalId=` quando há `handoff`. `410` → mensagem "Este convite expirou. Você pode escolher o profissional normalmente." e segue o fluxo padrão.
  3. Usuário escolhe data/horário.
  4. `customerApi.createReservation({ professionalId, startAt, endAt, handoffToken })` → conclusão atômica no backend (§9.6) → `navigate('/cliente/agendamentos/:id')`.
- `professionalId` do handoff pré-selecionado → usuário escolhe data/hora → reserva criada e handoff concluído **numa transação**.

---

## 16. Dashboard PROFISSIONAL (`/profissional`) — KPIs exatos

**Dados reais:** `GET /api/professional/me` → `{ name, profession, description, photoUrl }`; `GET /api/professional/visits` (aceita `from`/`to` sobre `ArrivedAt` + `PagedResponse.totalCount` **exato** via `CountAsync`); `GET /api/professional/reservations` (`status` + paginação; **hoje sem `from`/`to`**); `GET /api/professional/availability` → `effectiveDays[].intervals`; `GET /api/professional/leases`; `GET /api/professional/presence` (status próprio).

- **Shell:** já é sidebar (`ProfessionalShell`). Reestilizar para DNA escuro via tokens + `LumisBackground` `muted`. Nav (já existente): Dashboard, Agenda, Disponibilidade, Atendimentos, Reservas, Locações, Financeiro, Perfil. Topbar: `PROFISSIONAL` / "Olá, {primeiro nome}" / "Seu espaço, sua agenda, mais possibilidades." / data + avatar + nome + profissão.
- **Pré-requisito de backend (gap #, obrigatório neste redesenho):** adicionar `from`/`to` opcionais a `GET /api/professional/reservations` (backward-compatible, espelha o `ReservationListQuery.from/to` do admin) — o plano TDD **inclui** essa extensão. Com ela, todos os KPIs e a agenda usam `totalCount` exato + a lista da janela do dia.
- **Cards do topo — todos exatos server-side, `pageSize: 1` + `totalCount`:**
  1. **Atendimentos hoje** = visitas com `ArrivedAt` no dia civil e `status ∈ {IN_SERVICE, ENDED}` (um atendimento = visita que começou): `professionalVisitsApi.list({ status: 'all', from: <início do dia>, to: <fim do dia>, page: 1, pageSize: 100 })` e contar `items` por status, **ou** duas chamadas `status: 'IN_SERVICE'` e `status: 'ENDED'` somando `totalCount`. A 2ª forma é exata sem depender de `pageSize`; é a adotada.
  2. **Próximo horário** = próxima reserva `APPROVED` com `endAt >= now` (o `next` já calculado).
  3. **Disponibilidade** = de `availabilityApi.get()` `effectiveDays` do dia → total de horas abertas; barra discreta.
  4. **Check-ins confirmados hoje** = `professionalVisitsApi.list({ status: 'all', from: <início>, to: <fim>, page: 1, pageSize: 1 }).totalCount` (toda visita registrada é um check-in confirmado). Exato — **nunca** contar client-side a partir de uma lista truncada.
- **Card principal "Agenda de hoje":** `professionalReservationsApi.list({ status: 'all', from: <início>, to: <fim>, page: 1, pageSize: 100 })` (com a extensão `from`/`to`), cada linha com status **derivado** do `Visit`: sem Visit → "Agendado"; `WAITING` → "Aguardando"; `IN_SERVICE` → "Em atendimento"; `ENDED` → "Concluído"; `CANCELLED` → "Cancelado". `totalCount` é o resumo exato do dia. Nada hardcoded.
- **Coluna direita:** "Disponibilidade de hoje"; "Próximas reservas"; "Resumo/avisos" (nº de reservas `kind ∈ {RESCHEDULE, CANCELLATION}` `PENDING`).
- **Não inventar** mensagens/chat/documentos.
- **Escopo:** redesenha **Dashboard + shell + Agenda** (visual). `ProfessionalPlaceholder` continuam placeholders (stretch opcional).

---

## 17. Dashboard CLIENTE (`/cliente`)

**Dados reais:** `GET /api/customer/me` → `CustomerProfileDto`; `GET /api/customer/reservations` → `PagedResponse<ReservationDto>` (futuros = "próximos", passados = "histórico recente", mesma lista ordenada); `GET /api/customer/reservations/{id}`; `POST /api/customer/reservations/{id}/check-in-token` → `{ token, manualCode, expiresAt }` (real, gate `CHECK_IN_NOT_ELIGIBLE` = 1 h antes → fim); `QRCode.toDataURL` client-side.

- **Shell:** hoje topbar horizontal (`CustomerShell`). Reestruturar para **sidebar** desktop + drawer mobile (padrão `AdminLayout`), DNA escuro, `LumisBackground` `muted`. Logo → `/lumis-logo-transparent.png`.
- **Nav — só destinos reais:** Dashboard (`/cliente`), Agendamentos (`/cliente/agendamentos`), Novo agendamento (`/cliente/agendar`). **Não** adicionar itens para páginas inexistentes. "Check-in" é ação (card QR), não rota.
- Topbar: `ÁREA DO CLIENTE` / "Olá, {primeiro nome}!" / "Seu bem-estar em um só lugar." / data + avatar + nome.
- **Card "Próximo atendimento":** `reservations.items.filter(status === 'APPROVED' && endAt >= now).sort(startAt)[0]` → profissional, data, hora, sala, status, `[Ver detalhes]` → `/cliente/agendamentos/:id`. **Especialidade** omitida (`ReservationDto` não traz profissão — gap flagado; card funciona sem).
- **Card "Meu QR Code":** reusa **o fluxo real** `issueCheckInToken`. Elegível → `[Gerar QR Code]`; gerado → `<img>` do QR + `manualCode` de 6 dígitos espaçado + `[copiar]`. **Nunca** hardcode. Não elegível → texto "Disponível 1 h antes do horário".
- **Card "Novo agendamento":** "Agende seus atendimentos de forma rápida e prática." + `[Novo agendamento →]` → `/cliente/agendar`.
- **Listas:** "Próximos agendamentos" (futuros); "Histórico recente" (passados, mesma lista). **Sem notificações falsas** — bloco de notificações **omitido**.

---

## 18. Dashboard ADMIN (`/admin`) — KPIs exatos server-side

Hoje `/admin` index = `ModuleUnavailable`. Esta rodada **constrói** o dashboard.

**Dados reais:** `GET /api/admin/dashboard` → `DashboardSnapshot(OperationalDate, Counts, Financial, Alerts, Agenda, CurrentVisits, Rooms)`. `GET /api/reception/overview`, `GET /api/reception/professionals` → `ReceptionProfessionalResponse[]` (tem `Presence`). `GET /api/admin/operational-alerts`.

- **Cliente TS a adicionar:** `dashboardApi.get()` para `/api/admin/dashboard`; `receptionApi.professionals()` para `/api/reception/professionals`. Endpoints já existem no backend.
- **Gap de backend # — `TodayCheckIns` (contagem exata server-side):** adicionar ao record `DashboardCounts` o campo `int TodayCheckIns` e, em `PostgreSqlDashboardReader.ReadAsync`, um `CountAsync` exato: `db.Visits.CountAsync(v => v.ArrivedAt >= day.StartAt && v.ArrivedAt < day.EndAt, ct)` (o `day` já está em escopo). **Não** é mudança de schema — só o DTO/record + a query. **Não** trazer 50 registros para contar no frontend.
- **KPI cards (todos reais):**
  1. **Atendimentos hoje** = `Counts.TodayReservations`.
  2. **Check-ins realizados** = `Counts.TodayCheckIns` (novo agregado exato).
  3. **Salas ocupadas** = `Counts.OccupiedRooms` (de `Counts.ActiveRooms` / `Rooms`).
  4. **Reservas hoje** = `Counts.TodayReservations`, com `Counts.PendingReservations` como sub-stat ("aguardando aprovação"). Ambos exatos server-side.
- **Painel "Recepção · Em atendimento":** `dashboard.CurrentVisits` (ordem, cliente, serviço/profissional, status, espera, `DurationMinutes`). `VisitorName` é PII, mas esta é tela **de operador** autenticado (`ADMINISTRADOR`) — nome completo aceitável aqui (≠ Totem público).
- **"Ocupação das salas":** `dashboard.Rooms` (`RoomName`, `Status` "Em uso"/"Livre", `NextCommitmentAt`).
- **"Profissionais presentes":** `receptionApi.professionals()` → lista com `Presence`. Usar **"presentes"** (presença física), não "online".
- **"Atividades/Alertas":** `dashboard.Alerts` (`Total`/`Warning`/`Critical` + `Recent`). Só dados reais.
- Admin mantém densidade operacional; DNA escuro via tokens + `LumisBackground` `muted`. Sidebar `AdminLayout` (rotas reais) — manter itens, ajustar estilo, construir o index `/admin`.

---

## 19. Dados/endpoints reutilizados (mapa)

| Tela | Endpoint(s) | Cliente TS |
|---|---|---|
| Totem handoff (Totem) | `POST /api/totem/booking-handoffs`, `.../{id}/status`, `.../{id}/cancel` | **novo** `totemApi.createHandoff/pollHandoff/cancelHandoff` |
| Handoff (celular) | `POST /api/totem/booking-handoffs/claim` (público), `POST /api/customer/booking-handoffs/resolve` (CustomerPolicy), `POST /api/customer/reservations` (estendido com `handoffToken`) | **novo** `totemApi.claimHandoff`, `customerApi.resolveHandoff`; `customerApi.createReservation` ganha `handoffToken?` |
| Dashboard Profissional | `/api/professional/me`, `/api/professional/reservations` (+ `from`/`to` a adicionar), `/api/professional/visits` (já tem `from`/`to`+`totalCount`), `/api/professional/availability`, `/api/professional/leases`, `/api/professional/presence` | existentes (+ query `from`/`to` em `professionalReservationsApi`) |
| Dashboard Cliente | `/api/customer/me`, `/api/customer/reservations`, `/api/customer/reservations/{id}`, `/api/customer/reservations/{id}/check-in-token` | existentes |
| Dashboard Admin | `/api/admin/dashboard` (+ `Counts.TodayCheckIns`), `/api/reception/overview`, `/api/reception/visits`, `/api/reception/professionals`, `/api/admin/operational-alerts` | **novos** `dashboardApi.get`, `receptionApi.professionals` |
| Telas de login | `/api/auth/{csrf,login,session}` (existentes) | `session.login` (sem alteração de assinatura) |

---

## 20. Responsivo (todos os dashboards + login)

- Sidebar fixa lateral no desktop (≥1024 px); drawer/hamburger no tablet/mobile (padrão `open` do `AdminLayout`, com overlay e `aria-label`).
- Grade de KPIs: 4-up desktop → 2-up tablet → 1-up mobile (mobile **nunca** 4 KPIs lado a lado).
- Tabelas: container `overflow-x: auto` no mobile; nunca espremidas. `body` nunca rola horizontalmente.
- Totem `/totem/handoff`: coluna única; QR `min(70vw, 320px)`; contagem abaixo.
- Totem `/totem/check-in`: coluna única em todos os breakpoints; câmera por `min()`.
- **Login (§26.5):** card com largura confortável; teclado não esconde o CTA; inputs ≥44 px; sem scroll horizontal; `returnUrl`/`handoff` preservados no retorno ao booking.

---

## 21. Acessibilidade

- Mantém `focus-visible`, `aria-label`, `role`, teclado, contraste, alvo de toque ≥44 px.
- Carrossel: `role="listbox"`/`option`, `aria-selected`, Arrow/Home/End — inalterado.
- QR do handoff: `<img alt="QR Code para continuar o agendamento no seu celular">`.
- Contagem regressiva num container `aria-live="polite"` com atualização de texto **grosseira** (≈a cada 30 s), não a cada segundo.
- Status nunca só por cor: texto + ícone (disponível / em atendimento / expirado / concluído).
- `prefers-reduced-motion: reduce` → remove/para o fundo animado e a entrada BlurFade; interface visualmente correta; nenhuma função depende de animação.

---

## 22. Testes

### 22.1 Backend (xUnit integração)

- `POST /api/totem/booking-handoffs`: `handoffToken` ≠ `statusToken`; DB guarda só hashes; audit sem token/PII.
- `status` com `statusToken` correto: `PENDING`→`COMPLETED`→`EXPIRED`; token errado → `INVALID_HANDOFF` genérico.
- **`claim` (público)**: marca `StartedAt` **sem autenticação**; aplica carência 10 min; 2ª chamada não re-estende; teto de 20 min desde `CreatedAt`; token inválido/expirado → `410`/`INVALID_HANDOFF`; **não retorna PII**.
- **Atomicidade `CreateReservation` + handoff:**
  - com `handoffToken` válido → reserva criada **e** handoff `COMPLETED` com `ReservationId`/`CompletedAt`, num único commit.
  - handoff inválido/expirado/profissional divergente → `400 INVALID_HANDOFF` **e nenhuma reserva persistida**.
  - retry com handoff já `COMPLETED` → `409 HANDOFF_ALREADY_USED` **e nenhuma segunda reserva**.
  - duas chamadas concorrentes com o mesmo token → uma vence, a outra `409` com rollback da reserva.
  - **sem** `handoffToken` → comportamento atual idêntico (regressão dos testes existentes de `CreateReservation`).
  - **não é possível** um CUSTOMER concluir handoff com `reservationId` arbitrário (não há mais esse input).
- `cancel` → `EXPIRED`; idempotente.
- **Rate limit dedicado:** `status` a ~2 s por toda a vida do handoff (até 20 min) **nunca** retorna 429; `create`/`claim`/`cancel` respeitam seus limites; o orçamento **não** é compartilhado com check-in/register.
- `Counts.TodayCheckIns`: contagem exata server-side (visitas com `ArrivedAt` no dia civil), incl. cenário > 50 visitas.
- Um ciclo `/totem/*` não seta cookie de auth.

### 22.2 Frontend (vitest, TDD)

- `TotemHandoff`: renderiza nome/profissão + QR + contagem; faz polling e troca para ✓ em `COMPLETED`; auto-retorno ~6 s com estado limpo e `localStorage`/`sessionStorage` intactos; `EXPIRED` mostra os dois botões; "Escolher outro profissional" chama `cancel` e volta ao carrossel; **F5 sem `state` → redireciona para `/totem`, sem recuperar credencial**.
- `Login.tsx`: as 3 audiences renderizam o card correto (§26); **sem** link de "esqueci a senha", **sem** checkbox "lembrar de mim", **sem** login social; `audience === 'customer'` com `handoff` no `returnUrl` chama `claim` no mount; `returnUrl`/`handoff` preservados; o Totem nunca navega para essas telas (asserção em `frontend-portals.test.ts`).
- `CustomerBooking` com `?handoff=`: chama `claim` (idempotente) + `resolve`, pré-seleciona profissional, passa `handoffToken` ao `createReservation`; caminho `410` mostra mensagem e segue normal.
- `TotemCheckIn` novo layout: topbar (`← Voltar` / `LUMIS` / relógio), stage central, câmera grande, 6 caixas; **todos** os comportamentos de check-in preservados (reusar testes, ajustar seletores só onde a estrutura mudou).
- Dashboards (Profissional/Cliente/Admin): renderizam de dados de API mockados; **sem** nomes/números hardcoded; rótulos de status derivados; KPIs vêm de `totalCount`/`Counts.*` exatos (nenhum card computa contagem a partir de lista paginada truncada); card QR usa o fluxo real (`issueCheckInToken` mockado); caminho `prefers-reduced-motion`.
- `LumisBackground`: `aria-hidden`, `pointer-events: none`, sem `filter` em ancestrais; reduced-motion desliga a animação.
- `styles.css` (leitura): `--lumis-*` em `:root`; `--tk/tc/tp/te-*` resolvem através deles; `.totem-magic-fade.is-in.is-settled` zera `filter`/`will-change`.

### 22.3 Gates (de `recepcaototem/ClientApp` + backend)

`dotnet test tests/GestaoPredio.IntegrationTests` · `npx vitest run` · `npx tsc -b` · `npx vite build` · `npm run --silent verify:production-bundle` · `git diff --check`.

---

## 23. Implicações de migration

- **Uma** migration nova: cria `TotemBookingHandoffs` (colunas §8.1; índices únicos nos dois hashes; `IX_*_ProfessionalId`; `IX_*_ReservationId`; `IX_*_Status_ExpiresAt`; FKs `NoAction`; `xmin`).
- **Puramente aditiva:** sem `ALTER`/`DROP`/rename de tabela/coluna existente, sem backfill, sem `UPDATE` massivo, sem tocar `Reservations`/`Visits`/`CheckInTokens`. `Down()` = `DropTable("TotemBookingHandoffs")`.
- **`Counts.TodayCheckIns` e a query `from`/`to` de `/api/professional/reservations` NÃO são migration** — são DTO/record + LINQ. Zero schema.
- **Não** criada nesta rodada (SPEC apenas) — mesmo padrão da §26 do design de check-in. Será criada na rodada de implementação (`dotnet ef migrations add TotemBookingHandoff` offline) e aplicada ao **staging Supabase pelo cliente**.
- Todo o trabalho de tokens CSS, fundo, dashboards, layout do check-in e telas de login é **migration-free**.
- `20260910085909_CheckInManualCode` (já aplicada) não é afetada.

---

## 24. Implicações operacionais

- **Quiosque:** o código garante que `/totem/*` nunca autentica; recomenda-se perfil de navegador **dedicado** ao quiosque (sem login CUSTOMER no dispositivo), pois um navegador compartilhado poderia carregar um cookie de cliente antigo. Nota de deploy, não código que desloga ninguém.
- **Tabela `TotemBookingHandoffs`:** ~1 linha por visita "sem código" no Totem. Linhas minúsculas (só FKs, sem PII). Limpeza periódica (`Expired`/`Completed` > N dias) é *nice-to-have*; o `IX_*_Status_ExpiresAt` já prepara.
- **Config nova (opcional, com defaults):** `RateLimiting:HandoffCreateIpPermitLimit=10`, `HandoffStatusPermitLimit=50`, `HandoffCancelIpPermitLimit=15`, `HandoffClaimIpPermitLimit=20`, `HandoffResolveIpPermitLimit=20`, `HandoffWindowSeconds=60`. Sem config → defaults valem. **Sem novos segredos.** Janelas 5/10/20 min são constantes.
- **`GET /api/professional/reservations`** ganha `from`/`to` opcionais (backward-compatible) — nenhum cliente atual quebra.
- **Railway/Supabase:** uma migration a aplicar no deploy (pelo cliente). CSP inalterada. Carga de polling desprezível (1 quiosque = 1 req/2 s; limiter dedicado).
- **CSP:** QR do handoff é `<img src="data:image/png;...">` de `qrcode` — coberto por `img-src 'self' data: blob:`. Nada muda.

---

## 25. Fora de escopo (reafirmado)

Intelbras real, Meta/WhatsApp, **Resend / recuperação de senha**, pagamentos, app nativo, **WebSocket para o handoff**, refatoração geral de backend, redesenho de DB sem necessidade, novos módulos admin sem requisito, analytics, domínio customizado, **merge em `main`**, push/deploy/migration remota nesta etapa. Investigação do **POST 400 do cadastro profissional** permanece separada — a máscara de WhatsApp `aa1b3f4` é UX aprovada mas **não** prova que o 400 foi corrigido. Construir como features completas `/cliente/perfil`, `/cliente/profissionais` e os `ProfessionalPlaceholder` — fora desta rodada. **"Lembrar de mim"** e **"Esqueci minha senha"** — não há backend; **não** exibir UI decorativa.

---

## 26. Telas de login (design aprovado — consolidado)

### 26.1 Princípio

Login **simples, humano e consistente** com o resto do LUMIS. **ZERO:** Google, iCloud, Apple, login social, foto de prédios/consultórios, ilustrações artificiais, excesso de ícones, excesso de texto. Um único componente `Login` com prop `audience` (como hoje) — o layout de 2 painéis com copy de marketing e selo de confiança **é substituído** por um card central discreto.

### 26.2 DNA visual

- Mesmo `LumisBackground` animado aprovado (`#181818` + feixes brancos/cinza em movimento lento). **O fundo NÃO é imagem estática.**
- Card central discreto: `background: var(--lumis-surface-2)`; `border: 1px solid var(--lumis-border)`; raio consistente com o resto (ex.: 16 px); **sem** glassmorphism exagerado; `max-width` ~380–420 px; `margin: auto`; padding confortável.
- Dentro do card, de cima para baixo: logo `LUMIS` (`/lumis-logo-transparent.png`) · eyebrow pequeno · título · texto curto · campo E-mail · campo Senha (com toggle mostrar/ocultar, mantido) · CTA branco `Entrar` (`background: var(--lumis-text-dim)`, texto escuro; `min-height` ≥44 px) · ação secundária (por audience) · `← Voltar` para `/`.
- `{error && <div className="form-error" role="alert">}` mantido. Estado "sessão ativa" (`session.user` presente) mantém o comportamento atual (continuar / trocar conta), reestilizado.

### 26.3 Copy por audience

| Audience | Eyebrow | Título | Texto | Campos | CTA | Ação secundária |
|---|---|---|---|---|---|---|
| `customer` | `ÁREA DO CLIENTE` | `Bem-vindo de volta` | `Acesse sua conta para continuar.` | E-mail, Senha | `Entrar` | `Não tem uma conta? Criar conta` → `/cliente/cadastro` (preservando `?returnUrl=` quando houver) |
| `professional` | `ÁREA DO PROFISSIONAL` | `Bem-vindo de volta` | `Acesse sua conta para continuar.` | E-mail, Senha | `Entrar` | `Ainda não possui acesso? Solicitar cadastro` → `/profissional/cadastro` (fluxo **real** `professional-registration`) |
| `admin` | `ADMINISTRAÇÃO` | `Bem-vindo de volta` | curto ou nenhum, conforme composição | E-mail, Senha | `Entrar` | **nenhuma** (sem "Criar conta", sem "Solicitar acesso", sem social) |

- **Sem** "Esqueci minha senha" em nenhuma audience (não há backend). **Sem** "Lembrar de mim" (não há `isPersistent`).
- Cross-links atuais entre portais (ex.: admin ↔ cliente) podem permanecer como um link discreto único, ou serem removidos — decisão de composição no plano; **não** são obrigatórios.

### 26.4 `claim` do handoff

- `audience === 'customer'`: `useEffect` de mount detecta `handoff` dentro de `safeCustomerReturnUrl(params.get('returnUrl'))` e chama `POST /api/totem/booking-handoffs/claim { handoffToken }` (idempotente, sem bloquear a UI, ignora erro silenciosamente — o `resolve` posterior trata expiração). Preserva a janela de "começou no celular" antes mesmo da autenticação.

### 26.5 Mobile (crítico — o login CUSTOMER roda no celular no fluxo do handoff)

- Card ocupa largura confortável (`width: min(100% - 32px, 420px)`); teclado virtual **não** esconde o CTA (layout com `min-height: 100dvh`, card centrado com scroll natural, CTA dentro do fluxo do card); inputs ≥44 px; **sem** scroll horizontal; fundo continua sutil (`intensity` normal, é leve); retorno ao booking preservado (`returnUrl` + `?handoff=`); performance boa (o fundo é CSS `transform`/`opacity`).

### 26.6 Totem não usa essas telas

Reforço: `/totem/*` **nunca** navega para `/login`, `/cliente/login` ou `/profissional/login`. Fluxo do Totem: Totem → QR → celular → login CUSTOMER **no celular**. Asserção em `frontend-portals.test.ts`.

---

## 27. Threat model — `handoffToken` na URL do QR

- O `handoffToken` aparece na URL aberta pelo QR (`/cliente/agendar?handoff=<token>`) e, URL-encoded, dentro do `returnUrl` durante o round-trip de login/cadastro. **É uma credencial curta e temporária, não uma sessão.**
- **Mitigações:**
  - Alta entropia (32 bytes CSPRNG, base64url).
  - Vida curta (5 min inicial; máx 20 min desde a criação).
  - Uso restrito ao handoff: só `claim` (estende timer, sem PII), `resolve` (retorna `professionalId` a um CUSTOMER autenticado) e `CreateReservation` (conclui, transacional). **Não** concede sessão CUSTOMER, **não** dá acesso geral, **não** lê/escreve dados de outrem.
  - Expira; `Completed`/`Expired` encerram a utilidade (todo consumo revalida `Status == Pending && ExpiresAt > now`).
  - **Não logar explicitamente** o `handoffToken` em application logs (nem o `statusToken`); auditoria só com `Id`/FKs.
  - `statusToken` é **separado** e nunca sai do Totem — comprometer a URL do QR não expõe o canal de polling.
- **Mitigação simples no SPA (sem arquitetura excessiva):** após o `resolve` bem-sucedido, `CustomerBooking` guarda `handoffId`/`professionalId`/`handoffToken` em estado/`history.state` e faz `navigate('/cliente/agendar', { replace: true })` removendo o `?handoff=` da barra de endereço (o token some do histórico visível). O `returnUrl` durante o login é same-origin e efêmero, e `safeCustomerReturnUrl` já o restringe a `/cliente` + `CUSTOMER_QUERY`. Não há tentativa de impedir todo logging possível — a defesa real é entropia + vida curta + escopo restrito.

---

## Auto-revisão da SPEC (revisão 2)

- **Atomicidade handoff/reserva:** `CreateReservation` já usa `BeginTransactionAsync`→`SaveChangesAsync`→`CommitAsync`; a extensão valida + conclui o handoff **na mesma transação**, com concorrência otimista via `handoff.Version`. `/complete` removido. Sem input `reservationId` → ataque de "reservationId arbitrário" eliminado por construção. Idempotência via `409 HANDOFF_ALREADY_USED`.
- **Expiry antes do login:** `claim` **público** marca `StartedAt` + carência de 10 min sem `CustomerPolicy`; teto 20 min; chamado por `Login.tsx` (desvio anônimo) e `CustomerBooking.tsx` (fallback). Sem sessão eterna.
- **Rate limits:** `TotemHandoffRateLimiter` dedicado, orçamento separado; STATUS 50/60 s por `{ip}:{id}` > 30/min de polling real → **nunca 429** no polling normal por toda a vida do handoff; CREATE 10, CANCEL 15, CLAIM 20, RESOLVE 20; config keys + defaults + bump em `Testing` documentados.
- **KPIs exatos:** Admin ganha `Counts.TodayCheckIns` (CountAsync server-side no dia civil); Profissional usa `/api/professional/visits` `from`/`to` + `totalCount` exato e a extensão obrigatória `from`/`to` em `/api/professional/reservations` (gap # incluído no plano). **Nenhum KPI computa contagem de lista paginada truncada.**
- **Login design:** 3 audiences, card discreto no `LumisBackground`, copy exata, **sem** social / forgot / remember-me (confirmado: `LoginRequest(Email,Password)`, sem endpoint de reset); mobile detalhado; `claim` no mount para customer; Totem nunca navega para login.
- **Mobile handoff/login:** card `min(100% - 32px, 420px)`, CTA no fluxo, inputs ≥44 px, sem scroll horizontal, `returnUrl`/`handoff` preservados, fundo leve (CSS transform/opacity).
- **Refresh do `/totem/handoff`:** `statusToken` só em navigation state; F5 sem state → `Navigate` para `/totem`, sem recuperar credencial, estado limpo. Nada em storage.
- **Threat model dos tokens:** §27 — `handoffToken` = credencial curta/temporária/escopo restrito; `statusToken` separado e nunca sai do Totem; nenhum dos dois é logado; mitigação SPA simples (drop do `?handoff=` via `replace`) sem over-engineering.
- **Carousel/compositing:** `165f2f1` preservado integralmente; a "settle" do BlurFade (`filter: none`) remove a causa raiz original mas **não** substitui o pointer-drag por scroll nativo; `LumisBackground` sem `filter`, `pointer-events: none`, reduced-motion.
- **Nenhuma UI fake:** sem "lembrar de mim", sem "esqueci a senha", sem notificações do cliente, sem chat/documentos do profissional, sem nomes/números hardcoded; blocos sem dado de domínio são omitidos, não fabricados.
- **Migration:** só a tabela `TotemBookingHandoffs` (aditiva). `TodayCheckIns` e `from`/`to` são DTO+LINQ, zero schema. Documentada, não criada.
- **Rotas reais:** confirmadas; nova rota pública `/totem/handoff`; nenhum item de menu para rota inexistente; QR aponta direto para `/cliente/agendar?handoff=` (sem rota `/continuar` extra).

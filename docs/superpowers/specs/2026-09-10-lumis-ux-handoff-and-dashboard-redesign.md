# LUMIS — Evolução visual/UX: identidade, fundo animado, Totem check-in, handoff de agendamento e redesenho dos 3 dashboards

> Rodada **arquitetural**. As decisões de produto/design já foram aprovadas pelo cliente. Esta SPEC apenas as reconcilia com o código real do branch, fecha ambiguidades técnicas e define contratos antes de qualquer implementação. **Nada foi implementado nesta etapa.** Após aprovação desta SPEC: `superpowers:writing-plans` → plano TDD → nova parada antes de implementar.

- **Branch:** `codex/reception-backend` (worktree `.worktrees/reception-backend`)
- **HEAD local:** `165f2f1cf34aff8e1c82819336ddeb20119ae377` (`fix(totem): touch swipe now moves the professional carousel`)
- **`main`:** `fb40fde` — intocada
- **Working tree:** limpa. HEAD já publicado em `origin/codex/reception-backend`.
- **Migration `20260910085909_CheckInManualCode`:** presente no branch e — conforme execução do cliente — já aplicada ao Supabase staging (`xpblbvrmljtvyltvvnpd`) com o segredo `CheckIn__ManualCodeHmacKey` provisionado no Railway staging. Esta rodada **não** mexe nessa migration.
- **Fix de swipe do carrossel (`165f2f1`):** já integrado em HEAD. Esta SPEC **não** o reabre nem o duplica; apenas adiciona um reforço de compositing (seção 4) que o torna redundante.

---

## 1. Estado atual investigado (contratos a preservar)

### 1.1 Autenticação e sessão

- Sessão **cookie HttpOnly** no servidor. `SessionProvider` (`src/auth/SessionProvider.tsx`) fala com `/api/auth/{csrf,login,session,logout,change-password}`. `apiClient` (`src/api/client.ts`) usa `credentials: 'same-origin'`, guarda o CSRF só em memória, dispara `lumis:unauthorized` em 401, revalida em `focus`/`pageshow` e via `BroadcastChannel('lumis-session')`. **O cliente nunca vê um token.**
- `SessionProvider` embrulha o app inteiro (`main.tsx`: `<BrowserRouter><SessionProvider><App/></SessionProvider></BrowserRouter>`).
- `ProtectedRoute` (`src/components/ProtectedRoute.tsx`): latch `validatedOnce` que não re-desmonta; ramo anônimo redireciona rotas `/cliente*` para `/cliente/login?returnUrl=<encodeURIComponent(pathname+search)>` e staff para `/login` sem `returnUrl`. O ternário literal `location.pathname.startsWith('/cliente') ? '/cliente/login' : '/login'` é exigido por testes de portais — **mantido**.
- `safeCustomerReturnUrl` (`src/auth/returnUrl.ts`): allowlist estrita — só caminhos `/cliente`; `CUSTOMER_QUERY = /^[A-Za-z0-9_-]+=[A-Za-z0-9_-]*(?:&[A-Za-z0-9_-]+=[A-Za-z0-9_-]*)*$/`. O alfabeto base64url é `A-Za-z0-9_-`, logo `handoff=<token base64url>` **já passa sem alteração** neste helper e sobrevive ao round-trip de login/cadastro no celular.

### 1.2 Totem hoje

- Rotas **públicas** (fora de `ProtectedRoute`): `/totem` (`TotemEntry`), `/totem/check-in` (`TotemCheckIn`), `/totem/profissionais` (`TotemProfessionals`).
- `TotemEntry`: `LightRays` + `BlurFade` + dois `MagicCard as="button"` → `Tenho código` navega para `/totem/check-in`; `Não tenho código` navega para `/totem/profissionais`. Sem campo de QR/código aqui.
- `TotemProfessionals`: carrega `totemApi.professionals()` (anônimo), 4 fases (`loading`/`ready`/`empty`/`error`), carrossel `TotemProfessionalCarousel`, e **hoje** `Continuar` faz `navigate('/cliente/agendar?professionalId=' + active.id)` → cai em `/cliente/login` se anônimo. **É exatamente esse passo que o handoff substitui.**
- `TotemProfessionalCarousel`: um único caminho de pointer-drag (touch+pen+mouse) com `setPointerCapture` + snap por deslocamento líquido; `.totem-carousel-viewport { touch-action: none }`. `role="listbox"`, opções `role="option"`, teclado Arrow/Home/End. **Não alterar.**
- `TotemCheckIn`: `useQrScanner` (QR forte via worker `qr-scanner`), aba manual com **um** `<input maxLength={6} inputMode="numeric" autoComplete="one-time-code">` + `onlyDigits6`/`isComplete6`, `resolveCheckIn` → preview → `confirmCheckIn` → Visit `WAITING` → auto-retorno para `/totem` em 12 s. Erros genéricos (`Não foi possível validar este QR Code.` / `...este código.`). Regras HMAC, resolve/confirm, `allowUsed`, rate limiter — **todas preservadas**.
- Magic UI local em `src/features/totem/magic/`: `LightRays` (div, `pointer-events:none` inline + CSS `.totem-magic-rays` animando `background`/`transform`), `BlurFade` (`.totem-magic-fade` → `.is-in`, mantém **`filter: blur(0)` + `will-change: filter` permanentes** após a entrada — ver seção 4), `MagicCard`, `BorderBeam`, `ProgressiveBlur` (`backdrop-filter`), `RippleButton`, `usePrefersReducedMotion`.

### 1.3 Booking do CUSTOMER hoje (lado celular do handoff)

- `/cliente/agendar` (`CustomerBooking.tsx`, rota `CustomerPolicy`): lê `?professionalId=` de `useSearchParams`, pré-seleciona se o id existe em `customerApi.professionals()`, escolhe data/duração, `customerApi.availability(...)`, `customerApi.createReservation({ professionalId, startAt, endAt })` → `navigate('/cliente/agendamentos/:id')`.
- `POST /api/customer/reservations` (`CustomerSchedulingEndpoints.CreateReservation`): transação + `ILeaseResourceLock` + `IAppointmentAvailabilityService.FindAvailableRoomAsync` + `Reservation.CreateApproved(...)` + `AuditEntry("RESERVATION_CREATED")`. **Lógica intocada nesta rodada.**
- Cadastro anônimo do cliente: `POST /api/customer/register` (`AllowAnonymous` + `AntiforgeryFilter`). Login: `/api/auth/login`. Tudo isso acontece **no celular**.

### 1.4 Dashboards hoje

| Portal | Shell atual | Index atual | Tema atual |
|---|---|---|---|
| Profissional `/profissional` | `ProfessionalShell` (sidebar já existe, `src/pages/professional/ProfessionalHome.tsx`) | `ProfessionalDashboard` (métricas reais: reservas hoje, aguardando, em atendimento; painel "Próximo compromisso" + "Atendimentos atuais") | **claro** (`:root` global) |
| Cliente `/cliente` | `CustomerShell` (**topbar horizontal**, não sidebar) | `CustomerHome` ("Olá, {firstName}" + 2 CTAs + card "cadastro pronto") | **claro** |
| Admin `/admin` | `AdminLayout` (sidebar `LUMIS` já existe, rotas reais) | **`ModuleUnavailable title="Visão geral"` — não existe dashboard** | **claro** |
| Recepção `/recepcao` e `/admin/recepcao` | — | `ReceptionMonitor` (usa `receptionApi.overview()` + `receptionApi.visits()`, refresh 10 s) | **claro** |

- **Rotas CUSTOMER que existem:** `/cliente` (index `CustomerHome`), `/cliente/agendamentos` (`CustomerReservations`), `/cliente/agendar` (`CustomerBooking`), `/cliente/agendamentos/:id` (`CustomerReservationDetail`). **NÃO existem** `/cliente/reservas`, `/cliente/check-in`, `/cliente/profissionais`, `/cliente/perfil`.
- **Rotas PROFISSIONAL:** `/profissional` (index), `/profissional/agenda` (`ProfessionalAgenda` real), `/profissional/disponibilidade` (`ProfessionalAvailability` real). `reservas`, `atendimentos`, `locacoes`, `financeiro`, `perfil` são `ProfessionalPlaceholder`.
- **Rotas ADMIN:** `/admin` (index vazio), `salas`, `profissionais`, `locacoes`, `reservas`, `visitas`, `recepcao`, `solicitacoes-profissionais`, `configuracoes` — todas reais no `AdminLayout`.

### 1.5 Cliente HTTP frontend (`src/api/modules.ts`)

- Já existe: `totemApi`, `customerApi` (inclui `issueCheckInToken` → `{ token, manualCode, expiresAt }`), `professionalReservationsApi`, `professionalVisitsApi`, `professionalAvailabilityApi`, `professionalLeasesApi`, `receptionApi` (`overview`, `visits`, `startVisit`, `endVisit`).
- **Não existe** cliente para `GET /api/admin/dashboard` nem para `GET /api/reception/professionals` — os endpoints existem no backend, o wrapper TS falta.
- `qrcode@1.5.4` + `@types/qrcode` já são dependências; `CustomerReservationDetail.tsx` já renderiza `QRCode.toDataURL(...)` como `<img src="data:image/png...">` sob a CSP atual sem problema.

### 1.6 CSP atual (`recepcaototem/Program.cs`, commit `c89a5c3`)

```
default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self' data:;
img-src 'self' data: blob:; connect-src 'self'; media-src 'self' blob:; worker-src 'self' blob:;
object-src 'none'; base-uri 'self'; frame-ancestors 'none'
```

`img-src ... data:` cobre o QR gerado por `qrcode`; `worker-src ... blob:` cobre o `qr-scanner`. **Nenhuma alteração de CSP é necessária nesta rodada.**

### 1.7 Domínio / persistência

- `ApplicationDbContext` DbSets relevantes: `Reservations`, `Visits`, `Customers`, `Professionals`, `Rooms`, `CheckInTokens`, `ProfessionalPresenceTokens`, `RescheduleTokens`, `AuditEntries`.
- Padrão de "token efêmero hasheado" já existe 3×: `CheckInToken`, `RescheduleToken`, `ProfessionalPresenceToken` — todos com `Id`, FK, `TokenHash byte[32]`, `IssuedAt/ExpiresAt`, `RevokedAt?`, `UsedAt?`, `Version` (xmin), config EF `bytea` + `timestamp with time zone` + índices únicos `UX_*_TokenHash`. **O `TotemBookingHandoff` segue este mesmo padrão.**
- Migrations PostgreSQL: 13, a última = `20260910085909_CheckInManualCode`. `DesignTimeDbContextFactory` permite `dotnet ef migrations add` offline.

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

- Bloco único em `:root` no topo de `src/styles.css`, logo após o `:root` atual. O `:root` atual permanece (é o tema **claro** ainda usado por telas não migradas), mas ganha os `--lumis-*`.
- **Nesta rodada** os aliases por-página (`--tk-*` em `.totem-kiosk`, `--tc-*` no carrossel, `--tp-*` em `/totem/profissionais`, `--te-*` em `TotemEntry`) **passam a apontar para os `--lumis-*`** no seu ponto de declaração atual (ex.: `--tk-bg: var(--lumis-bg)`), sem mudança visual. Componentes novos consomem `--lumis-*` direto.
- **Não** há refatoração destrutiva do CSS. A remoção dos aliases legados fica para rodadas futuras.

### 2.3 Logo

- Asset oficial único: `/lumis-logo-transparent.png` (já usado em Home, Totem, AdminLayout, ProfessionalShell). `/lumis-logo-dark.png` é usado só no `CustomerShell` claro — ao migrar o Customer para o DNA escuro, passa a usar `/lumis-logo-transparent.png` como os demais. Sem recriar o logo com fontes diferentes por portal.

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

`src/features/lumis/LumisPageShell.tsx` (fino, opcional por tela):

```tsx
export function LumisPageShell({ intensity, className, children }) {
  return (
    <div className={`lumis-shell ${className ?? ''}`.trim()}>
      <LumisBackground intensity={intensity} />
      <div className="lumis-shell-content">{children}</div>
    </div>
  )
}
```

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
.lumis-bg-ray:nth-child(3) { top: -30%; width: 52vw; animation-duration: 19s; animation-delay: -15s; opacity: 0; }
.lumis-bg[data-intensity="muted"] .lumis-bg-ray { opacity: 0; }
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

- Raios **nascem fora do viewport → cruzam devagar na diagonal → somem → o próximo entra** (delays negativos escalonados dão continuidade). Ciclo 19–34 s, largura/opacidade/velocidade variadas. Nunca rápido, nunca "protetor de tela".
- **Sem `filter` animado, sem `box-shadow` gigante, sem propriedade de layout.** `will-change` só nas propriedades animadas.
- `pointer-events: none` no container **e** inline (garantia mesmo sem CSS, ex.: jsdom).

### 3.3 Onde aplicar

- Home `/`; Totem `/totem`, `/totem/profissionais`, `/totem/check-in`, `/totem/handoff`.
- Portais CUSTOMER (`/cliente`, `/cliente/agendar`, `/cliente/agendamentos`, `/cliente/agendamentos/:id`), PROFISSIONAL (`/profissional/*`), ADMIN (`/admin/*`) e Recepção — via shells, com `intensity="muted"` em telas densas (tabelas admin, agenda cheia).
- **Não** aplicar com intensidade cheia atrás de: tabelas densas, modais, câmera do QR (`.totem-scan-frame` continua sólida `#000`), inputs, dropdowns. O fundo é sempre a camada `z-index: 0`; cards/superfícies ficam sólidos o bastante para legibilidade.

---

## 4. Performance / compositing (auditoria explícita)

### 4.1 Achado

`.totem-magic-fade` (BlurFade) hoje: `filter: blur(8px)` na entrada e, **após a entrada**, `.is-in` deixa `filter: blur(0)` + `will-change: opacity, transform, filter` **permanentes**. Um `filter` não-`none` num ancestral desabilita o scroll nativo por toque de um scroller aninhado no WebKit/Blink mobile — foi a causa raiz do bug de swipe corrigido em `165f2f1` (contornado via pointer-drag JS + `touch-action: none`, não removido).

### 4.2 Correção desta rodada

- Ao terminar a transição de entrada do BlurFade, aplicar `filter: none; will-change: auto`. Implementação: `onTransitionEnd` (ou timeout de ~550 ms) adiciona `.is-settled`:

```css
.totem-magic-fade.is-in.is-settled { filter: none; will-change: auto; }
```

- Resultado: o carrossel continua funcionando (o pointer-drag JS permanece) **e** nenhum `filter` persistente sobra na árvore de nenhum elemento com scroll/toque.
- **Regra documentada:** nenhum ancestral de elemento com scroll/toque/carrossel/câmera pode carregar `filter` não-`none` persistente nem `will-change: filter`. `LumisBackground` nunca usa `filter` animado. `ProgressiveBlur` usa `backdrop-filter` num elemento folha `pointer-events: none` fora da árvore de scroll — mantido.
- Teste (vitest, leitura de `styles.css`): assegura que `.totem-magic-fade.is-in.is-settled` zera `filter`/`will-change`, e que `.lumis-bg`/`.lumis-shell` não declaram `filter`.

---

## 5. Totem `/totem/check-in` — layout definitivo

### 5.1 Causa raiz da regressão

`.totem-kiosk { display: grid; grid-template-columns: minmax(300px, 400px) 1fr }` + `.totem-aside` quase vazia (só logo + relógio) + `.totem-stage` centralizando uma coluna `max-width: 520px` dentro do `1fr` gigante ⇒ conteúdo colado à esquerda, vazio enorme à direita, "gutter" de logo/relógio superdimensionado, câmera minúscula (`max-height: 44vh`), CTA pequeno.

### 5.2 Novo layout

- **Elimina** o grid de 2 colunas e a `.totem-aside`.
- **Topbar discreta** (`.totem-kiosk-topbar`, `display: flex; justify-content: space-between; align-items: center; padding: clamp(14px,2.5vw,24px) clamp(16px,4vw,40px)`):
  - esquerda: `← Voltar` (`.totem-back`, ≥44 px, foco visível, cor `--lumis-muted`→`--lumis-text` no hover)
  - centro: wordmark `LUMIS` (`<img alt="LUMIS">`, ~120–132 px)
  - direita: relógio + data em tamanho normal (`KioskClock`, **sem** `clamp(...44px)`)
- **Stage central** (`.totem-kiosk-stage`, `flex: 1; display: flex; align-items: center; justify-content: center; padding: clamp(24px,5vh,64px) clamp(16px,5vw,40px); overflow-y: auto`) com coluna `max-width: 560px; width: 100%; margin-inline: auto; text-align: center`:
  - eyebrow `CHECK-IN`
  - `Confirme sua chegada`
  - `Escolha como deseja identificar seu agendamento`
  - controle de 2 abas `[Escanear QR] [Digitar código]` (`role="tablist"`, mantém os `aria-label` atuais não-colidentes)
  - **QR:** área de câmera **grande** — `width: min(70vw, 420px); aspect-ratio: 1/1` — + `[Ativar câmera]` (CTA `min-height: 60px`, largura confortável)
  - **Código:** `Digite o código de 6 dígitos` + **6 caixas** + `[Confirmar]`
- `.totem-kiosk { position: relative; overflow-x: hidden; min-height: 100dvh; display: flex; flex-direction: column; background: var(--lumis-bg) }` + `<LumisBackground />` como primeira filha.

### 5.3 As 6 caixas

- Visual de 6 células, **um** modelo de dados controlado (`token: string` de até 6 dígitos). Implementação aceitável: um `<input>` invisível/overlay dirigindo 6 células renderizadas, **ou** 6 `<input maxLength={1}>` com um controlador que concatena. Requisitos preservados: `onlyDigits6`/`isComplete6`, `inputMode="numeric"`, `autoComplete="one-time-code"`, `maxLength` efetivo 6, **zero à esquerda preservado**, colar (`paste`) preenchendo as 6, `submitManual` só dispara com `isComplete6`, botão desabilitado enquanto `!isComplete6 || loading`.
- `<label>`/`aria-label` descrevendo "código de 6 dígitos" — manter compatível com os seletores dos testes existentes (`getByLabelText(/código de 6 dígitos/i)` deve casar **um** elemento).

### 5.4 Responsivo

- 1920×1080, 1366×768, tablet landscape/portrait, mobile: **sempre coluna única centralizada**. Câmera escala por `min()`. Nunca: conteúdo colado à borda, scroll horizontal, card cortado, câmera minúscula. Remove o `@media (max-width: 900px)` que virava a `.totem-aside` em linha (não há mais aside).

### 5.5 Regras preservadas (inalteradas)

`useQrScanner`, QR forte (worker), código manual de 6 dígitos, hashing HMAC, `resolveCheckIn`/`confirmCheckIn`, `allowUsed` (resolve=false, confirm=true), preview, criação de `Visit` `WAITING`, auto-retorno para `/totem` em 12 s, erros genéricos, CSP. Mudança é **de layout/componente em `TotemCheckIn.tsx` + `styles.css`**, não de fluxo.

---

## 6. Carrossel — toque (já resolvido)

`165f2f1` já integrado em HEAD: um único caminho de pointer-drag para touch/pen/mouse com `setPointerCapture` + snap por deslocamento líquido; `.totem-carousel-viewport { touch-action: none }`; testes de swipe touch em `TotemProfessionalCarousel.test.tsx`. **Esta rodada não altera o componente nem os testes.** O único item relacionado é a "settle" do BlurFade (seção 4), que remove a causa raiz original e torna o contorno redundante — sem tocar no carrossel.

---

## 7. Totem booking handoff — arquitetura

### 7.1 Princípio

**O cliente não faz login nem cadastro no Totem.** Nenhuma conta CUSTOMER é autenticada no quiosque. O Totem entrega a continuação para o **celular do visitante** via QR.

### 7.2 Fluxo

```
/totem
  ├─ "Tenho código"      → /totem/check-in            (inalterado)
  └─ "Não tenho código"  → /totem/profissionais
                              → seleciona profissional no carrossel
                              → "Continuar"
                                 → POST /api/totem/booking-handoffs { professionalId }
                                 → navigate('/totem/handoff', { state: { handoffId, statusToken, professionalName, profession, expiresAt } })

/totem/handoff  (rota pública, TotemHandoff)
  - "Continue no seu celular"
  - nome + profissão do profissional
  - QR (imagem) codificando  <ORIGIN>/cliente/agendar?handoff=<handoffToken>
  - "Escaneie para continuar seu agendamento"
  - "Aguardando conclusão..." + contagem regressiva (expiresAt - now, mm:ss)
  - "← Escolher outro profissional"
  - polling: POST /api/totem/booking-handoffs/{handoffId}/status { statusToken }  a cada 2 s

Celular  (scan abre o navegador do visitante)
  /cliente/agendar?handoff=<handoffToken>
  - ProtectedRoute: se anônimo → /cliente/login?returnUrl=%2Fcliente%2Fagendar%3Fhandoff%3D<token>
      (CUSTOMER_QUERY já aceita `handoff=<base64url>`)
  - o celular pode: já estar logado / logar / criar conta  — tudo no celular
  - CustomerBooking monta com ?handoff:
      POST /api/customer/booking-handoffs/resolve { handoffToken }
        → { handoffId, professionalId, professionalName, profession, expiresAt }   (marca StartedAt)
      professionalId pré-selecionado; usuário escolhe data/horário
  - createReservation (fluxo atual, intocado) → reserva criada
  - POST /api/customer/booking-handoffs/{handoffId}/complete { reservationId }
      → handoff.Status = COMPLETED, ReservationId, CompletedAt

Totem (próximo poll)
  - status = COMPLETED → troca para tela ✓
      "Agendamento concluído!", nome do profissional, data, hora, "Tudo certo por aqui.", "Retornando ao início..."
  - após ~6 s: navigate('/totem') + limpa todo o estado efêmero
```

### 7.3 Dois tokens (nunca o mesmo)

| Token | Onde vive | Onde vai | Armazenamento |
|---|---|---|---|
| `handoffToken` | QR + URL do celular | `/cliente/agendar?handoff=` | `HandoffTokenHash = SHA256(bytes)` |
| `statusToken` | **só** no estado React do Totem (nunca localStorage/cookie/URL) | corpo do POST de polling/cancel | `StatusTokenHash = SHA256(bytes)` |

- Ambos: `RandomNumberGenerator.GetBytes(32)`, base64url, alta entropia, hash em DB, **texto puro só na resposta inicial de criação**, nunca logado, nunca auditado em claro, nunca expõe PII.
- Lookup por hash: `where x.HandoffTokenHash == SHA256(decoded)` / `where x.StatusTokenHash == SHA256(decoded)`. Falha de decode / tamanho ≠ 32 / não encontrado / token errado → **mesmo** erro genérico `INVALID_HANDOFF` (sem distinguir "não existe" de "token inválido").

### 7.4 Cancelar / voltar

- "← Escolher outro profissional": `POST /api/totem/booking-handoffs/{id}/cancel { statusToken }` (revoga o handoff pendente → `EXPIRED`), limpa estado local, volta ao carrossel. Idempotente (já `COMPLETED`/`EXPIRED` → 200 no-op).
- Se expirou: tela "Este QR Code expirou." + `[Gerar novo QR]` (cria handoff novo para o mesmo profissional) + `[Escolher outro profissional]`.

### 7.5 Garantia de "Totem sem sessão CUSTOMER"

- Todos os endpoints do Totem (`/api/totem/*`, incluindo os de handoff) são `AllowAnonymous`. O Totem **nunca** chama endpoint `CustomerPolicy`, **nunca** chama `/api/auth/login`, **nunca** escreve em `localStorage`/`sessionStorage`/cookie.
- As páginas `/totem/*` **não** leem `useSession()` para decisões de fluxo, e "Continuar" **nunca** navega para uma rota `ProtectedRoute`. `/totem/handoff` é rota pública.
- O Totem não chama `session.logout()` automaticamente (poderia derrubar sessão de staff num dispositivo compartilhado). Recomendação **operacional**: perfil de navegador dedicado ao quiosque (ver seção 24).
- Teste: um ciclo completo de handoff no Totem não seta cookie de auth e não toca `localStorage`.

---

## 8. Entidade / modelo do handoff

### 8.1 `TotemBookingHandoff` (Domain — `src/GestaoPredio.Domain/Customers/TotemBookingHandoff.cs`, `sealed class`)

| Campo | Tipo | Notas |
|---|---|---|
| `Id` | `Guid` | PK |
| `ProfessionalId` | `Guid` | FK → `Professionals`, `OnDelete(NoAction)` |
| `HandoffTokenHash` | `byte[]` (32) | índice único `UX_TotemBookingHandoffs_HandoffTokenHash` |
| `StatusTokenHash` | `byte[]` (32) | índice único `UX_TotemBookingHandoffs_StatusTokenHash` |
| `Status` | enum `TotemBookingHandoffStatus` (`smallint`) | `Pending=0`, `Completed=1`, `Expired=2` |
| `CreatedAt` | `DateTimeOffset` | `timestamp with time zone`, normalizado `TimestampNormalizer.ToUtcMicroseconds` |
| `ExpiresAt` | `DateTimeOffset` | idem |
| `StartedAt` | `DateTimeOffset?` | setado no 1º `resolve` bem-sucedido do celular |
| `CompletedAt` | `DateTimeOffset?` | setado na conclusão |
| `ReservationId` | `Guid?` | FK → `Reservations`, `OnDelete(NoAction)`, setado na conclusão |
| `Version` | `uint` | xmin rowversion |

### 8.2 Fábrica / métodos

- `static Create(Guid professionalId, byte[] handoffTokenHash, byte[] statusTokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt)` — guarda: `professionalId != Guid.Empty`; cada hash com 32 bytes; `expiresAt > createdAt`.
- `MarkStarted(DateTimeOffset at, TimeSpan graceWindow, DateTimeOffset hardCeiling)` — só de `Pending`; se `StartedAt == null` seta `StartedAt = at` e `ExpiresAt = Min(hardCeiling, Max(ExpiresAt, at + graceWindow))`. Chamadas subsequentes: no-op (sem re-extensão).
- `Complete(Guid reservationId, DateTimeOffset at)` — só de `Pending`; `Status = Completed`, `ReservationId`, `CompletedAt = at`.
- `MarkExpired(DateTimeOffset at)` — só de `Pending`; `Status = Expired`. (Cancelar reusa este estado — enum mínimo, sem `Cancelled`.)
- `bool IsUsable(DateTimeOffset now) => Status == Pending && ExpiresAt > now`.

### 8.3 EF config (`TotemBookingHandoffConfiguration`)

`ToTable("TotemBookingHandoffs")`, `HasKey(Id)`, `TokenHash`s `HasColumnType("bytea").IsRequired()`, timestamps `timestamp with time zone`, `Version.IsRowVersion()`, índices únicos nos dois hashes, `HasIndex(ProfessionalId)`, `HasIndex(ReservationId)`, `HasIndex(x => new { x.Status, x.ExpiresAt })` (para limpeza futura). `HasOne<Professional>().WithMany().HasForeignKey(ProfessionalId).OnDelete(NoAction)`; idem `Reservation` (opcional).

### 8.4 Constantes de janela

`HandoffWindows` (no endpoint, como `AUTO_RESET_MS` já é feito): `Initial = 5 min`, `Grace = 10 min`, `HardCeiling = 20 min`. Não vira config/segredo nesta rodada.

---

## 9. Endpoints

Todos os do Totem: `AllowAnonymous`, **sem** `AntiforgeryFilter` (coerente com `/api/totem/check-in/*` e `/api/totem/presence/confirm`), **com** rate limiting. Resposta de erro sempre `ApiError(code, message)` genérica.

### 9.1 `POST /api/totem/booking-handoffs`  (Totem cria)

- Body: `{ professionalId: Guid }`. Rate limit por IP (partição nova `"totem-handoff-create"`).
- Valida profissional existente e `IsActive` → senão `404` genérico.
- Cria entidade: `CreatedAt = now`, `ExpiresAt = now + 5 min`, dois tokens CSPRNG.
- Audita `TOTEM_HANDOFF_CREATED` (só `Id`/`ProfessionalId`/`CorrelationId` — sem token).
- `201` → `{ id, handoffToken, statusToken, expiresAt, professionalName, profession }`. **Única** ocorrência de token em claro. `professionalName`/`profession` já são públicos em `/totem/profissionais` (não é nova exposição).

### 9.2 `POST /api/totem/booking-handoffs/{id}/status`  (Totem faz polling)

> **Desvio deliberado do "GET" conceitual:** o `statusToken` é uma credencial bearer; regra de privacidade proíbe credencial em query string/URL (logs, histórico, proxies). Vira `POST` com `{ statusToken }` no corpo. O produto (polling ~2 s, autorizado só pelo `statusToken`) fica preservado.

- Body: `{ statusToken: string }`. Rate limit por partição `id` com teto generoso para ~2 s durante 5–20 min (limite `300` / janela `900 s`; acima disso `429` genérico).
- `statusToken` inválido/errado → `INVALID_HANDOFF` genérico.
- Expiração preguiçosa: se `Pending && ExpiresAt <= now` → `MarkExpired(now)` + `SaveChanges`.
- Respostas:
  - `Pending` → `{ status: "PENDING", expiresAt }`
  - `Completed` → `{ status: "COMPLETED", professionalName, startAt, roomName }` — **sem** nome/telefone/email/CPF do cliente, `customerId`, `reservationId`, token ou detalhe interno. `roomName` é local físico do prédio (não PII) e é útil na tela ✓.
  - `Expired` → `{ status: "EXPIRED" }`

### 9.3 `POST /api/totem/booking-handoffs/{id}/cancel`  (Totem abandona)

- Body: `{ statusToken }`. `Pending` → `MarkExpired(now)` + audita `TOTEM_HANDOFF_CANCELLED`. Já terminal → `200` no-op. Token errado → `INVALID_HANDOFF`.

### 9.4 `POST /api/customer/booking-handoffs/resolve`  (celular resolve)

- `RequireAuthorization(CustomerPolicy)` + `AntiforgeryFilter` (é o celular, autenticado — não o quiosque).
- Body: `{ handoffToken }`. `!IsUsable` → `410` `HANDOFF_EXPIRED`.
- `MarkStarted(now, Grace=10min, HardCeiling = CreatedAt + 20min)` + `SaveChanges`.
- `200` → `{ handoffId, professionalId, professionalName, profession, expiresAt }`.

### 9.5 `POST /api/customer/booking-handoffs/{id}/complete`  (celular conclui)

- `RequireAuthorization(CustomerPolicy)` + `AntiforgeryFilter`.
- Body: `{ reservationId: Guid }`.
- Valida: handoff existe; `reservation` existe, pertence a este customer (`GetCustomer` + `CustomerId`), `Status == Approved`, `ProfessionalId == handoff.ProfessionalId`.
- Se `handoff.Status == Pending` → `handoff.Complete(reservationId, now)` + audita `TOTEM_HANDOFF_COMPLETED`.
- Se `handoff` já `Expired` → **ainda retorna `200`** (a reserva foi criada; o Totem apenas mostrará `EXPIRED` e o cliente segue com a reserva no celular). Documentado, não é erro.
- `200` → `{ status }` (sem PII).

> **Por que `complete` separado e não `handoffId` dentro de `POST /api/customer/reservations`:** preservar intacta a transação/auditoria de `CreateReservation` ("não é refatoração"). Custo: janela de ≤1 ciclo de poll entre "reserva criada" e "Totem vê COMPLETED" — aceitável.

### 9.6 Rate limiter

Estender `CustomerPublicRateLimiter` (`recepcaototem/Features/Customers/CustomerPublicRateLimiter.cs`) com partições nomeadas para 9.1/9.2/9.3, **ou** adicionar `TotemHandoffRateLimiter` análogo. `ModulesApiFactory` sobe os limites em `Testing` (como já faz para check-in). Números finais definidos no plano.

### 9.7 Rotas frontend novas / alteradas

- **Nova rota pública** `/totem/handoff` → `TotemHandoff` (`src/pages/TotemHandoff.tsx`), fora de `ProtectedRoute`. Carregada só via `navigate(..., { state })`; sem `state` (refresh) → `<Navigate to="/totem/profissionais" replace />` (o token não vai na URL).
- `/cliente/agendar` inalterada como rota; `CustomerBooking.tsx` ganha tratamento de `?handoff=`.
- `frontend-portals.test.ts` atualizado: "Continuar" do Totem não navega mais para `/cliente/login`; nova rota `/totem/handoff` anônima.

---

## 10. Segurança / tokens (resumo normativo)

1. `handoffToken` e `statusToken`: CSPRNG 32 bytes, base64url, **distintos**, hash SHA-256 em DB, texto puro só em 9.1.
2. Nenhum token em URL/query string. `statusToken` só em corpo de POST; `handoffToken` só dentro do QR e do parâmetro do celular (base64url, sem PII).
3. Erros genéricos e uniformes (`INVALID_HANDOFF`) — sem oráculo de existência.
4. Auditoria registra só `Id`/`ProfessionalId`/`ReservationId`/`CorrelationId` — nunca token, nunca PII do cliente.
5. Resposta `COMPLETED` ao Totem: apenas `professionalName`, `startAt`, `roomName`. A tela do Totem é pública.
6. Rate limiting em todos os 3 endpoints anônimos.
7. Expiração: inicial 5 min; **uma** extensão de carência para 10 min a partir do `resolve` legítimo (`StartedAt`), com teto absoluto de 20 min desde `CreatedAt`. Sem sessão eterna. Enforcement preguiçoso em todo `status`/`resolve`/`complete`.
8. `/totem/*` nunca autentica CUSTOMER; o quiosque não guarda cookie/access/refresh/senha/email/sessão/localStorage de cliente.

---

## 11. Polling

- `TotemHandoff`: `setInterval(2000)` → `POST .../{id}/status { statusToken }`. Limpa no unmount e em status terminal.
- `PENDING` → continua; atualiza a contagem a partir do `expiresAt` retornado (reflete a carência automaticamente).
- `COMPLETED` → para o polling; troca para ✓; `setTimeout(6000)` → `navigate('/totem')` + `reset()` (limpa `handoffId`, `statusToken`, dados do profissional, contagem, fase).
- `EXPIRED` → para o polling; mostra "Este QR Code expirou." + `[Gerar novo QR]` + `[Escolher outro profissional]`.
- Erro de rede num poll: não quebra; após **5** falhas consecutivas mostra aviso suave com `[Tentar novamente]`; senão segue tentando.
- `prefers-reduced-motion` não afeta o polling (só a animação do spinner/contagem).
- **Sem WebSocket** (fora de escopo).

---

## 12. Conclusão (tela ✓ do Totem)

- Disparada **somente** quando `status == COMPLETED` (reserva realmente criada no celular).
- Mostra: "Agendamento concluído!", nome do profissional, data, hora (`startAt`), "Tudo certo por aqui.", "Retornando ao início...".
- Após ~6 s: `navigate('/totem')` + limpeza total do estado efêmero (seção 14).

---

## 13. Expiração (regra única, documentada)

| Momento | `ExpiresAt` |
|---|---|
| Criação (`POST /api/totem/booking-handoffs`) | `CreatedAt + 5 min` |
| 1º `resolve` do celular (`MarkStarted`, `StartedAt == null`) | `Min(CreatedAt + 20 min, Max(ExpiresAt, now + 10 min))` |
| `resolve` subsequente | inalterado (no-op) |
| Sempre (`status`/`resolve`/`complete`) | se `Pending && ExpiresAt <= now` → `Expired` |

- O Totem exibe `ExpiresAt - now` (mm:ss), relendo `expiresAt` de cada poll.
- Sem job em background. Sem regra confusa: "5 min para começar; se você começou, 10 min para terminar; nunca mais que 20 min no total".

---

## 14. Limpeza do quiosque

- Todo o estado do handoff vive em estado React da subárvore `/totem/*`. `navigate('/totem')` (auto após ✓, "Escolher outro profissional", "Voltar") desmonta → estado some. `reset()` explícito zera `handoffId`, `statusToken`, profissional, contagem, fase.
- Nada é escrito em `localStorage`/`sessionStorage`/cookie pelo fluxo do quiosque.
- Teste: após um ciclo completo (criar → polling → COMPLETED → auto-retorno), `localStorage` intacto e sem cookie de auth.

---

## 15. Continuação no celular (CUSTOMER)

- Scan do QR abre `<ORIGIN>/cliente/agendar?handoff=<handoffToken>` no navegador do visitante.
- `ProtectedRoute` anônimo → `/cliente/login?returnUrl=%2Fcliente%2Fagendar%3Fhandoff%3D<token>` (helper `safeCustomerReturnUrl` já aceita `handoff=<base64url>` — nenhuma mudança em `returnUrl.ts`).
- Celular: já logado / loga / cria conta (`/cliente/cadastro` → `POST /api/customer/register`) — tudo no celular.
- `CustomerBooking.tsx` no mount, se `?handoff` presente:
  1. `POST /api/customer/booking-handoffs/resolve { handoffToken }` → guarda `handoffId`, seta `professionalId` do resultado (ignora `?professionalId=` quando há `handoff`).
  2. Falha/expirado → mensagem "Este convite expirou. Você pode escolher o profissional normalmente." e segue o fluxo padrão.
  3. Usuário escolhe data/horário; `createReservation` (fluxo atual).
  4. Sucesso → `POST /api/customer/booking-handoffs/{handoffId}/complete { reservationId }` (best-effort; falha não impede o `navigate` para o detalhe da reserva).
- `professionalId` pré-selecionado a partir do handoff resolvido → usuário escolhe data/hora → reserva criada.

---

## 16. Dashboard PROFISSIONAL (`/profissional`)

**Dados reais disponíveis:** `apiClient.get('/api/professional/me')` → `{ name, profession, description, photoUrl }`; `professionalReservationsApi.list({status:'all',page:1,pageSize:50})` → `ReservationDto[]`; `professionalVisitsApi.list({status:'all',page:1,pageSize:50})` → `VisitDto[]`; `professionalAvailabilityApi.get()` → `ProfessionalAvailabilityDto` (`effectiveDays[].intervals`); `professionalLeasesApi.list()` → `ProfessionalLeaseDto[]`; `GET /api/professional/presence` (status próprio `AVAILABLE`/`IN_SERVICE`/`UNAVAILABLE`).

- **Shell:** já é sidebar (`ProfessionalShell`). Reestilizar para DNA escuro via tokens + `LumisBackground` (`intensity="muted"`). Nav (todos já existem): Dashboard, Agenda, Disponibilidade, Atendimentos, Reservas, Locações, Financeiro, Perfil. Topbar: `PROFISSIONAL` / "Olá, {primeiro nome}" / "Seu espaço, sua agenda, mais possibilidades." / data + avatar + nome + profissão.
- **Cards do topo (dados reais):**
  1. **Atendimentos hoje** = `visits` com `arrivedAt` hoje e `status != CANCELLED`.
  2. **Próximo horário** = próxima reserva `APPROVED` com `endAt >= now` (o `next` já calculado hoje).
  3. **Disponibilidade** = a partir de `availabilityApi.get()` `effectiveDays` do dia da semana → total de horas abertas; barra discreta.
  4. **Check-ins confirmados** = `visits` hoje com `status ∈ {WAITING, IN_SERVICE, ENDED}` (um `Visit` é um check-in confirmado).
- **Card principal "Agenda de hoje":** reservas de hoje, cada uma com rótulo semântico **derivado** do `Visit` correspondente: sem Visit → "Agendado"; `WAITING` → "Aguardando"; `IN_SERVICE` → "Em atendimento"; `ENDED` → "Concluído"; `CANCELLED` → "Cancelado". Nada hardcoded.
- **Coluna direita:** "Disponibilidade de hoje" (intervalos 08:00–18:00 + slots já reservados, progresso discreto); "Próximas reservas" (`APPROVED` após hoje); "Resumo/avisos" (ex.: nº de reservas `kind ∈ {RESCHEDULE, CANCELLATION}` `PENDING`).
- **Não inventar** mensagens/chat/documentos (sem backend).
- **Escopo:** esta rodada redesenha **Dashboard + shell + Agenda** (visual). Os `ProfessionalPlaceholder` (Reservas/Atendimentos/Locações/Financeiro/Perfil) **continuam placeholders** — cada um tem API pronta, mas construí-los é fora do escopo desta rodada (stretch opcional, não obrigatório).

---

## 17. Dashboard CLIENTE (`/cliente`)

**Dados reais:** `customerApi.me()` → `CustomerProfileDto`; `customerApi.reservations({page,pageSize})` → `PagedResponse<ReservationDto>`; `customerApi.reservation(id)`; `customerApi.issueCheckInToken(id)` → `{ token, manualCode, expiresAt }` (real, gate `CHECK_IN_NOT_ELIGIBLE` = 1 h antes → fim); `QRCode.toDataURL` client-side (já usado em `CustomerReservationDetail`).

- **Shell:** hoje é topbar horizontal (`CustomerShell`). Reestruturar para **sidebar** no desktop + drawer no mobile (mesmo padrão de `AdminLayout`), DNA escuro, `LumisBackground` `muted`. Logo passa a `/lumis-logo-transparent.png`.
- **Nav — só destinos reais:** Dashboard (`/cliente`), Agendamentos (`/cliente/agendamentos`), Novo agendamento (`/cliente/agendar`). **Não** adicionar itens para páginas inexistentes ("Reservas", "Check-in", "Profissionais", "Perfil"). "Check-in" é uma ação (card QR), não rota.
- Topbar: `ÁREA DO CLIENTE` / "Olá, {primeiro nome}!" / "Seu bem-estar em um só lugar." / data + avatar + nome.
- **Card "Próximo atendimento":** `reservations.items.filter(status === 'APPROVED' && endAt >= now).sort(startAt)[0]` → profissional, data, hora, sala, status, `[Ver detalhes]` → `/cliente/agendamentos/:id`. **Especialidade:** `ReservationDto` não traz profissão → o card **omite** especialidade nesta rodada (gap de backend flagado; card funciona sem ela).
- **Card "Meu QR Code":** reusa **o fluxo real** `issueCheckInToken`. Se a próxima reserva está elegível → `[Gerar QR Code]`; após gerar → `<img>` do QR + `manualCode` de 6 dígitos espaçado + `[copiar]`. **Nunca** hardcode (`4 7 2 9 1 6` é só referência visual). Se não elegível → texto "Disponível 1 h antes do horário".
- **Card "Novo agendamento":** "Agende seus atendimentos de forma rápida e prática." + `[Novo agendamento →]` → `/cliente/agendar`.
- **Listas:** "Próximos agendamentos" (`reservations.items` futuros); "Histórico recente" (`reservations.items` passados — derivado da mesma lista ordenada desc; a API suporta). **Sem notificações falsas** — bloco de notificações **omitido** (sem dados de domínio).

---

## 18. Dashboard ADMIN (`/admin`)

Hoje `/admin` index = `ModuleUnavailable`. Esta rodada **constrói** o dashboard.

**Dados reais:** `GET /api/admin/dashboard` → `DashboardSnapshot(OperationalDate, Counts, Financial, Alerts, Agenda, CurrentVisits, Rooms)`:
- `DashboardCounts(ActiveProfessionals, ActiveRooms, OccupiedRooms, ReservedRooms, ActiveLeases, ScheduledLeases, PendingReservations, TodayReservations, WaitingVisits, InServiceVisits)`
- `DashboardCurrentVisit(VisitId, VisitorName, Status, ProfessionalId, ProfessionalName, RoomId, RoomName, ArrivedAt, ServiceStartedAt, DurationMinutes)`
- `DashboardRoomStatus(RoomId, RoomName, Status, NextCommitmentAt)`
- `DashboardAlertSummary(Total, Warning, Critical, Recent: DashboardAlertItem[])`

Também: `GET /api/reception/overview` (via `receptionApi.overview()`), `GET /api/reception/professionals` → `ReceptionProfessionalResponse[]` (tem `Presence` + `OperationalStatus` por profissional).

- **Cliente TS a adicionar:** `dashboardApi.get()` para `/api/admin/dashboard`; `receptionApi.professionals()` para `/api/reception/professionals`. Endpoints já existem no backend.
- **Shell:** `AdminLayout` (sidebar `LUMIS` + rotas reais) — reestilizar para DNA escuro, `LumisBackground` `muted`, mantendo densidade operacional. Topbar: `ADMINISTRAÇÃO` / "Olá, Administrador." / "Visão completa do seu espaço em tempo real." / data/hora + usuário.
- **KPI cards (dados reais):**
  1. **Atendimentos hoje** = `Counts.TodayReservations` (sub-stat: `Counts.PendingReservations`).
  2. **Check-ins realizados** = **gap**: não há campo agregado de "visitas encerradas hoje". Nesta rodada, computar client-side de `receptionApi.visits({status:'all',page:1,pageSize:50})` filtrando por hoje, **com ressalva documentada de truncamento** em dia cheio (>50). Agregado no `DashboardCounts` fica como gap de backend para rodada futura.
  3. **Salas ocupadas** = `Counts.OccupiedRooms` (de `Counts.ActiveRooms`).
  4. **Reservas ativas** = `Counts.TodayReservations` / painel usa `Agenda`.
- **Painel "Recepção · Em atendimento":** `dashboard.CurrentVisits` (ordem, cliente, serviço/profissional, status, espera, `DurationMinutes`). `VisitorName` é PII, mas esta é tela **de operador** autenticado (`ADMINISTRADOR`) — nome completo é aceitável aqui (≠ Totem público).
- **"Ocupação das salas":** `dashboard.Rooms` (`RoomName`, `Status` "Em uso"/"Livre", `NextCommitmentAt`).
- **"Profissionais presentes":** `receptionApi.professionals()` → lista com `Presence`. Usar **"presentes"** (presença física), não "online".
- **"Atividades/Alertas":** `dashboard.Alerts` (`Total`/`Warning`/`Critical` + `Recent`). Só dados reais; sem atividade fabricada.

---

## 19. Dados/endpoints reutilizados (mapa)

| Tela | Endpoint(s) | Cliente TS |
|---|---|---|
| Totem handoff (Totem) | `POST /api/totem/booking-handoffs`, `.../{id}/status`, `.../{id}/cancel` | **novo** `totemApi.createHandoff/pollHandoff/cancelHandoff` |
| Handoff (celular) | `POST /api/customer/booking-handoffs/resolve`, `.../{id}/complete` | **novo** `customerApi.resolveHandoff/completeHandoff` |
| Dashboard Profissional | `/api/professional/me`, `/api/professional/reservations`, `/api/professional/visits`, `/api/professional/availability`, `/api/professional/leases`, `/api/professional/presence` | existentes (+ nada novo) |
| Dashboard Cliente | `/api/customer/me`, `/api/customer/reservations`, `/api/customer/reservations/{id}`, `/api/customer/reservations/{id}/check-in-token` | existentes |
| Dashboard Admin | `/api/admin/dashboard`, `/api/reception/overview`, `/api/reception/visits`, `/api/reception/professionals`, `/api/admin/operational-alerts` | **novos** `dashboardApi.get`, `receptionApi.professionals` |

---

## 20. Responsivo (todos os dashboards)

- Sidebar fixa lateral no desktop (≥1024 px); drawer/hamburger no tablet/mobile (reusar o padrão `open` do `AdminLayout`, incluindo overlay e `aria-label`).
- Grade de KPIs: 4-up desktop → 2-up tablet → 1-up mobile (mobile **nunca** 4 KPIs lado a lado).
- Tabelas: container `overflow-x: auto` no mobile; nunca espremidas ilegíveis. `body` nunca rola horizontalmente.
- Totem `/totem/handoff`: coluna única; QR `min(70vw, 320px)`; contagem abaixo.
- Totem `/totem/check-in`: coluna única em todos os breakpoints; câmera por `min()`.

---

## 21. Acessibilidade

- Mantém `focus-visible`, `aria-label`, `role`, navegação por teclado, contraste, alvo de toque ≥44 px.
- Carrossel: `role="listbox"`/`option`, `aria-selected`, Arrow/Home/End — inalterado.
- QR do handoff: `<img alt="QR Code para continuar o agendamento no seu celular">`.
- Contagem regressiva num container `aria-live="polite"` com atualização de texto **grosseira** (≈a cada 30 s), não a cada segundo.
- Status nunca só por cor: sempre texto + ícone (disponível / em atendimento / expirado / concluído).
- `prefers-reduced-motion: reduce` → remove/para o fundo animado e a entrada BlurFade; interface visualmente correta; nenhuma função depende de animação.

---

## 22. Testes

### 22.1 Backend (xUnit integração)

- `POST /api/totem/booking-handoffs`: retorna `handoffToken` ≠ `statusToken`; DB guarda só hashes (nenhum token em claro em nenhuma coluna); audit sem token/PII.
- `status` com `statusToken` correto: `PENDING`→`COMPLETED`→`EXPIRED`; com token errado → `INVALID_HANDOFF` genérico (sem vazar existência).
- Expiração preguiçosa após 5 min (via `FreezeTime`/`UnfreezeTime`).
- `resolve` aplica carência (10 min) e respeita teto de 20 min; 2º `resolve` não re-estende.
- `complete` liga `ReservationId` + vira `COMPLETED`; valida posse do customer, `ProfessionalId` casado, `Status == Approved`.
- Corpo `COMPLETED` **não** contém nome/telefone/email/CPF/`customerId`/`reservationId`/token (assert explícito).
- `cancel` → `EXPIRED`; idempotente.
- Rate limit das 3 partições anônimas.
- Um ciclo `/totem/*` não seta cookie de auth.

### 22.2 Frontend (vitest, TDD)

- `TotemHandoff`: renderiza nome/profissão + QR + contagem; faz polling e troca para ✓ em `COMPLETED`; auto-retorno em ~6 s com estado limpo e `localStorage` intacto; `EXPIRED` mostra os dois botões; "Escolher outro profissional" chama `cancel` e volta ao carrossel; sem `state` → redireciona para `/totem/profissionais`.
- `TotemCheckIn` novo layout: topbar (`← Voltar` / `LUMIS` / relógio), stage central, área de câmera grande, 6 caixas; **todos** os comportamentos de check-in preservados (reusar testes atuais, ajustar seletores só onde a estrutura mudou; `getByLabelText(/código de 6 dígitos/i)` casa **um** elemento).
- `CustomerBooking` com `?handoff=`: chama `resolve`, pré-seleciona profissional, chama `complete` após criar; caminho de expirado mostra mensagem e segue normal.
- Dashboards (Profissional/Cliente/Admin): renderizam de dados de API mockados; **sem** nomes/números hardcoded; rótulos de status derivados; card QR usa o fluxo real (`issueCheckInToken` mockado); caminho `prefers-reduced-motion`.
- `LumisBackground`: `aria-hidden`, `pointer-events: none`, sem `filter` em ancestrais; reduced-motion desliga a animação.
- `styles.css` (leitura): `--lumis-*` em `:root`; `--tk/tc/tp/te-*` resolvem através deles; `.totem-magic-fade.is-in.is-settled` zera `filter`/`will-change`.
- `frontend-portals.test.ts`: "Continuar" do Totem não vai para `/cliente/login`; `/totem/handoff` anônima.

### 22.3 Gates (rodar de `recepcaototem/ClientApp` + backend)

`dotnet test tests/GestaoPredio.IntegrationTests` · `npx vitest run` · `npx tsc -b` · `npx vite build` · `npm run --silent verify:production-bundle` · `git diff --check`.

---

## 23. Implicações de migration

- **Uma** migration nova: cria `TotemBookingHandoffs` (colunas da seção 8.1; índices únicos nos dois hashes; `IX_*_ProfessionalId`; `IX_*_ReservationId`; `IX_*_Status_ExpiresAt`; FKs `NoAction`; `xmin` rowversion).
- **Puramente aditiva:** sem `ALTER`/`DROP`/rename de tabela ou coluna existente, sem backfill, sem `UPDATE` massivo, sem tocar `Reservations`/`Visits`/`CheckInTokens`. `Down()` = `DropTable("TotemBookingHandoffs")`.
- **Não** criada nesta rodada (SPEC apenas) — mesmo padrão de "documentada, não criada" da §26 do design de check-in. Será criada na rodada de implementação (`dotnet ef migrations add TotemBookingHandoff` offline) e aplicada ao **staging Supabase pelo cliente** (não por mim).
- Todo o trabalho de tokens CSS, fundo, dashboards e layout do check-in é **migration-free**.
- `20260910085909_CheckInManualCode` (já aplicada) não é afetada.

---

## 24. Implicações operacionais

- **Quiosque:** o código garante que `/totem/*` nunca autentica; recomenda-se perfil de navegador **dedicado** ao quiosque (sem login CUSTOMER no dispositivo), pois um navegador compartilhado poderia carregar um cookie de cliente antigo. Nota de deploy, não código que desloga ninguém.
- **Tabela `TotemBookingHandoffs`:** ~1 linha por visita "sem código" no Totem. Linhas minúsculas (só FKs `ProfessionalId`/`ReservationId`, sem PII). Limpeza periódica (deletar `Expired`/`Completed` com > N dias) é *nice-to-have*, não bloqueia lançamento; o índice `IX_*_Status_ExpiresAt` já prepara isso.
- **Sem novos segredos, sem nova config de ambiente.** Janelas 5/10/20 min são constantes no endpoint (como `AUTO_RESET_MS`).
- **Railway/Supabase:** uma migration a aplicar no deploy (pelo cliente). CSP inalterada → sem mudança de header/config. Carga de polling desprezível (1 quiosque = 1 req/2 s).
- **CSP:** o QR do handoff é `<img src="data:image/png;...">` gerado por `qrcode` — já coberto por `img-src 'self' data: blob:`. Nada muda.

---

## 25. Fora de escopo (reafirmado)

Intelbras real, Meta/WhatsApp, Resend, pagamentos, app nativo, **WebSocket para o handoff**, refatoração geral de backend, redesenho de DB sem necessidade, novos módulos admin sem requisito, analytics, domínio customizado, **merge em `main`**, push/deploy/migration remota nesta etapa. Investigação do **POST 400 do cadastro profissional** permanece separada — a máscara de WhatsApp `aa1b3f4` é UX aprovada mas **não** prova que o 400 foi corrigido. Construir como features completas `/cliente/perfil`, `/cliente/profissionais` e os `ProfessionalPlaceholder` (Reservas/Atendimentos/Locações/Financeiro) — fora desta rodada. Follow-up `.totem-magic-fade` além da "settle" da seção 4 — não.

---

## Auto-revisão da SPEC

- **Contradições:** nenhuma detectada. O ternário de `ProtectedRoute` e o regex de `returnUrl.ts` são citados como preservados; o handoff reusa-os sem alteração.
- **Rotas reais:** confirmadas contra `src/App.tsx` — CUSTOMER só tem `/cliente`, `/cliente/agendamentos`, `/cliente/agendar`, `/cliente/agendamentos/:id`; a SPEC **não** cria itens de menu para rotas inexistentes. Nova rota `/totem/handoff` é pública (fora de `ProtectedRoute`).
- **Endpoints reais:** `/api/admin/dashboard` e `/api/reception/professionals` existem no backend; só falta cliente TS (gap explícito). Demais dados de dashboard vêm de APIs já consumidas.
- **Schemas:** `DashboardSnapshot`/`ReceptionOverviewResponse`/`ReservationDto`/`VisitDto`/`ProfessionalAvailabilityDto` conferidos; "especialidade no card do cliente" e "check-ins encerrados hoje no admin" marcados como gaps de backend, com fallback definido.
- **Migration:** o handoff precisa de **uma** migration aditiva (nova tabela). Todo o resto (tokens CSS, fundo, dashboards, layout check-in) é migration-free. Documentada, não criada.
- **Auth / sessão CUSTOMER no quiosque:** mitigada por design (endpoints anônimos, sem `useSession` nos fluxos, sem navegação a rota protegida, sem storage) + nota operacional de perfil dedicado.
- **Fundo / performance:** só `transform`/`opacity`; `prefers-reduced-motion` para tudo; sem `filter` animado; a "settle" do BlurFade (seção 4) remove a causa raiz do bug de compositing do carrossel.
- **BlurFade/filter/carrossel:** auditado na seção 4; correção mínima (`is-settled` → `filter: none`), sem tocar no carrossel nem em `165f2f1`.
- **Dados falsos:** nenhum. Nomes de referência (Mariana/Rafael/Camila/Dra. Helena) e o código `4 7 2 9 1 6` são só referência visual; produção usa APIs reais; blocos sem dados de domínio (notificações do cliente, chat do profissional) são omitidos, não fabricados.

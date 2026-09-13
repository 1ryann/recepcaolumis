# Totem Native Carousel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restaurar o swipe pelo scroll nativo e ampliar o coverflow, com aceite obrigatório em celular físico.

**Architecture:** O navegador produz o movimento; `onScroll` + `requestAnimationFrame` continuam derivando o card mais próximo do centro. `data-offset` mantém o coverflow; flex, padding e clamp dimensionam o palco. A entrega independe do catálogo de salas.

**Tech Stack:** React + TypeScript + Vite, CSS, Vitest + Testing Library; dependências existentes.

**Spec:** `docs/superpowers/specs/2026-09-13-room-rental-and-totem-carousel.md`, §§2–4, 12–14.

## Global Constraints

- `touch-action: pan-x pan-y` no viewport e no card; `overflow-x: auto`; `scroll-snap-type: x mandatory`.
- Manter exatamente a lógica de `onScroll` + `requestAnimationFrame`; nenhum `IntersectionObserver`.
- Remover pointerdown/move/up/cancel/leave, captura/liberação, thresholds, refs de drag e escrita de scrollLeft durante arrasto.
- Manter `centre(index)`, `setActive(index)`, setas, dots, teclado, clique lateral/central e fallback de foto.
- Preservar professionalId, dados, disponibilidade, handoff, autenticação e reservas.
- Layout por flex/grid, gap, padding, min-height e clamp; nenhum absolute/top/translateY novo para reposicionar palco. Perspectiva/rotação/escala existentes permanecem.
- Referências desktop: central 300–340px × 420–470px; palco 480–560px. Validar visualmente, não usar números cegamente.
- Capturas obrigatórias: 1920×1080, 1366×768, 768×1024, 390×844.
- O swipe só será considerado resolvido após teste em CELULAR FÍSICO.
- Este plano não implementa aluguel nem gera migration. Nenhum pacote novo, alteração de env vars, Supabase ou Railway.
- Push, merge e deploy exigem autorização futura explícita. A etapa atual autoriza somente planos e commits documentais.

## Base verificada e arquivos

Worktree `.worktrees/reception-backend`, branch `codex/reception-backend`, HEAD `16979ed7ce2b280d74a2c2a70aaffc444c641631`. O diretório principal está em `codex/leases-design`; executar na worktree correta, nunca em main/master. Este arquivo existia como rascunho não versionado; esta revisão corrige publicação automática e referências a testes inexistentes.

| Arquivo real | Responsabilidade |
|---|---|
| `recepcaototem/ClientApp/src/features/totem/TotemProfessionalCarousel.tsx` | gesto, seleção e geometria |
| `recepcaototem/ClientApp/src/features/totem/TotemProfessionalCarousel.test.tsx` | dez testes atuais; quatro fixam drag customizado |
| `recepcaototem/ClientApp/src/styles.css` | viewport, card, mobile e composição |
| `recepcaototem/ClientApp/src/pages/TotemProfessionals.tsx` | consumidor existente, somente leitura neste plano |
| `recepcaototem/ClientApp/src/pages/TotemProfessionals.test.tsx` | regressão da seleção/handoff |
| `recepcaototem/ClientApp/src/pages/TotemHandoff.test.tsx` | regressão do QR |
| `docs/operations/2026-09-13-totem-native-carousel-acceptance.md` | novo registro de aceite físico, Task 3 |

**Diferença factual da spec:** o teste de geometria de onScroll citado não existe no arquivo atual. Criá-lo como caracterização, que deve passar antes da mudança. Não fabricar RED alterando lógica correta. O RED da Task 1 será o contrato de scroll nativo.

O comentário atual atribui o problema a filtros de ancestrais, mas não constitui prova de causa. Removê-lo junto do drag; não modificar BlurFade preventivamente. Mouse continua com clique, teclado, setas e roda/trackpad; não prometer click-drag de mouse sem JS.

## Gates e dependências

Task 1 → Task 2 → Task 3. Nenhuma depende do Plano 2. Código pode ser publicado isoladamente após gates e autorização; aceite do bug permanece pendente até homologação física.

Na raiz da worktree:
```powershell
git status --porcelain -uall
git branch --show-current
git rev-parse HEAD
git log -10 --oneline
git diff --check
```

Em `recepcaototem/ClientApp`, gates encontrados em package.json/spec:
```powershell
npx vitest run
npx tsc -b
npx vite build
node scripts/verify-production-bundle.mjs
```

Não executados durante escrita documental. Não há gate backend para alteração exclusivamente frontend. Ausência de dependências deve ser reportada; não autoriza instalação.

---

### Task 1: Scroll nativo com seleção preservada

**Files:**
- Create: nenhum.
- Modify: `recepcaototem/ClientApp/src/features/totem/TotemProfessionalCarousel.tsx`.
- Modify: `recepcaototem/ClientApp/src/styles.css`.
- Test: `recepcaototem/ClientApp/src/features/totem/TotemProfessionalCarousel.test.tsx`.

**Interfaces:**
- Consumes: `TotemProfessionalCardDto[]` de modules.ts, `usePrefersReducedMotion(): boolean`.
- Produces: mesma `TotemProfessionalCarousel({ professionals, onActiveChange, onContinue? })`; callbacks `(professional: TotemProfessionalCardDto) => void` e `() => void`; listbox/option, aria-selected e data-offset preservados.

- [ ] **Step 1: Escrever o teste que falha e as caracterizações.**

Substituir o teste atual `card disables native touch panning...` por:
```tsx
test('viewport and cards permit native horizontal and vertical gestures', () => {
  const css = readFileSync(resolve(process.cwd(), 'src/styles.css'), 'utf8')
  for (const selector of ['viewport', 'card']) {
    const rule = css.match(new RegExp('\\.totem-carousel-' + selector + '\\s*\\{([^}]*)\\}'))
    expect(rule).not.toBeNull()
    expect(rule![1]).toMatch(/touch-action:\s*pan-x pan-y\s*;/)
  }
  const source = readFileSync(resolve(process.cwd(),
    'src/features/totem/TotemProfessionalCarousel.tsx'), 'utf8')
  expect(source).not.toMatch(/onPointerDown|onPointerMove|onPointerEnd|setPointerCapture|releasePointerCapture|didDragRef/)
})
```

Adicionar `act` ao import RTL. A seguinte caracterização deve passar já na base:
```tsx
test('onScroll finds the nearest centre without scrolling back', () => {
  let frame: FrameRequestCallback = () => {}
  const raf = vi.spyOn(window, 'requestAnimationFrame').mockImplementation(cb => { frame = cb; return 1 })
  const emit = vi.fn()
  const view = render(<TotemProfessionalCarousel professionals={people} onActiveChange={emit} />)
  const viewport = screen.getByRole('listbox')
  Object.defineProperty(viewport, 'clientWidth', { value: 400, configurable: true })
  screen.getAllByRole('option').forEach((card, index) => {
    Object.defineProperty(card, 'offsetLeft', { value: 100 + index * 220, configurable: true })
    Object.defineProperty(card, 'offsetWidth', { value: 200, configurable: true })
  })
  viewport.scrollLeft = 220
  emit.mockClear()
  vi.mocked(viewport.scrollTo).mockClear()
  fireEvent.scroll(viewport)
  act(() => frame(0))
  expect(emit).toHaveBeenLastCalledWith(people[1])
  expect(screen.getAllByRole('option')[1]).toHaveAttribute('aria-selected', 'true')
  expect(viewport.scrollTo).not.toHaveBeenCalled()
  view.unmount()
  raf.mockRestore()
})
test('side click centres and active click continues', () => {
  const emit = vi.fn(), next = vi.fn()
  render(<TotemProfessionalCarousel professionals={people} onActiveChange={emit} onContinue={next} />)
  fireEvent.click(screen.getAllByRole('option')[1])
  expect(emit).toHaveBeenLastCalledWith(people[1])
  expect(next).not.toHaveBeenCalled()
  fireEvent.click(screen.getAllByRole('option')[1])
  expect(next).toHaveBeenCalledTimes(1)
})
```

Casos adicionais precisos: geometria zero não emite; dois scrolls antes do frame pedem só um rAF; unmount cancela frame pendente; Home/End/ArrowLeft respeitam limites. Usar os mesmos spies restaurados ao fim de cada teste.

- [ ] **Step 2: Rodar e confirmar RED.**

Em ClientApp:
```powershell
npx vitest run src/features/totem/TotemProfessionalCarousel.test.tsx
```
Esperado: contrato nativo falha por `touch-action: none` e handlers presentes. Caracterizações passam; falha nelas exige investigação, não alteração automática de onScroll.

- [ ] **Step 3: Implementar o mínimo.**

Remover import PointerEvent; DRAG_THRESHOLD_PX, SWIPE_COMMIT_PX, SWIPE_CARD_PX; didDragRef, pointerActiveRef, dragOriginXRef, dragLastXRef, dragTravelRef; funções onPointerDown/onPointerMove/onPointerEnd e as cinco props de ponteiro. Manter o corpo de onScroll/centre/setActive/onKeyDown e rafRef integralmente.

```tsx
<div className="totem-carousel-viewport" role="listbox" aria-label="Profissionais"
  tabIndex={0} ref={viewportRef} onKeyDown={onKeyDown} onScroll={onScroll}>
```

No onClick de cada card:
```tsx
onClick={() => {
  if (active) { onContinueRef.current?.(); return }
  setActive(index)
}}
```

Fundir nas regras existentes, preservando demais declarações:
```css
.totem-carousel-viewport {
  overflow-x: auto;
  scroll-snap-type: x mandatory;
  touch-action: pan-x pan-y;
}
.totem-carousel-card {
  scroll-snap-align: center;
  touch-action: pan-x pan-y;
  user-select: none;
  -webkit-user-select: none;
  -webkit-touch-callout: none;
}
```

Manter draggable=false na imagem e reduced-motion. Remover cursor grab/grabbing e comentários de drag JS. Não adicionar preventDefault de touch ou remover propriedades Safari sem necessidade.

Apagar somente os quatro testes: `mouse drag scrolls and suppresses the click-select`, `a touch swipe moves the active card left and right (no arrows needed)`, `a long touch fling can jump more than one card`, `a tiny touch drag stays a tap and does not change the card`. Retirar stubs de captura sem uso; preservar demais testes.

- [ ] **Step 4: Rodar e confirmar GREEN.**

Repetir Step 2, todos passam sem skips. Conferir no diff que onScroll não mudou.

- [ ] **Step 5: Rodar regressões.**

Executar os quatro gates frontend e git diff --check. Confirmar TotemProfessionals/TotemHandoff preservam professionalId e QR.

- [ ] **Step 6: Commit.**

Na raiz:
```powershell
git add recepcaototem/ClientApp/src/features/totem/TotemProfessionalCarousel.tsx recepcaototem/ClientApp/src/features/totem/TotemProfessionalCarousel.test.tsx recepcaototem/ClientApp/src/styles.css
git commit -m "fix(totem): use native scroll-snap for professional carousel"
```

### Task 2: Coverflow maior e palco mais abaixo

**Files:**
- Create: nenhum.
- Modify: `recepcaototem/ClientApp/src/styles.css`.
- Test: `recepcaototem/ClientApp/src/features/totem/TotemProfessionalCarousel.test.tsx`.
- Test: capturas nas quatro resoluções, vinculadas ao registro da Task 3.

**Interfaces:**
- Consumes: classes totem-carousel-viewport/card/is-active e offsets -2..2.
- Produces: mesmas classes/DOM; largura compartilhada por card e padding de borda, palco ampliado, vizinhos visíveis.

- [ ] **Step 1: Escrever teste que falha e capturar referência.**

```tsx
test('cards and edge padding share a responsive width', () => {
  const css = readFileSync(resolve(process.cwd(), 'src/styles.css'), 'utf8')
  expect(css).toContain('--totem-card-width:')
  expect(css).toMatch(/flex:\s*0 0 var\(--totem-card-width\)/)
  expect(css).toContain('calc(50% - (var(--totem-card-width) / 2))')
  expect(css).toMatch(/min-height:\s*var\(--totem-stage-height\)/)
})
```

Capturar a composição atual nas quatro resoluções. O teste protege consistência, não prova aparência.

- [ ] **Step 2: Rodar e confirmar RED.**

```powershell
npx vitest run src/features/totem/TotemProfessionalCarousel.test.tsx
```
Esperado: variáveis inexistentes. Registrar composição pequena antes da alteração.

- [ ] **Step 3: Implementar o mínimo de layout.**

Fundir declarações nas regras atuais; valores iniciais sujeitos a captura:
```css
.totem-carousel {
  --totem-card-width: clamp(280px, 28vw, 320px);
  --totem-stage-height: clamp(480px, 54vh, 540px);
}
.totem-carousel-viewport {
  align-items: center;
  padding-inline: max(12px, calc(50% - (var(--totem-card-width) / 2)));
  padding-block: clamp(24px, 5vh, 48px) clamp(18px, 3vh, 32px);
  min-height: var(--totem-stage-height);
}
.totem-carousel-card {
  flex: 0 0 var(--totem-card-width);
  min-height: clamp(360px, 39vh, 400px);
}
.totem-carousel-card.is-active { min-height: clamp(420px, 43vh, 450px); }
.totem-carousel-photo {
  width: clamp(112px, 12vw, 148px);
  height: clamp(112px, 12vw, 148px);
}
.totem-professionals-inner {
  padding-block: clamp(24px, 5vh, 48px) clamp(12px, 2vh, 24px);
  gap: clamp(24px, 4vh, 44px);
}
@media (max-width: 560px) {
  .totem-carousel {
    --totem-card-width: min(80vw, 320px);
    --totem-stage-height: calc(var(--totem-card-width) * 1.6);
  }
  .totem-carousel-card {
    flex-basis: var(--totem-card-width);
    min-height: calc(var(--totem-card-width) * 1.2);
  }
  .totem-carousel-card.is-active { min-height: calc(var(--totem-card-width) * 1.4); }
  .totem-carousel-viewport {
    padding-inline: max(12px, calc(50% - (var(--totem-card-width) / 2)));
  }
}
```

Substituir o override mobile antigo de 240px. Preservar justify-content:center, cores, sombras e rotações. Reservar padding para escala 1.06 e sombras; ajustar caso haja clipping. Se gutters impedirem vizinhos em 390px, reduzir padding-inline do inner para 8px no breakpoint e verificar também 320px. Para telas baixas, manter documento rolável e não impor altura fixa. Não usar a CTA absoluta existente para mover o palco.

- [ ] **Step 4: Rodar e confirmar GREEN.**

Repetir teste focado e capturas reais: central dominante, laterais inclinados, nomes legíveis, primeiro/último centrados, dots/foco acessíveis, sem overflow horizontal do documento. Ajustar medidas pelas imagens.

- [ ] **Step 5: Rodar regressões.**

Quatro gates frontend e git diff --check. Verificar 0/1/2/vários profissionais com fixtures de teste/local, reduced-motion, zoom 200%, teclado e scroll vertical; não commitar mocks de produção.

- [ ] **Step 6: Commit.**

```powershell
git add recepcaototem/ClientApp/src/styles.css recepcaototem/ClientApp/src/features/totem/TotemProfessionalCarousel.test.tsx
git commit -m "fix(totem): enlarge responsive coverflow stage"
```

### Task 3: Gates e homologação física com evidência

**Files:**
- Create: `docs/operations/2026-09-13-totem-native-carousel-acceptance.md`.
- Modify: esse registro após as verificações.
- Test: matriz manual abaixo e suítes frontend existentes.

**Interfaces:**
- Consumes: SHA Tasks 1–2, gates e URL executando exatamente esse build.
- Produces: registro com SHA, URL, data, aparelho, OS, browser/versão e resultado de cada caso; aceite físico ou pendência explícita.

- [ ] **Step 1: Escrever o teste de aceitação inicialmente não satisfeito.**

Criar matriz com resultado inicial NÃO EXECUTADO para cada caso: quatro resoluções; swipe esquerda/direita; dez swipes seguidos; swipe curto/longo nos extremos; tap lateral centraliza; tap central abre handoff correto; scroll não dispara handoff; scroll vertical sobre imagem/texto; ausência de seleção/callout/arrasto de imagem; nenhum travamento.

- [ ] **Step 2: Rodar a verificação inicial e registrar RED de aceite.**

Sem execução física, registrar ACEITE FÍSICO PENDENTE. Trata-se de teste manual, não de falha inventada de unit test. Se existir reprodução no aparelho/build anterior, identificar seu SHA.

- [ ] **Step 3: Preparar o mínimo para testar o resultado.**

Preencher SHAs, gates e capturas. Usar instância local acessível ao celular ou staging com publicação autorizada separadamente. Se precisar publicar, apresentar o resultado concreto para autorização; não fazer push/deploy/env vars por força deste plano. Após publicação autorizada, conferir identidade do build por artefatos/deployment e /health + /health/ready. Last-Modified sozinho não prova SHA.

- [ ] **Step 4: Executar no aparelho e confirmar GREEN com evidência.**

O usuário executa a matriz no celular físico que reproduzia o bug, com toque real sobre foto e texto. Registrar aparelho, sistema, navegador/versão, URL, SHA e resultados. Sem dispositivo ou retorno, manter PENDENTE e não encerrar o bug. Se falhar, voltar ao ciclo da tarefa responsável sem reintroduzir thresholds.

- [ ] **Step 5: Rodar regressões do build candidato.**

Confirmar gates para o SHA final, smoke de seleção/handoff e quatro capturas. Repetir gates se houver alteração posterior; eventos sintéticos não substituem a matriz física.

- [ ] **Step 6: Commit do registro verdadeiro.**

```powershell
git add docs/operations/2026-09-13-totem-native-carousel-acceptance.md
git commit -m "docs(totem): record native carousel acceptance evidence"
```

Um registro de preparação pode ser commitado com pendências explícitas, mas não equivale a GREEN nem conclui a Task 3. Não criar commit vazio ou inventar resultados.

## Autorrevisão

§§3.1–3.4 → Task 1; §4 → Task 2; §3.5 → Task 3. O teste inexistente foi identificado, a lógica correta será preservada e nenhum contrato de aluguel entra no plano. Revisão documental concluída; implementação e homologação ainda não executadas.

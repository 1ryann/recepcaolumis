# Tutorial guiado e central de ajuda — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Entregar um tour guiado próprio na interface de administração e uma central de ajuda pública em `/ajuda`, com trilhas separadas para Administrador, Profissional e Cliente.

**Architecture:** Todo o conteúdo vive em arquivos TypeScript versionados em `src/help/content`. Um `TourProvider` montado em `main.tsx` decide se o tour roda, resolve o elemento alvo por `data-tour` e renderiza um único `TourOverlay` com spotlight. O progresso fica em `localStorage` atrás da interface `TourProgressStore`, para migrar ao backend depois sem reescrever o motor. Nenhuma dependência nova, nenhuma alteração de backend.

**Tech Stack:** React 19 + TypeScript + Vite, react-router-dom, Vitest + Testing Library + jsdom, CSS à mão em `src/styles.css`, ícones `lucide-react` (já presente).

**Spec:** `docs/superpowers/specs/2026-09-23-tutorial-onboarding-design.md`

## Global Constraints

- **Nenhuma dependência nova** em `recepcaototem/ClientApp/package.json`. Sem biblioteca de tour.
- **Nenhuma alteração de backend, entidade, migration ou endpoint.**
- Idioma: **pt-BR apenas**. Sem internacionalização.
- As telas existentes recebem **apenas atributos `data-tour`**. Nenhuma mudança de classe, layout, estrutura ou comportamento. A única exceção aprovada é o link "Ajuda" no rodapé da barra lateral do `AdminLayout`.
- Chave de storage: `lumis.tour.<trilha>.v1`. Nunca use o prefixo `atrium_`, proibido no bundle por `scripts/verify-production-bundle.mjs`.
- Prazo de espera por um alvo: **1500 ms**. Esgotado, o passo é pulado.
- Nenhum dado pessoal gravado em storage — somente progresso.
- **Estilo de código do projeto:** sem ponto e vírgula no fim das linhas, aspas simples, JSX denso (várias tags por linha), `type` em vez de `interface`, sem comentários explicativos no código. Leia um arquivo vizinho antes de escrever.
- **Worktree compartilhado:** outras sessões editam os mesmos arquivos. Em cada commit, dê `git add` **somente nos caminhos listados na tarefa**. Nunca use `git add -A`, `git add .` ou `git commit -a`.
- Comandos `npm` rodam em `recepcaototem/ClientApp`. Comandos `git` rodam na raiz do repositório.
- Cada commit termina com a linha `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

---

## Estrutura de arquivos

| Arquivo | Responsabilidade |
|---|---|
| `src/help/content/types.ts` | Tipos `Passo`, `Trilha`, `TrilhaId`. Fonte da verdade do formato. |
| `src/help/content/admin.ts` | Trilha do Administrador, a única com passos ancorados. |
| `src/help/content/profissional.ts` | Trilha do Profissional, somente leitura. |
| `src/help/content/cliente.ts` | Trilha do Cliente, somente leitura, pública. |
| `src/help/content/index.ts` | Registro role → trilha, trilhas públicas, filtro por role. |
| `src/help/tourStorage.ts` | `TourProgressStore`, implementação `localStorage`, fallback em memória. |
| `src/help/useTourTarget.ts` | Espera o elemento por `data-tour` e mede o retângulo. |
| `src/help/TourProvider.tsx` | Estado do tour, gatilho, navegação, persistência. |
| `src/help/TourOverlay.tsx` | Spotlight, balão do passo, teclado, `aria`. |
| `src/help/HelpCenter.tsx` | Página `/ajuda`. |
| `src/styles.css` | Seção nova `/* Tutorial */` no fim do arquivo. |
| `src/App.tsx` | Rota pública `/ajuda`. |
| `src/dev/DevelopmentApp.tsx` | Mesma rota `/ajuda`, senão a página não existe no dev server. |
| `src/main.tsx` | Montagem do `TourProvider`. |
| `src/components/AdminLayout.tsx` | `data-tour` na navegação e link "Ajuda" no rodapé. |
| `src/pages/admin/Rooms.tsx` | `data-tour` no botão de nova sala e na busca. |
| `src/pages/admin/Professionals.tsx` | `data-tour` no botão, busca, filtro, ações e paginação. |

---

### Task 1: Tipos e trilhas de leitura

**Files:**
- Create: `recepcaototem/ClientApp/src/help/content/types.ts`
- Create: `recepcaototem/ClientApp/src/help/content/profissional.ts`
- Create: `recepcaototem/ClientApp/src/help/content/cliente.ts`
- Create: `recepcaototem/ClientApp/src/help/content/admin.ts`
- Create: `recepcaototem/ClientApp/src/help/content/index.ts`
- Test: `recepcaototem/ClientApp/src/help/content.test.ts`

**Interfaces:**
- Consumes: nada.
- Produces: `Passo`, `Trilha`, `TrilhaId` de `types.ts`. De `index.ts`: `trilhas: Trilha[]`, `trilhaPorId(id: TrilhaId): Trilha | undefined`, `trilhaDaRole(roles: string[]): Trilha | undefined`, `trilhasPublicas(): Trilha[]`, `passosVisiveis(trilha: Trilha, roles: string[]): Passo[]`.

Nesta tarefa `admin.ts` é criada **sem passos ancorados** (`alvo` ausente em todos os passos). Os `alvo` entram na Task 2, junto com os `data-tour` que eles apontam, para que cada tarefa termine verde.

- [ ] **Step 1: Escreva o teste falhando**

`src/help/content.test.ts`:

```ts
import { expect, test } from 'vitest'
import { passosVisiveis, trilhaDaRole, trilhaPorId, trilhas, trilhasPublicas } from './content'

test('cada trilha tem ids de passo únicos', () => {
  for (const trilha of trilhas) {
    const ids = trilha.passos.map(passo => passo.id)
    expect(new Set(ids).size, `ids duplicados na trilha ${trilha.id}`).toBe(ids.length)
  }
})

test('cada trilha tem título, resumo, versão e ao menos um passo', () => {
  for (const trilha of trilhas) {
    expect(trilha.titulo.length).toBeGreaterThan(0)
    expect(trilha.resumo.length).toBeGreaterThan(0)
    expect(trilha.versao).toBeGreaterThanOrEqual(1)
    expect(trilha.passos.length).toBeGreaterThan(0)
  }
})

test('roles de gestão recebem a trilha do administrador', () => {
  expect(trilhaDaRole(['ADMINISTRADOR'])?.id).toBe('admin')
  expect(trilhaDaRole(['GERENTE'])?.id).toBe('admin')
})

test('a role de profissional recebe a própria trilha', () => {
  expect(trilhaDaRole(['PROFISSIONAL'])?.id).toBe('profissional')
})

test('role desconhecida não recebe trilha', () => {
  expect(trilhaDaRole(['OUTRA'])).toBeUndefined()
  expect(trilhaDaRole([])).toBeUndefined()
})

test('as trilhas de profissional e cliente são públicas e a de admin não', () => {
  const publicas = trilhasPublicas().map(trilha => trilha.id)
  expect(publicas).toContain('profissional')
  expect(publicas).toContain('cliente')
  expect(publicas).not.toContain('admin')
})

test('passosVisiveis remove passos restritos a outra role', () => {
  const trilha = trilhaPorId('admin')!
  const restrito = { id: 'restrito-teste', titulo: 'T', texto: 'X', somenteRoles: ['ADMINISTRADOR'] }
  const alterada = { ...trilha, passos: [...trilha.passos, restrito] }
  expect(passosVisiveis(alterada, ['GERENTE']).map(passo => passo.id)).not.toContain('restrito-teste')
  expect(passosVisiveis(alterada, ['ADMINISTRADOR']).map(passo => passo.id)).toContain('restrito-teste')
})
```

- [ ] **Step 2: Rode o teste para confirmar que falha**

```bash
npm test -- --run src/help/content.test.ts
```

Esperado: FAIL — não resolve o módulo `./content`.

- [ ] **Step 3: Escreva os tipos**

`src/help/content/types.ts`:

```ts
export type TrilhaId = 'admin' | 'profissional' | 'cliente'

export type Passo = {
  id: string
  titulo: string
  texto: string
  alvo?: string
  rota?: string
  somenteRoles?: string[]
}

export type Trilha = {
  id: TrilhaId
  titulo: string
  resumo: string
  publica: boolean
  versao: number
  passos: Passo[]
}
```

- [ ] **Step 4: Escreva a trilha do Profissional**

`src/help/content/profissional.ts`:

```ts
import type { Trilha } from './types'

export const trilhaProfissional: Trilha = {
  id: 'profissional',
  titulo: 'Para profissionais',
  resumo: 'Como funcionam suas visitas, suas solicitações de agenda e suas locações.',
  publica: true,
  versao: 1,
  passos: [
    { id: 'prof-visitas-proprias', titulo: 'Você acompanha apenas as suas visitas', texto: 'Cada profissional vê e movimenta somente as visitas dos próprios atendimentos. A gestão pode atuar em qualquer visita, e toda correção excepcional registra autor, data e os estados anterior e novo.' },
    { id: 'prof-agenda-por-solicitacao', titulo: 'A agenda muda por solicitação', texto: 'Você não altera a agenda diretamente. Nova reserva, reagendamento e cancelamento são solicitações enviadas à gestão, que aprova ou recusa. A recusa sempre vem com justificativa.' },
    { id: 'prof-antecedencia', titulo: 'Uma hora de antecedência', texto: 'Nova reserva, reagendamento e cancelamento exigem pelo menos uma hora de antecedência. Nova reserva também depende de haver horário disponível.' },
    { id: 'prof-reserva-durante-analise', titulo: 'Sua reserva continua válida durante a análise', texto: 'Enquanto a solicitação está pendente, a reserva original segue valendo. A disponibilidade é verificada de novo no momento da aprovação.' },
    { id: 'prof-bloqueio-visita-aberta', titulo: 'Visita em andamento bloqueia mudanças', texto: 'Quando a ocorrência já tem visita aguardando ou em atendimento, reagendamento e cancelamento ficam bloqueados. Resolva a visita primeiro.' },
    { id: 'prof-financeiro-consulta', titulo: 'Financeiro é consulta', texto: 'Você consulta valor, vencimento e situação das suas locações, sem alterá-los. Pagamento em atraso não encerra a locação nem retira você do totem.' },
    { id: 'prof-liberacao-acesso', titulo: 'Liberação de acesso é registrada', texto: 'Você pode solicitar liberação de acesso nas suas próprias visitas. Toda ação é confirmada, limitada e registrada em auditoria.' },
  ],
}
```

- [ ] **Step 5: Escreva a trilha do Cliente**

`src/help/content/cliente.ts`:

```ts
import type { Trilha } from './types'

export const trilhaCliente: Trilha = {
  id: 'cliente',
  titulo: 'Para quem vai ser atendido',
  resumo: 'Como agendar, o que acontece na chegada e o que fazemos com os seus dados.',
  publica: true,
  versao: 1,
  passos: [
    { id: 'cliente-agendamento', titulo: 'O agendamento', texto: 'Um atendimento acontece em uma sala reservada, em um horário definido. O agendamento pode ser feito por você ou registrado na recepção no momento da chegada.' },
    { id: 'cliente-chegada', titulo: 'Ao chegar no edifício', texto: 'Você informa sua chegada no totem da recepção e escolhe o profissional que vai atender. O profissional recebe o aviso e vem buscá-lo quando estiver pronto.' },
    { id: 'cliente-janela', titulo: 'Quando o totem aceita sua chegada', texto: 'O atendimento aparece no totem a partir de uma hora antes do horário marcado e até o fim dele. Depois disso o totem informa que o atendimento foi encerrado e não aceita nova chegada.' },
    { id: 'cliente-qr', titulo: 'Check-in por QR', texto: 'Quando você recebe um código QR do seu agendamento, ele identifica a sua chegada diretamente, sem precisar procurar seu nome na lista.' },
    { id: 'cliente-privacidade', titulo: 'Foto e privacidade', texto: 'A chegada pode incluir uma foto, sempre com aviso antes da captura. A imagem fica em armazenamento privado, é usada apenas para identificar a sua visita e segue a política de retenção do edifício.' },
  ],
}
```

- [ ] **Step 6: Escreva a trilha do Administrador, ainda sem alvos**

`src/help/content/admin.ts`:

```ts
import type { Trilha } from './types'

export const trilhaAdmin: Trilha = {
  id: 'admin',
  titulo: 'Para a administração',
  resumo: 'Onde ficam salas e profissionais, e o que já está disponível no sistema.',
  publica: false,
  versao: 1,
  passos: [
    { id: 'admin-navegacao', titulo: 'A navegação', texto: 'Por aqui você alcança as áreas do sistema. Gestão reúne o dia a dia do edifício; Preferências guarda as configurações.' },
    { id: 'admin-modulos-em-construcao', titulo: 'O que já está disponível', texto: 'Salas e Profissionais já operam com dados reais. Visão geral, Locações, Visitas e Configurações ainda estão em construção e mostram um aviso ao serem abertas.' },
    { id: 'admin-nova-sala', titulo: 'Cadastrar uma sala', texto: 'Cada sala tem nome, descrição e as tarifas por hora e por diária. As tarifas são informadas em reais.' },
    { id: 'admin-busca-salas', titulo: 'Encontrar uma sala', texto: 'A busca filtra por nome, e o seletor ao lado mostra apenas salas ativas ou inativas.' },
    { id: 'admin-novo-profissional', titulo: 'Cadastrar um profissional', texto: 'O cadastro reúne nome, profissão e WhatsApp. O número é normalizado pela API no padrão internacional.' },
    { id: 'admin-busca-profissionais', titulo: 'Encontrar um profissional', texto: 'A busca aceita nome ou profissão.' },
    { id: 'admin-filtro-status', titulo: 'Filtrar por situação', texto: 'O seletor separa profissionais ativos de inativos. Ativos são os que podem aparecer no totem.' },
    { id: 'admin-acoes-linha', titulo: 'As ações de cada profissional', texto: 'Na linha de cada pessoa você edita os dados cadastrais e administra a foto, guardada em armazenamento privado.' },
    { id: 'admin-vincular-conta', titulo: 'Vincular uma conta de acesso', texto: 'Somente administradores vinculam um profissional a uma conta do sistema. É esse vínculo que permite à pessoa entrar e ver os próprios atendimentos.', somenteRoles: ['ADMINISTRADOR'] },
    { id: 'admin-ativar-desativar', titulo: 'Desativar em vez de excluir', texto: 'Profissionais e salas são desativados, nunca excluídos. O histórico de visitas e locações depende desses registros continuarem existindo.' },
    { id: 'admin-paginacao', titulo: 'Listas longas', texto: 'Quando a lista passa do tamanho de uma página, a navegação aparece no rodapé do painel.' },
    { id: 'admin-onde-esta-ajuda', titulo: 'Onde reencontrar este tutorial', texto: 'A qualquer momento, o botão de ajuda no rodapé da barra lateral abre a central de ajuda, onde você pode reler tudo e refazer o tour.' },
  ],
}
```

- [ ] **Step 7: Escreva o registro**

`src/help/content/index.ts`:

```ts
import { trilhaAdmin } from './admin'
import { trilhaCliente } from './cliente'
import { trilhaProfissional } from './profissional'
import type { Passo, Trilha, TrilhaId } from './types'

export type { Passo, Trilha, TrilhaId }

export const trilhas: Trilha[] = [trilhaAdmin, trilhaProfissional, trilhaCliente]

const trilhaPorRole: Record<string, TrilhaId> = {
  ADMINISTRADOR: 'admin',
  GERENTE: 'admin',
  PROFISSIONAL: 'profissional',
}

export function trilhaPorId(id: TrilhaId) {
  return trilhas.find(trilha => trilha.id === id)
}

export function trilhaDaRole(roles: string[]) {
  for (const role of roles) {
    const id = trilhaPorRole[role]
    if (id) return trilhaPorId(id)
  }
  return undefined
}

export function trilhasPublicas() {
  return trilhas.filter(trilha => trilha.publica)
}

export function passosVisiveis(trilha: Trilha, roles: string[]): Passo[] {
  return trilha.passos.filter(passo => !passo.somenteRoles || passo.somenteRoles.some(role => roles.includes(role)))
}
```

- [ ] **Step 8: Rode o teste para confirmar que passa**

```bash
npm test -- --run src/help/content.test.ts
```

Esperado: PASS, 7 testes.

- [ ] **Step 9: Commit**

```bash
git add recepcaototem/ClientApp/src/help/content recepcaototem/ClientApp/src/help/content.test.ts
git commit -m "feat: add tutorial content model and reading tracks" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: Âncoras `data-tour` e a trilha ancorada

**Files:**
- Modify: `recepcaototem/ClientApp/src/components/AdminLayout.tsx` (elemento `<nav className="admin-nav">`)
- Modify: `recepcaototem/ClientApp/src/pages/admin/Rooms.tsx:73-75`
- Modify: `recepcaototem/ClientApp/src/pages/admin/Professionals.tsx:127-141`
- Modify: `recepcaototem/ClientApp/src/help/content/admin.ts`
- Test: `recepcaototem/ClientApp/src/help/anchors.test.ts`

**Interfaces:**
- Consumes: `trilhas`, `Trilha` da Task 1.
- Produces: os valores `data-tour` que os passos apontam: `nav-lateral`, `nova-sala`, `busca-salas`, `novo-profissional`, `busca-profissionais`, `filtro-status-profissionais`, `acoes-profissional`, `paginacao-profissionais`. O valor `ajuda` é criado na Task 7 — por isso o passo `admin-onde-esta-ajuda` **permanece sem `alvo` nesta tarefa**.

Dois pontos que valem saber antes de editar. `acoes-profissional` fica no `<div className="row-actions">`, que se repete por linha da tabela: o resolvedor usa `querySelector` e pega a primeira ocorrência, o que é o comportamento desejado. E `paginacao-profissionais` só existe quando a lista passa de uma página; quando não existe, o passo é pulado pela regra de alvo ausente da Task 5.

- [ ] **Step 1: Escreva o teste falhando**

`src/help/anchors.test.ts`:

```ts
import { expect, test } from 'vitest'
import adminLayoutSource from '../components/AdminLayout.tsx?raw'
import appSource from '../App.tsx?raw'
import developmentAppSource from '../dev/DevelopmentApp.tsx?raw'
import professionalsSource from '../pages/admin/Professionals.tsx?raw'
import roomsSource from '../pages/admin/Rooms.tsx?raw'
import { trilhas } from './content'

const fontes = [adminLayoutSource, professionalsSource, roomsSource].join('\n')

test('todo alvo declarado existe como data-tour no código das telas', () => {
  for (const trilha of trilhas) {
    for (const passo of trilha.passos) {
      if (!passo.alvo) continue
      expect(fontes, `alvo ${passo.alvo} do passo ${passo.id} não existe nas telas`).toContain(`data-tour="${passo.alvo}"`)
    }
  }
})

test('toda rota declarada está registrada em produção e em desenvolvimento', () => {
  for (const trilha of trilhas) {
    for (const passo of trilha.passos) {
      if (!passo.rota) continue
      const segmento = passo.rota.split('/').filter(Boolean).pop()!
      for (const fonte of [appSource, developmentAppSource]) {
        expect(fonte, `rota ${passo.rota} do passo ${passo.id} não está registrada`).toMatch(new RegExp(`path="/?${segmento}"`))
      }
    }
  }
})

test('a trilha do administrador tem passos ancorados', () => {
  const admin = trilhas.find(trilha => trilha.id === 'admin')!
  expect(admin.passos.filter(passo => passo.alvo).length).toBeGreaterThanOrEqual(7)
})
```

- [ ] **Step 2: Rode o teste para confirmar que falha**

```bash
npm test -- --run src/help/anchors.test.ts
```

Esperado: FAIL no terceiro teste — a trilha do admin ainda não tem passo com `alvo`.

- [ ] **Step 3: Adicione o `data-tour` na navegação**

Em `src/components/AdminLayout.tsx`, no elemento `nav`, acrescente apenas o atributo:

```tsx
        <nav className="admin-nav" data-tour="nav-lateral" aria-label="Navegação administrativa">
```

- [ ] **Step 4: Adicione os `data-tour` na tela de Salas**

Em `src/pages/admin/Rooms.tsx`, no botão do `PageHeader` e no campo de busca:

```tsx
      action={<button className="primary-button" data-tour="nova-sala" onClick={() => setFormRoom(null)}><Plus size={18} /> Nova sala</button>} />
```

```tsx
      <div className="table-toolbar"><div className="search-field" data-tour="busca-salas"><Search size={18} /><input value={rawSearch} onChange={event => { setRawSearch(event.target.value); setPage(1) }} placeholder="Buscar por nome" aria-label="Buscar salas" /></div>
```

- [ ] **Step 5: Adicione os `data-tour` na tela de Profissionais**

Em `src/pages/admin/Professionals.tsx`, quatro atributos. No botão do `PageHeader`:

```tsx
      action={<button className="primary-button" data-tour="novo-profissional" onClick={() => setFormProfessional(null)}><Plus size={18} /> Novo profissional</button>} />
```

No campo de busca e no seletor de status:

```tsx
      <div className="table-toolbar"><div className="search-field" data-tour="busca-profissionais"><Search size={18} /><input value={rawSearch} onChange={event => { setRawSearch(event.target.value); setPage(1) }} placeholder="Buscar por nome ou profissão" aria-label="Buscar profissionais" /></div>
        <select className="field-input compact-select" data-tour="filtro-status-profissionais" value={status} aria-label="Status dos profissionais" onChange={event => { setStatus(event.target.value as ModuleStatus); setPage(1) }}>
```

No contêiner de ações da linha:

```tsx
<td><div className="row-actions" data-tour="acoes-profissional">
```

E na paginação:

```tsx
      {result.totalCount > pageSize && <div className="pagination" data-tour="paginacao-profissionais">
```

- [ ] **Step 6: Ancore os passos da trilha do administrador**

Em `src/help/content/admin.ts`, acrescente `alvo` e `rota` aos passos existentes, sem mudar `id`, `titulo` nem `texto`:

```ts
    { id: 'admin-navegacao', alvo: 'nav-lateral', rota: '/admin', titulo: 'A navegação', texto: 'Por aqui você alcança as áreas do sistema. Gestão reúne o dia a dia do edifício; Preferências guarda as configurações.' },
    { id: 'admin-modulos-em-construcao', titulo: 'O que já está disponível', texto: 'Salas e Profissionais já operam com dados reais. Visão geral, Locações, Visitas e Configurações ainda estão em construção e mostram um aviso ao serem abertas.' },
    { id: 'admin-nova-sala', alvo: 'nova-sala', rota: '/admin/salas', titulo: 'Cadastrar uma sala', texto: 'Cada sala tem nome, descrição e as tarifas por hora e por diária. As tarifas são informadas em reais.' },
    { id: 'admin-busca-salas', alvo: 'busca-salas', rota: '/admin/salas', titulo: 'Encontrar uma sala', texto: 'A busca filtra por nome, e o seletor ao lado mostra apenas salas ativas ou inativas.' },
    { id: 'admin-novo-profissional', alvo: 'novo-profissional', rota: '/admin/profissionais', titulo: 'Cadastrar um profissional', texto: 'O cadastro reúne nome, profissão e WhatsApp. O número é normalizado pela API no padrão internacional.' },
    { id: 'admin-busca-profissionais', alvo: 'busca-profissionais', rota: '/admin/profissionais', titulo: 'Encontrar um profissional', texto: 'A busca aceita nome ou profissão.' },
    { id: 'admin-filtro-status', alvo: 'filtro-status-profissionais', rota: '/admin/profissionais', titulo: 'Filtrar por situação', texto: 'O seletor separa profissionais ativos de inativos. Ativos são os que podem aparecer no totem.' },
    { id: 'admin-acoes-linha', alvo: 'acoes-profissional', rota: '/admin/profissionais', titulo: 'As ações de cada profissional', texto: 'Na linha de cada pessoa você edita os dados cadastrais e administra a foto, guardada em armazenamento privado.' },
    { id: 'admin-vincular-conta', alvo: 'acoes-profissional', rota: '/admin/profissionais', titulo: 'Vincular uma conta de acesso', texto: 'Somente administradores vinculam um profissional a uma conta do sistema. É esse vínculo que permite à pessoa entrar e ver os próprios atendimentos.', somenteRoles: ['ADMINISTRADOR'] },
    { id: 'admin-ativar-desativar', alvo: 'acoes-profissional', rota: '/admin/profissionais', titulo: 'Desativar em vez de excluir', texto: 'Profissionais e salas são desativados, nunca excluídos. O histórico de visitas e locações depende desses registros continuarem existindo.' },
    { id: 'admin-paginacao', alvo: 'paginacao-profissionais', rota: '/admin/profissionais', titulo: 'Listas longas', texto: 'Quando a lista passa do tamanho de uma página, a navegação aparece no rodapé do painel.' },
```

- [ ] **Step 7: Rode os testes de conteúdo e de âncoras**

```bash
npm test -- --run src/help/anchors.test.ts src/help/content.test.ts
```

Esperado: PASS nos dois arquivos.

- [ ] **Step 8: Confirme que as telas existentes não regrediram**

```bash
npm test -- --run src/pages/admin/Rooms.test.tsx src/pages/admin/Professionals.test.tsx src/production-isolation.test.tsx
```

Esperado: PASS. `data-tour` é atributo inerte e não altera nenhuma asserção.

- [ ] **Step 9: Commit**

```bash
git add recepcaototem/ClientApp/src/help/anchors.test.ts recepcaototem/ClientApp/src/help/content/admin.ts recepcaototem/ClientApp/src/components/AdminLayout.tsx recepcaototem/ClientApp/src/pages/admin/Rooms.tsx recepcaototem/ClientApp/src/pages/admin/Professionals.tsx
git commit -m "feat: anchor admin tutorial steps to screen elements" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: Persistência do progresso

**Files:**
- Create: `recepcaototem/ClientApp/src/help/tourStorage.ts`
- Test: `recepcaototem/ClientApp/src/help/tourStorage.test.ts`

**Interfaces:**
- Consumes: `TrilhaId` da Task 1.
- Produces: `type ProgressoTour = { estado: 'concluido' | 'pulado'; ultimoPasso: string; versao: number }`; `type TourProgressStore = { ler(id: TrilhaId): ProgressoTour | null; gravar(id: TrilhaId, progresso: ProgressoTour): void; limpar(id: TrilhaId): void }`; `chaveProgresso(id: TrilhaId): string`; `criarTourProgressStore(): TourProgressStore`.

- [ ] **Step 1: Escreva o teste falhando**

`src/help/tourStorage.test.ts`:

```ts
import { expect, test, vi } from 'vitest'
import { chaveProgresso, criarTourProgressStore } from './tourStorage'

test('a chave segue o formato acordado e não usa o prefixo proibido', () => {
  expect(chaveProgresso('admin')).toBe('lumis.tour.admin.v1')
  expect(chaveProgresso('cliente')).not.toContain('atrium_')
})

test('grava e lê o progresso de uma trilha', () => {
  const store = criarTourProgressStore()
  store.gravar('admin', { estado: 'concluido', ultimoPasso: 'admin-navegacao', versao: 1 })
  expect(store.ler('admin')).toEqual({ estado: 'concluido', ultimoPasso: 'admin-navegacao', versao: 1 })
})

test('trilha sem progresso lê nulo', () => {
  expect(criarTourProgressStore().ler('profissional')).toBeNull()
})

test('limpar remove o progresso', () => {
  const store = criarTourProgressStore()
  store.gravar('admin', { estado: 'pulado', ultimoPasso: 'admin-navegacao', versao: 1 })
  store.limpar('admin')
  expect(store.ler('admin')).toBeNull()
})

test('conteúdo corrompido lê nulo em vez de lançar', () => {
  localStorage.setItem(chaveProgresso('admin'), 'não é json')
  expect(criarTourProgressStore().ler('admin')).toBeNull()
})

test('segue funcionando em memória quando o localStorage lança', () => {
  vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('bloqueado') })
  vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('bloqueado') })
  const store = criarTourProgressStore()
  store.gravar('admin', { estado: 'pulado', ultimoPasso: 'admin-navegacao', versao: 1 })
  expect(store.ler('admin')).toEqual({ estado: 'pulado', ultimoPasso: 'admin-navegacao', versao: 1 })
})
```

- [ ] **Step 2: Rode o teste para confirmar que falha**

```bash
npm test -- --run src/help/tourStorage.test.ts
```

Esperado: FAIL — não resolve `./tourStorage`.

- [ ] **Step 3: Escreva a implementação**

`src/help/tourStorage.ts`:

```ts
import type { TrilhaId } from './content'

export type ProgressoTour = { estado: 'concluido' | 'pulado'; ultimoPasso: string; versao: number }

export type TourProgressStore = {
  ler(id: TrilhaId): ProgressoTour | null
  gravar(id: TrilhaId, progresso: ProgressoTour): void
  limpar(id: TrilhaId): void
}

export function chaveProgresso(id: TrilhaId) {
  return `lumis.tour.${id}.v1`
}

function valido(valor: unknown): valor is ProgressoTour {
  if (typeof valor !== 'object' || valor === null) return false
  const candidato = valor as Partial<ProgressoTour>
  return (candidato.estado === 'concluido' || candidato.estado === 'pulado') && typeof candidato.ultimoPasso === 'string' && typeof candidato.versao === 'number'
}

export function criarTourProgressStore(): TourProgressStore {
  const memoria = new Map<TrilhaId, ProgressoTour>()
  return {
    ler(id) {
      try {
        const bruto = localStorage.getItem(chaveProgresso(id))
        if (bruto === null) return memoria.get(id) ?? null
        const analisado = JSON.parse(bruto) as unknown
        return valido(analisado) ? analisado : null
      } catch { return memoria.get(id) ?? null }
    },
    gravar(id, progresso) {
      memoria.set(id, progresso)
      try { localStorage.setItem(chaveProgresso(id), JSON.stringify(progresso)) } catch { /* armazenamento indisponível */ }
    },
    limpar(id) {
      memoria.delete(id)
      try { localStorage.removeItem(chaveProgresso(id)) } catch { /* armazenamento indisponível */ }
    },
  }
}
```

- [ ] **Step 4: Rode o teste para confirmar que passa**

```bash
npm test -- --run src/help/tourStorage.test.ts
```

Esperado: PASS, 6 testes.

- [ ] **Step 5: Commit**

```bash
git add recepcaototem/ClientApp/src/help/tourStorage.ts recepcaototem/ClientApp/src/help/tourStorage.test.ts
git commit -m "feat: persist tutorial progress behind a store interface" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: Resolução e medição do alvo

**Files:**
- Create: `recepcaototem/ClientApp/src/help/useTourTarget.ts`
- Test: `recepcaototem/ClientApp/src/help/useTourTarget.test.ts`

**Interfaces:**
- Consumes: nada.
- Produces: `type Retangulo = { top: number; left: number; width: number; height: number }`; `aguardarAlvo(alvo: string, limite?: number): Promise<HTMLElement | null>`; `useTourTarget(alvo: string | undefined): { elemento: HTMLElement | null; retangulo: Retangulo | null; procurando: boolean }`.

`ResizeObserver` não existe em jsdom e `scrollIntoView` não é implementado, por isso os dois são chamados de forma condicional. Isso mantém `src/test/setup.ts` intocado, o que importa num worktree compartilhado.

- [ ] **Step 1: Escreva o teste falhando**

`src/help/useTourTarget.test.ts`:

```ts
import { expect, test, vi } from 'vitest'
import { aguardarAlvo } from './useTourTarget'

test('encontra um alvo que já está no documento', async () => {
  document.body.innerHTML = '<button data-tour="nova-sala">Nova sala</button>'
  await expect(aguardarAlvo('nova-sala')).resolves.toBeInstanceOf(HTMLElement)
})

test('encontra um alvo que aparece depois', async () => {
  document.body.innerHTML = ''
  const promessa = aguardarAlvo('busca-salas', 1000)
  setTimeout(() => { document.body.innerHTML = '<div data-tour="busca-salas"></div>' }, 20)
  await expect(promessa).resolves.toBeInstanceOf(HTMLElement)
})

test('resolve nulo quando o alvo nunca aparece', async () => {
  document.body.innerHTML = ''
  await expect(aguardarAlvo('inexistente', 30)).resolves.toBeNull()
})

test('devolve a primeira ocorrência quando o alvo se repete', async () => {
  document.body.innerHTML = '<div data-tour="acoes-profissional" id="um"></div><div data-tour="acoes-profissional" id="dois"></div>'
  const encontrado = await aguardarAlvo('acoes-profissional')
  expect(encontrado?.id).toBe('um')
})

test('não vaza observadores após encontrar o alvo', async () => {
  const desconectar = vi.fn()
  vi.spyOn(globalThis, 'MutationObserver').mockImplementation(() => ({ observe: vi.fn(), disconnect: desconectar, takeRecords: vi.fn() }) as unknown as MutationObserver)
  document.body.innerHTML = ''
  await aguardarAlvo('inexistente', 20)
  expect(desconectar).toHaveBeenCalled()
})
```

- [ ] **Step 2: Rode o teste para confirmar que falha**

```bash
npm test -- --run src/help/useTourTarget.test.ts
```

Esperado: FAIL — não resolve `./useTourTarget`.

- [ ] **Step 3: Escreva a implementação**

`src/help/useTourTarget.ts`:

```ts
import { useEffect, useState } from 'react'

export type Retangulo = { top: number; left: number; width: number; height: number }

export const limiteAlvoEmMs = 1500

export function aguardarAlvo(alvo: string, limite = limiteAlvoEmMs) {
  const seletor = `[data-tour="${alvo}"]`
  return new Promise<HTMLElement | null>(resolve => {
    const imediato = document.querySelector<HTMLElement>(seletor)
    if (imediato) return resolve(imediato)
    const observador = new MutationObserver(() => {
      const encontrado = document.querySelector<HTMLElement>(seletor)
      if (!encontrado) return
      observador.disconnect()
      clearTimeout(prazo)
      resolve(encontrado)
    })
    const prazo = setTimeout(() => { observador.disconnect(); resolve(null) }, limite)
    observador.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['data-tour'] })
  })
}

export function useTourTarget(alvo: string | undefined) {
  const [elemento, setElemento] = useState<HTMLElement | null>(null)
  const [retangulo, setRetangulo] = useState<Retangulo | null>(null)
  const [procurando, setProcurando] = useState(false)

  useEffect(() => {
    setElemento(null)
    setRetangulo(null)
    if (!alvo) { setProcurando(false); return }
    let cancelado = false
    setProcurando(true)
    void aguardarAlvo(alvo).then(encontrado => {
      if (cancelado) return
      setElemento(encontrado)
      setProcurando(false)
    })
    return () => { cancelado = true }
  }, [alvo])

  useEffect(() => {
    if (!elemento) return
    const medir = () => {
      const caixa = elemento.getBoundingClientRect()
      setRetangulo({ top: caixa.top, left: caixa.left, width: caixa.width, height: caixa.height })
    }
    elemento.scrollIntoView?.({ block: 'center' })
    medir()
    const observador = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(medir)
    observador?.observe(elemento)
    window.addEventListener('resize', medir)
    window.addEventListener('scroll', medir, true)
    return () => {
      observador?.disconnect()
      window.removeEventListener('resize', medir)
      window.removeEventListener('scroll', medir, true)
    }
  }, [elemento])

  return { elemento, retangulo, procurando }
}
```

- [ ] **Step 4: Rode o teste para confirmar que passa**

```bash
npm test -- --run src/help/useTourTarget.test.ts
```

Esperado: PASS, 5 testes.

- [ ] **Step 5: Commit**

```bash
git add recepcaototem/ClientApp/src/help/useTourTarget.ts recepcaototem/ClientApp/src/help/useTourTarget.test.ts
git commit -m "feat: resolve and measure tutorial step targets" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 5: Estado do tour

**Files:**
- Create: `recepcaototem/ClientApp/src/help/TourProvider.tsx`
- Modify: `recepcaototem/ClientApp/src/main.tsx`
- Test: `recepcaototem/ClientApp/src/help/TourProvider.test.tsx`

**Interfaces:**
- Consumes: `trilhaDaRole`, `passosVisiveis`, `Passo`, `Trilha`, `TrilhaId` da Task 1; `TourProgressStore`, `criarTourProgressStore` da Task 3; `useSession` de `../auth/SessionProvider`.
- Produces: `type TourContextValue = { trilha: Trilha | null; passo: Passo | null; indice: number; total: number; avancar(): void; voltar(): void; pular(): void; reiniciar(id: TrilhaId): void }`; `TourProvider({ children, store }: { children: ReactNode; store?: TourProgressStore })`; `useTour(): TourContextValue`.

Nesta tarefa o provider ainda não renderiza overlay nenhum — só administra estado. O `TourOverlay` entra na Task 6. Só passos com `alvo` entram no tour; passos de leitura ficam para a central de ajuda, conforme a spec.

- [ ] **Step 1: Escreva o teste falhando**

`src/help/TourProvider.test.tsx`:

```tsx
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { expect, test, vi } from 'vitest'
import { SessionProvider } from '../auth/SessionProvider'
import { TourProvider, useTour } from './TourProvider'
import { criarTourProgressStore } from './tourStorage'

function sessaoDe(roles: string[], mustChangePassword = false) {
  return vi.fn().mockResolvedValue(new Response(JSON.stringify({
    userId: 'u1', displayName: 'Admin Real', email: 'admin@lumis.test', roles, mustChangePassword,
  }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
}

function Espiao() {
  const tour = useTour()
  return <div>{tour.trilha ? `${tour.trilha.id}:${tour.passo?.id}:${tour.indice + 1}/${tour.total}` : 'sem-tour'}</div>
}

function montar(rota = '/admin') {
  return render(
    <MemoryRouter initialEntries={[rota]}>
      <SessionProvider><TourProvider><Espiao /></TourProvider></SessionProvider>
    </MemoryRouter>,
  )
}

test('inicia a trilha do administrador no primeiro passo ancorado', async () => {
  vi.stubGlobal('fetch', sessaoDe(['ADMINISTRADOR']))
  montar()
  await waitFor(() => expect(screen.getByText(/^admin:admin-navegacao:1\//)).toBeInTheDocument())
})

test('não inicia para usuário anônimo', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 401 })))
  montar()
  await waitFor(() => expect(screen.getByText('sem-tour')).toBeInTheDocument())
})

test('não inicia durante troca de senha obrigatória', async () => {
  vi.stubGlobal('fetch', sessaoDe(['ADMINISTRADOR'], true))
  montar()
  await waitFor(() => expect(screen.getByText('sem-tour')).toBeInTheDocument())
})

test('não inicia na central de ajuda', async () => {
  vi.stubGlobal('fetch', sessaoDe(['ADMINISTRADOR']))
  montar('/ajuda')
  await waitFor(() => expect(screen.getByText('sem-tour')).toBeInTheDocument())
})

test('não reinicia quando a trilha já foi concluída na mesma versão', async () => {
  const store = criarTourProgressStore()
  store.gravar('admin', { estado: 'concluido', ultimoPasso: 'admin-paginacao', versao: 1 })
  vi.stubGlobal('fetch', sessaoDe(['ADMINISTRADOR']))
  render(
    <MemoryRouter initialEntries={['/admin']}>
      <SessionProvider><TourProvider store={store}><Espiao /></TourProvider></SessionProvider>
    </MemoryRouter>,
  )
  await waitFor(() => expect(screen.getByText('sem-tour')).toBeInTheDocument())
})

test('reinicia quando o progresso salvo é de uma versão anterior', async () => {
  const store = criarTourProgressStore()
  store.gravar('admin', { estado: 'concluido', ultimoPasso: 'admin-paginacao', versao: 0 })
  vi.stubGlobal('fetch', sessaoDe(['ADMINISTRADOR']))
  render(
    <MemoryRouter initialEntries={['/admin']}>
      <SessionProvider><TourProvider store={store}><Espiao /></TourProvider></SessionProvider>
    </MemoryRouter>,
  )
  await waitFor(() => expect(screen.getByText(/^admin:admin-navegacao/)).toBeInTheDocument())
})

test('o gerente não recebe o passo restrito a administradores', async () => {
  vi.stubGlobal('fetch', sessaoDe(['GERENTE']))
  montar()
  await waitFor(() => expect(screen.getByText(/^admin:/)).toBeInTheDocument())
  const total = Number(screen.getByText(/^admin:/).textContent!.split('/')[1])
  vi.stubGlobal('fetch', sessaoDe(['ADMINISTRADOR']))
  const administrador = render(
    <MemoryRouter initialEntries={['/admin']}>
      <SessionProvider><TourProvider><Espiao /></TourProvider></SessionProvider>
    </MemoryRouter>,
  )
  await waitFor(() => expect(administrador.getByText(/^admin:/)).toBeInTheDocument())
  expect(Number(administrador.getByText(/^admin:/).textContent!.split('/')[1])).toBe(total + 1)
})

test('o profissional não recebe tour, porque a trilha dele não tem passos ancorados', async () => {
  vi.stubGlobal('fetch', sessaoDe(['PROFISSIONAL']))
  montar()
  await waitFor(() => expect(screen.getByText('sem-tour')).toBeInTheDocument())
})
```

- [ ] **Step 2: Rode o teste para confirmar que falha**

```bash
npm test -- --run src/help/TourProvider.test.tsx
```

Esperado: FAIL — não resolve `./TourProvider`.

- [ ] **Step 3: Escreva o provider**

`src/help/TourProvider.tsx`:

```tsx
import { createContext, type ReactNode, useContext, useEffect, useMemo, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { useSession } from '../auth/SessionProvider'
import { passosVisiveis, type Passo, type Trilha, type TrilhaId, trilhaDaRole } from './content'
import { criarTourProgressStore, type TourProgressStore } from './tourStorage'

type TourContextValue = {
  trilha: Trilha | null
  passo: Passo | null
  indice: number
  total: number
  avancar(): void
  voltar(): void
  pular(): void
  reiniciar(id: TrilhaId): void
}

const rotasSemTour = ['/login', '/change-password', '/ajuda']
const TourContext = createContext<TourContextValue | null>(null)

export function TourProvider({ children, store }: { children: ReactNode; store?: TourProgressStore }) {
  const session = useSession()
  const location = useLocation()
  const navigate = useNavigate()
  const progresso = useMemo(() => store ?? criarTourProgressStore(), [store])
  const [trilha, setTrilha] = useState<Trilha | null>(null)
  const [indice, setIndice] = useState(0)

  const roles = session.user?.roles ?? []
  const candidata = useMemo(() => trilhaDaRole(roles), [roles.join(',')])
  const passos = useMemo(() => (trilha ? passosVisiveis(trilha, roles).filter(passo => passo.alvo) : []), [trilha, roles.join(',')])
  const passo = passos[indice] ?? null

  useEffect(() => {
    if (trilha) return
    if (session.status !== 'authenticated' || !candidata) return
    if (rotasSemTour.some(rota => location.pathname.startsWith(rota))) return
    if (passosVisiveis(candidata, roles).filter(item => item.alvo).length === 0) return
    const salvo = progresso.ler(candidata.id)
    if (salvo && salvo.versao === candidata.versao) return
    setTrilha(candidata)
    setIndice(0)
  }, [session.status, candidata, location.pathname, trilha])

  useEffect(() => {
    if (!passo?.rota || passo.rota === location.pathname) return
    navigate(passo.rota)
  }, [passo?.id])

  const encerrar = (estado: 'concluido' | 'pulado') => {
    if (trilha && passo) progresso.gravar(trilha.id, { estado, ultimoPasso: passo.id, versao: trilha.versao })
    setTrilha(null)
    setIndice(0)
  }

  const value: TourContextValue = {
    trilha,
    passo,
    indice,
    total: passos.length,
    avancar: () => { if (indice + 1 >= passos.length) encerrar('concluido'); else setIndice(valor => valor + 1) },
    voltar: () => setIndice(valor => Math.max(0, valor - 1)),
    pular: () => encerrar('pulado'),
    reiniciar: (id) => { progresso.limpar(id); setTrilha(null); setIndice(0); navigate('/admin') },
  }

  return <TourContext.Provider value={value}>{children}</TourContext.Provider>
}

export function useTour() {
  const value = useContext(TourContext)
  if (!value) throw new Error('useTour must be used inside TourProvider')
  return value
}
```

- [ ] **Step 4: Rode o teste para confirmar que passa**

```bash
npm test -- --run src/help/TourProvider.test.tsx
```

Esperado: PASS, 8 testes.

- [ ] **Step 5: Monte o provider na aplicação**

`src/main.tsx`, acrescentando apenas o import e o nível novo:

```tsx
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import { App } from './App'
import { SessionProvider } from './auth/SessionProvider'
import { TourProvider } from './help/TourProvider'
import './styles.css'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <SessionProvider><TourProvider><App /></TourProvider></SessionProvider>
    </BrowserRouter>
  </StrictMode>,
)
```

- [ ] **Step 6: Rode a suíte inteira**

```bash
npm test -- --run
```

Esperado: PASS em todos os arquivos.

- [ ] **Step 7: Commit**

```bash
git add recepcaototem/ClientApp/src/help/TourProvider.tsx recepcaototem/ClientApp/src/help/TourProvider.test.tsx recepcaototem/ClientApp/src/main.tsx
git commit -m "feat: drive tutorial tour state per role" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 6: Spotlight, balão e teclado

**Files:**
- Create: `recepcaototem/ClientApp/src/help/TourOverlay.tsx`
- Modify: `recepcaototem/ClientApp/src/help/TourProvider.tsx` (renderizar o overlay)
- Modify: `recepcaototem/ClientApp/src/styles.css` (fim do arquivo)
- Test: `recepcaototem/ClientApp/src/help/TourOverlay.test.tsx`

**Interfaces:**
- Consumes: `useTour` da Task 5; `useTourTarget` e `Retangulo` da Task 4.
- Produces: `TourOverlay()`, renderizado uma única vez pelo `TourProvider`.

- [ ] **Step 1: Escreva o teste falhando**

`src/help/TourOverlay.test.tsx`:

```tsx
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { expect, test, vi } from 'vitest'
import { SessionProvider } from '../auth/SessionProvider'
import { TourProvider } from './TourProvider'
import { criarTourProgressStore } from './tourStorage'

function sessaoAdmin() {
  return vi.fn().mockResolvedValue(new Response(JSON.stringify({
    userId: 'u1', displayName: 'Admin Real', email: 'admin@lumis.test', roles: ['ADMINISTRADOR'], mustChangePassword: false,
  }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
}

function montar(store = criarTourProgressStore()) {
  return render(
    <MemoryRouter initialEntries={['/admin']}>
      <SessionProvider>
        <TourProvider store={store}>
          <nav data-tour="nav-lateral">Navegação</nav>
          <button data-tour="nova-sala">Nova sala</button>
        </TourProvider>
      </SessionProvider>
    </MemoryRouter>,
  )
}

test('mostra o primeiro passo como diálogo acessível', async () => {
  vi.stubGlobal('fetch', sessaoAdmin())
  montar()
  const dialogo = await waitFor(() => screen.getByRole('dialog'))
  expect(dialogo).toHaveAttribute('aria-modal', 'true')
  expect(screen.getByText('A navegação')).toBeInTheDocument()
})

test('avança e mostra o progresso', async () => {
  vi.stubGlobal('fetch', sessaoAdmin())
  montar()
  await waitFor(() => screen.getByRole('dialog'))
  expect(screen.getByText(/passo 1 de/i)).toBeInTheDocument()
  await userEvent.click(screen.getByRole('button', { name: 'Avançar' }))
  await waitFor(() => expect(screen.getByText(/passo 2 de/i)).toBeInTheDocument())
})

test('pular encerra o tour e grava o progresso', async () => {
  const store = criarTourProgressStore()
  vi.stubGlobal('fetch', sessaoAdmin())
  montar(store)
  await waitFor(() => screen.getByRole('dialog'))
  await userEvent.click(screen.getByRole('button', { name: 'Pular tutorial' }))
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(store.ler('admin')?.estado).toBe('pulado')
})

test('Escape encerra o tour', async () => {
  const store = criarTourProgressStore()
  vi.stubGlobal('fetch', sessaoAdmin())
  montar(store)
  await waitFor(() => screen.getByRole('dialog'))
  await userEvent.keyboard('{Escape}')
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(store.ler('admin')?.estado).toBe('pulado')
})

test('pula o passo cujo alvo não existe na tela', async () => {
  vi.stubGlobal('fetch', sessaoAdmin())
  render(
    <MemoryRouter initialEntries={['/admin']}>
      <SessionProvider>
        <TourProvider>
          <button data-tour="nova-sala">Nova sala</button>
        </TourProvider>
      </SessionProvider>
    </MemoryRouter>,
  )
  await waitFor(() => expect(screen.getByText('Cadastrar uma sala')).toBeInTheDocument(), { timeout: 4000 })
})
```

`userEvent` vem de `@testing-library/user-event`, que já está na árvore como dependência de `@testing-library/react`. Se o import falhar, use `fireEvent` de `@testing-library/react` no lugar — **não** adicione dependência.

- [ ] **Step 2: Rode o teste para confirmar que falha**

```bash
npm test -- --run src/help/TourOverlay.test.tsx
```

Esperado: FAIL — nenhum `dialog` é renderizado.

- [ ] **Step 3: Escreva o overlay**

`src/help/TourOverlay.tsx`:

```tsx
import { useEffect, useRef } from 'react'
import { useTour } from './TourProvider'
import { type Retangulo, useTourTarget } from './useTourTarget'

const margem = 14

function posicionar(retangulo: Retangulo) {
  const largura = Math.min(360, window.innerWidth - 32)
  const abaixo = retangulo.top + retangulo.height + margem
  const cabeAbaixo = abaixo + 200 < window.innerHeight
  const top = cabeAbaixo ? abaixo : Math.max(16, retangulo.top - 200 - margem)
  const left = Math.min(Math.max(16, retangulo.left), Math.max(16, window.innerWidth - largura - 16))
  return { top, left, width: largura }
}

export function TourOverlay() {
  const { trilha, passo, indice, total, avancar, voltar, pular } = useTour()
  const { retangulo, procurando } = useTourTarget(passo?.alvo)
  const cartao = useRef<HTMLDivElement | null>(null)
  const ausente = Boolean(passo) && !procurando && retangulo === null

  useEffect(() => { if (ausente) avancar() }, [ausente, passo?.id])

  useEffect(() => {
    if (!trilha) return
    const teclado = (evento: KeyboardEvent) => {
      if (evento.key === 'Escape') { evento.preventDefault(); pular() }
      if (evento.key === 'ArrowRight') { evento.preventDefault(); avancar() }
      if (evento.key === 'ArrowLeft') { evento.preventDefault(); voltar() }
      if (evento.key !== 'Tab' || !cartao.current) return
      const foco = cartao.current.querySelectorAll<HTMLElement>('button')
      if (foco.length === 0) return
      const primeiro = foco[0]
      const ultimo = foco[foco.length - 1]
      if (!evento.shiftKey && document.activeElement === ultimo) { evento.preventDefault(); primeiro.focus() }
      if (evento.shiftKey && document.activeElement === primeiro) { evento.preventDefault(); ultimo.focus() }
    }
    document.addEventListener('keydown', teclado)
    return () => document.removeEventListener('keydown', teclado)
  }, [trilha, passo?.id])

  useEffect(() => { cartao.current?.focus() }, [passo?.id])

  if (!trilha || !passo || !retangulo) return null
  const caixa = posicionar(retangulo)
  const ultimo = indice + 1 >= total

  return (
    <div className="tour-overlay">
      <div className="tour-spotlight" style={{ top: retangulo.top - 6, left: retangulo.left - 6, width: retangulo.width + 12, height: retangulo.height + 12 }} />
      <div className="tour-card" ref={cartao} tabIndex={-1} role="dialog" aria-modal="true" aria-labelledby="tour-titulo" style={caixa}>
        <span className="tour-progresso" aria-live="polite">Passo {indice + 1} de {total}</span>
        <h2 id="tour-titulo">{passo.titulo}</h2>
        <p>{passo.texto}</p>
        <div className="tour-acoes">
          <button className="ghost-button" type="button" onClick={pular}>Pular tutorial</button>
          <div className="tour-navegacao">
            {indice > 0 && <button className="secondary-button" type="button" onClick={voltar}>Voltar</button>}
            <button className="primary-button" type="button" onClick={avancar}>{ultimo ? 'Concluir' : 'Avançar'}</button>
          </div>
        </div>
      </div>
    </div>
  )
}
```

- [ ] **Step 4: Renderize o overlay no provider**

Em `src/help/TourProvider.tsx`, importe o overlay e inclua-o no `Provider`:

```tsx
  return <TourContext.Provider value={value}>{children}<TourOverlay /></TourContext.Provider>
```

com `import { TourOverlay } from './TourOverlay'` junto aos outros imports.

- [ ] **Step 5: Escreva os estilos**

No fim de `src/styles.css`, antes do bloco `@media (max-width: 700px)`:

```css
.tour-overlay { position: fixed; inset: 0; z-index: 60; pointer-events: none; }
.tour-spotlight { position: fixed; border-radius: 14px; box-shadow: 0 0 0 9999px rgba(61,61,61,.55); transition: top .18s ease, left .18s ease, width .18s ease, height .18s ease; }
.tour-card { position: fixed; display: grid; gap: 9px; padding: 19px; border: 1px solid var(--line); border-radius: 17px; background: var(--surface); box-shadow: 0 24px 48px rgba(61,61,61,.22); pointer-events: auto; }
.tour-card:focus-visible { outline: 2px solid var(--ink); outline-offset: 3px; }
.tour-progresso { color: var(--muted); font-size: 11px; font-weight: 700; letter-spacing: .04em; text-transform: uppercase; }
.tour-card h2 { margin: 0; color: var(--ink); font-size: 17px; }
.tour-card p { margin: 0; color: #666; font-size: 13px; line-height: 1.5; }
.tour-acoes { display: flex; align-items: center; justify-content: space-between; gap: 10px; margin-top: 5px; }
.tour-navegacao { display: flex; align-items: center; gap: 8px; }
.tour-acoes .ghost-button,.tour-acoes .secondary-button,.tour-acoes .primary-button { min-height: 36px; font-size: 12px; }
```

- [ ] **Step 6: Rode o teste do overlay e a suíte**

```bash
npm test -- --run src/help/TourOverlay.test.tsx
```

Esperado: PASS, 5 testes.

```bash
npm test -- --run
```

Esperado: PASS em todos os arquivos.

- [ ] **Step 7: Commit**

```bash
git add recepcaototem/ClientApp/src/help/TourOverlay.tsx recepcaototem/ClientApp/src/help/TourOverlay.test.tsx recepcaototem/ClientApp/src/help/TourProvider.tsx recepcaototem/ClientApp/src/styles.css
git commit -m "feat: render tutorial spotlight and step card" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 7: Central de ajuda em `/ajuda`

**Files:**
- Create: `recepcaototem/ClientApp/src/help/HelpCenter.tsx`
- Modify: `recepcaototem/ClientApp/src/App.tsx`
- Modify: `recepcaototem/ClientApp/src/dev/DevelopmentApp.tsx`
- Modify: `recepcaototem/ClientApp/src/components/AdminLayout.tsx` (rodapé da barra lateral)
- Modify: `recepcaototem/ClientApp/src/help/content/admin.ts` (ancorar o último passo em `ajuda`)
- Modify: `recepcaototem/ClientApp/src/styles.css`
- Test: `recepcaototem/ClientApp/src/help/HelpCenter.test.tsx`

**Interfaces:**
- Consumes: `trilhas`, `trilhaDaRole`, `trilhasPublicas`, `passosVisiveis` da Task 1; `useTour` da Task 5; `useSession`.
- Produces: `HelpCenter()`; o atributo `data-tour="ajuda"` no link da barra lateral.

- [ ] **Step 1: Escreva o teste falhando**

`src/help/HelpCenter.test.tsx`:

```tsx
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { expect, test, vi } from 'vitest'
import { SessionProvider } from '../auth/SessionProvider'
import { HelpCenter } from './HelpCenter'
import { TourProvider } from './TourProvider'

function montar() {
  return render(
    <MemoryRouter initialEntries={['/ajuda']}>
      <SessionProvider><TourProvider><HelpCenter /></TourProvider></SessionProvider>
    </MemoryRouter>,
  )
}

test('sem sessão mostra as trilhas públicas e não a do administrador', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 401 })))
  montar()
  await waitFor(() => expect(screen.getByText('Para quem vai ser atendido')).toBeInTheDocument())
  expect(screen.getByText('Para profissionais')).toBeInTheDocument()
  expect(screen.queryByText('Para a administração')).not.toBeInTheDocument()
})

test('com sessão de administração mostra a trilha do perfil', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
    userId: 'u1', displayName: 'Admin Real', email: 'admin@lumis.test', roles: ['ADMINISTRADOR'], mustChangePassword: false,
  }), { status: 200, headers: { 'Content-Type': 'application/json' } })))
  montar()
  await waitFor(() => expect(screen.getByText('Para a administração')).toBeInTheDocument())
  expect(screen.getByRole('button', { name: 'Refazer o tour' })).toBeInTheDocument()
})

test('o texto de cada passo da trilha do cliente aparece na página', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 401 })))
  montar()
  await waitFor(() => expect(screen.getByText('Check-in por QR')).toBeInTheDocument())
  expect(screen.getByText('Foto e privacidade')).toBeInTheDocument()
})

test('trilha sem passos ancorados não oferece refazer o tour, mesmo autenticado', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
    userId: 'u2', displayName: 'Profissional Real', email: 'prof@lumis.test', roles: ['PROFISSIONAL'], mustChangePassword: false,
  }), { status: 200, headers: { 'Content-Type': 'application/json' } })))
  montar()
  await waitFor(() => expect(screen.getByText('Para profissionais')).toBeInTheDocument())
  expect(screen.queryByRole('button', { name: 'Refazer o tour' })).not.toBeInTheDocument()
})
```

- [ ] **Step 2: Rode o teste para confirmar que falha**

```bash
npm test -- --run src/help/HelpCenter.test.tsx
```

Esperado: FAIL — não resolve `./HelpCenter`.

- [ ] **Step 3: Escreva a página**

`src/help/HelpCenter.tsx`:

```tsx
import { CircleHelp } from 'lucide-react'
import { Link } from 'react-router-dom'
import { useSession } from '../auth/SessionProvider'
import { passosVisiveis, type Trilha, trilhaDaRole, trilhasPublicas } from './content'
import { useTour } from './TourProvider'

export function HelpCenter() {
  const session = useSession()
  const { reiniciar } = useTour()
  const roles = session.user?.roles ?? []
  const doPerfil = trilhaDaRole(roles)
  const lista: Trilha[] = doPerfil ? [doPerfil, ...trilhasPublicas().filter(trilha => trilha.id !== doPerfil.id)] : trilhasPublicas()

  return (
    <div className="help-page page-enter">
      <header className="help-header">
        <span className="help-icon"><CircleHelp size={22} /></span>
        <div><h1>Central de ajuda</h1><p>Como usar o LUMIS, separado por quem está usando.</p></div>
        {session.status === 'authenticated' && <Link className="secondary-button" to="/admin">Voltar ao painel</Link>}
      </header>
      {lista.map(trilha => {
        const passos = passosVisiveis(trilha, roles)
        const ancorada = passos.some(passo => passo.alvo)
        return (
          <section className="panel help-track" key={trilha.id}>
            <div className="help-track-header">
              <div><h2>{trilha.titulo}</h2><p>{trilha.resumo}</p></div>
              {ancorada && session.status === 'authenticated' && <button className="secondary-button" type="button" onClick={() => reiniciar(trilha.id)}>Refazer o tour</button>}
            </div>
            <ol className="help-steps">
              {passos.map(passo => <li key={passo.id}><strong>{passo.titulo}</strong><p>{passo.texto}</p></li>)}
            </ol>
          </section>
        )
      })}
    </div>
  )
}
```

- [ ] **Step 4: Registre a rota nos dois roteadores**

Em `src/App.tsx`, importe `HelpCenter` de `./help/HelpCenter` e acrescente a rota ao lado de `/login`:

```tsx
      <Route path="/ajuda" element={<HelpCenter />} />
```

Em `src/dev/DevelopmentApp.tsx`, importe `HelpCenter` de `../help/HelpCenter` e acrescente a mesma linha. Sem isso a página não existe no dev server, e o teste de rotas da Task 2 falha.

- [ ] **Step 5: Adicione o acesso na barra lateral**

Em `src/components/AdminLayout.tsx`, importe `CircleHelp` de `lucide-react` e `Link` de `react-router-dom` (junto dos imports existentes), e acrescente o link ao `sidebar-footer`, antes do botão de sair:

```tsx
          <Link className="icon-button" to="/ajuda" data-tour="ajuda" aria-label="Ajuda"><CircleHelp size={18} /></Link>
```

- [ ] **Step 6: Ancore o último passo da trilha do administrador**

Em `src/help/content/admin.ts`, o passo final passa a ter alvo:

```ts
    { id: 'admin-onde-esta-ajuda', alvo: 'ajuda', titulo: 'Onde reencontrar este tutorial', texto: 'A qualquer momento, o botão de ajuda no rodapé da barra lateral abre a central de ajuda, onde você pode reler tudo e refazer o tour.' },
```

- [ ] **Step 7: Escreva os estilos da página**

No fim de `src/styles.css`, antes do bloco `@media (max-width: 700px)`:

```css
.help-page { display: grid; gap: 18px; max-width: 860px; margin: 0 auto; padding: 34px 20px 48px; }
.help-header { display: flex; align-items: center; gap: 14px; }
.help-header > div { flex: 1; }
.help-header h1 { margin: 0; color: var(--ink); font-size: 25px; }
.help-header p { margin: 6px 0 0; color: var(--muted); font-size: 13px; }
.help-icon { width: 46px; height: 46px; display: grid; place-items: center; flex: 0 0 auto; color: var(--ink); background: #ececec; border-radius: 14px; }
.help-track { padding: 22px; }
.help-track-header { display: flex; align-items: flex-start; justify-content: space-between; gap: 14px; }
.help-track-header h2 { margin: 0; color: var(--ink); font-size: 18px; }
.help-track-header p { margin: 5px 0 0; color: var(--muted); font-size: 13px; }
.help-steps { display: grid; gap: 14px; margin: 18px 0 0; padding-inline-start: 22px; }
.help-steps li strong { color: var(--ink); font-size: 14px; font-weight: 650; }
.help-steps li p { margin: 4px 0 0; color: #666; font-size: 13px; line-height: 1.5; }
```

- [ ] **Step 8: Rode a suíte inteira**

```bash
npm test -- --run
```

Esperado: PASS em todos os arquivos, incluindo o teste de rotas da Task 2, que agora encontra `path="/ajuda"` nos dois roteadores.

- [ ] **Step 9: Commit**

```bash
git add recepcaototem/ClientApp/src/help/HelpCenter.tsx recepcaototem/ClientApp/src/help/HelpCenter.test.tsx recepcaototem/ClientApp/src/help/content/admin.ts recepcaototem/ClientApp/src/App.tsx recepcaototem/ClientApp/src/dev/DevelopmentApp.tsx recepcaototem/ClientApp/src/components/AdminLayout.tsx recepcaototem/ClientApp/src/styles.css
git commit -m "feat: add public help center at /ajuda" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 8: Verificação final

**Files:**
- Modify: nenhum, salvo correção de defeito encontrado.

**Interfaces:**
- Consumes: tudo das tarefas anteriores.
- Produces: evidência de que o tutorial funciona e nada regrediu.

- [ ] **Step 1: Rode a suíte completa**

```bash
npm test -- --run
```

Esperado: PASS, zero falhas.

- [ ] **Step 2: Compile com verificação de tipos**

```bash
npm run build
```

Esperado: `tsc -b` sem erro e build do Vite concluído.

- [ ] **Step 3: Verifique o isolamento do bundle de produção**

```bash
npm run verify:production-bundle
```

Esperado: sem erro. Se aparecer reclamação de marcador de storage, a chave foi escrita com o prefixo errado — deve ser `lumis.tour.`, nunca `atrium_`.

- [ ] **Step 4: Confirme que nenhuma dependência entrou**

```bash
git diff --stat main -- recepcaototem/ClientApp/package.json recepcaototem/ClientApp/package-lock.json
```

Esperado: saída vazia.

- [ ] **Step 5: Verifique no navegador**

Suba o dev server e confira, nesta ordem: `/ajuda` abre sem login e mostra as trilhas de Cliente e Profissional; ao entrar como administrador, o tour começa sozinho no primeiro passo; `Avançar` percorre os passos navegando entre Salas e Profissionais; `Escape` encerra; recarregar a página não reapresenta o tour; o botão de ajuda na barra lateral abre `/ajuda` com "Refazer o tour"; em viewport de 375 px o tour não trava, pulando os passos cujos alvos estão na barra lateral oculta.

- [ ] **Step 6: Commit da verificação, se houver correção**

Se algum defeito foi corrigido:

```bash
git add <apenas os arquivos corrigidos>
git commit -m "fix: <o que foi corrigido>" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

Se nada mudou, não há commit nesta tarefa.

---

## Cobertura da spec

| Seção da spec | Tarefa |
|---|---|
| 3. Arquitetura e arquivos | 1, 5, 6, 7 |
| 4. Modelo de conteúdo | 1 |
| 5. As três trilhas | 1, 2, 7 |
| 6. Fluxo do tour: gatilho | 5 |
| 6. Fluxo do tour: navegação por rota | 5 |
| 6. Alvo ausente, 1500 ms | 4, 6 |
| 6. Encerramento e reentrada | 6, 7 |
| 7. Persistência e privacidade | 3 |
| 8. Acessibilidade e teclado | 6 |
| 9. Estilo visual | 6, 7 |
| 10. Testes | todas |
| 12. Fora de escopo | respeitado: nenhuma tarefa toca backend, dependências ou telas além das âncoras e do link de ajuda |

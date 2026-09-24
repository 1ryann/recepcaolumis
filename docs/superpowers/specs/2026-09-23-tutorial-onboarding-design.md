# LUMIS — Tutorial guiado e central de ajuda

**Status:** especificação final para revisão. Nenhuma implementação, dependência nova, alteração de backend ou migration nesta etapa.

**Escopo:** um tour guiado dentro da interface na primeira visita e uma central de ajuda permanente em `/ajuda`, com trilhas separadas para Administrador, Profissional e Cliente.

**Fora do escopo:** backend, entidades, migrations, endpoints, telas novas além de `/ajuda`, vídeos, imagens, internacionalização, analytics e qualquer tour no Totem/Recepção.

## 1. Contexto e estado verificado do código

A inspeção da branch `codex/leases-design` encontrou o seguinte, e o desenho se apoia apenas nisto:

- As roles do sistema são `ADMINISTRADOR`, `GERENTE` e `PROFISSIONAL` (`src/GestaoPredio.Domain/Security/SystemRoles.cs`). Não existe role de cliente.
- `Customer` é somente especificação (`docs/superpowers/specs/2026-09-07-customer-self-scheduling-checkin-design.md`): sem entidade, sem tabela, sem tela, sem conta.
- No bundle de produção (`recepcaototem/ClientApp/src/App.tsx`) as únicas telas reais são **Salas** e **Profissionais**. Visão geral, Locações, Visitas, Configurações e Recepção renderizam `ModuleUnavailable`.
- Não existe área de Profissional no frontend.
- `SessionProvider` já expõe `status`, `user.roles`, `user.displayName` e `mustChangePassword` ao frontend.
- `recepcaototem/ClientApp/src/styles.css` tem 840 linhas escritas à mão, com variáveis `--ink`, `--muted`, `--line`, `--surface`. O Tailwind está importado, mas as telas usam classes próprias.
- `Modal.tsx` já estabelece o contrato de diálogo do projeto: backdrop, `Escape` para fechar, `role="dialog"`, `aria-modal="true"`, `aria-labelledby`.
- `production-isolation.test.tsx` lê arquivos-fonte com `?raw` para provar invariantes; `scripts/verify-production-bundle.mjs` proíbe no bundle os marcadores de storage `atrium_*` e referências a `src/dev/`, `AppStore` e `src/data/mock`.

Consequência direta: só a trilha do Administrador pode ter passos ancorados em elementos reais hoje. As trilhas de Profissional e Cliente existem como conteúdo de leitura até que suas áreas sejam implementadas.

## 2. Decisões tomadas

| Decisão | Escolha |
|---|---|
| Formato | Tour guiado na primeira visita **e** central de ajuda para reler |
| Escopo inicial | Motor completo, com conteúdo para os três perfis; Profissional e Cliente em texto conceitual |
| Onde vive o conteúdo | Arquivos TypeScript versionados no `ClientApp`, sem banco |
| Progresso do tour | `localStorage` agora, atrás de uma interface, para migrar ao perfil do usuário depois |
| Acesso do Cliente | Central de ajuda pública em `/ajuda`, sem login |
| Motor | Implementação própria, sem dependência nova |

Escolhas menores, tomadas no desenho e sujeitas a reversão: o tour é sempre pulável, nunca obrigatório; `GERENTE` vê a trilha de Administrador enquanto não houver conteúdo próprio; o tutorial silencia sobre questões que as specs deixaram em aberto (feriados, fuso definitivo, fallback sem câmera) em vez de inventar resposta.

## 3. Arquitetura e arquivos

```text
recepcaototem/ClientApp/src/help/
  content/
    types.ts          tipos Trilha e Passo
    admin.ts          trilha do Administrador
    profissional.ts   trilha do Profissional
    cliente.ts        trilha do Cliente
    index.ts          registro role -> trilha e lista pública
  TourProvider.tsx    estado do tour e montagem do overlay
  TourOverlay.tsx     spotlight e balão do passo
  useTourTarget.ts    resolve o elemento por data-tour e mede o retângulo
  tourStorage.ts      interface TourProgressStore e implementação localStorage
  HelpCenter.tsx      página /ajuda
  content.test.ts
  TourProvider.test.tsx
  HelpCenter.test.tsx
```

`TourProvider` é montado em `main.tsx`, dentro de `BrowserRouter` e de `SessionProvider`, envolvendo `<App />`: precisa da role para escolher a trilha e do roteador para navegar entre passos. O `TourOverlay` é renderizado uma única vez pelo provider, não por página, e não existe no DOM quando não há tour ativo.

`/ajuda` entra em `App.tsx` como rota pública, irmã de `/login`, fora do `ProtectedRoute`. Ela consulta `useSession()` apenas para escolher a trilha em destaque; com `status === 'anonymous'` destaca a trilha do Cliente.

As telas existentes recebem apenas atributos `data-tour="..."` em `AdminLayout.tsx`, `Rooms.tsx` e `Professionals.tsx`. Nenhuma mudança de layout, classe, estrutura ou comportamento: o desenho visual aprovado permanece intacto.

## 4. Modelo de conteúdo

```ts
type Passo = {
  id: string
  titulo: string
  texto: string
  alvo?: string
  rota?: string
  somenteRoles?: string[]
}

type Trilha = {
  id: 'admin' | 'profissional' | 'cliente'
  titulo: string
  resumo: string
  publica: boolean
  versao: number
  passos: Passo[]
}
```

`versao` é declarada na trilha e copiada para o progresso salvo. Incrementá-la reapresenta o tour a quem já o concluiu, e é o mecanismo previsto para quando a trilha ganhar telas novas.

`alvo` opcional é o que permite às três trilhas coexistirem hoje. Passo com `alvo` é etapa do tour e item da central de ajuda; passo sem `alvo` é somente leitura na central.

`somenteRoles` responde a um caso concreto e verificado: em `Professionals.tsx` o botão de vincular conta só é renderizado para `ADMINISTRADOR`, de modo que um passo apontando para ele quebraria a experiência de um `GERENTE`.

`id` é a chave do progresso salvo e deve ser estável. Renomear um `id` equivale a criar um passo novo.

## 5. As três trilhas

**Administrador** (com passos ancorados): navegação lateral e módulos disponíveis; cadastro de sala e tarifas; busca de salas; cadastro de profissional; foto do profissional e formatos aceitos; vincular conta de acesso, restrito a `ADMINISTRADOR`; ativar e desativar em vez de excluir; filtro de status e paginação; onde reencontrar a ajuda. Os módulos ainda indisponíveis aparecem como um passo de leitura declarando que estão em construção, sem prometer data.

**Profissional** (somente leitura), derivada das regras congeladas no `PROJECT_CONTEXT.md`: você atua apenas nas suas próprias visitas; a agenda não é alterada diretamente por você; nova reserva, reagendamento e cancelamento são solicitações sujeitas a aprovação da gestão, com antecedência mínima de uma hora; a reserva original continua válida durante a análise; reagendamento e cancelamento ficam bloqueados quando há visita aguardando ou em atendimento; você consulta valor, vencimento e situação das suas locações sem alterá-los; toda liberação de acesso é confirmada, limitada e auditada.

**Cliente** (somente leitura, pública), derivada da spec de autoagendamento e QR check-in: o que é o agendamento; o que acontece na chegada; o check-in por QR; o aviso de privacidade e a captura de foto; o que é feito com os dados. Redigida sem prometer prazo de lançamento.

As trilhas de Profissional e Cliente são `publica: true`: o profissional consegue lê-la antes de a área dele existir, e o cliente não possui conta para autenticar.

## 6. Fluxo do tour

**Gatilho.** O tour inicia somente quando `status === 'authenticated'`, fora de `/login`, `/change-password` e `/ajuda`, e apenas se não houver marca de conclusão para a trilha. Os estados `loading` e `mustChangePassword` nunca disparam o tour, para não competir com a troca de senha obrigatória. Usuário anônimo nunca vê tour; o Cliente lê sua trilha na central, sem overlay.

**Navegação.** Um passo com `rota` faz o provider chamar `navigate(rota)` e aguardar o alvo aparecer. O provider não abre modais, não preenche campos e não clica em nada em nome do usuário.

**Alvo ausente.** O provider espera o elemento por até 1500 ms, verificando via `MutationObserver` no documento. Esgotado o prazo, o passo é pulado silenciosamente e o tour avança; em desenvolvimento, um `console.warn` identifica o alvo perdido. Essa regra única cobre três casos reais: tela ainda carregando dados por fetch, elemento ausente por role, e mobile, onde a barra lateral do `AdminLayout` é off-canvas e seus alvos não estão visíveis. O tour omite o que não está presente em vez de manipular a interface para expô-lo.

**Encerramento.** O tour termina por conclusão do último passo ou por "Pular tutorial", ambos gravando a marca correspondente.

**Reentrada.** Item "Ajuda" no rodapé da barra lateral do `AdminLayout`, ao lado de sair, levando a `/ajuda`. Na central, cada trilha com passos ancorados oferece "Refazer o tour", que limpa a marca e reinicia do primeiro passo.

## 7. Persistência e privacidade

Chave `lumis.tour.<trilha>.v1`, com valor `{ estado: 'concluido' | 'pulado', ultimoPasso, versao }`.

`versao` permite reapresentar o tour quando a trilha mudar materialmente, por exemplo ao ganhar telas novas, sem reapresentá-lo a cada ajuste de texto.

Nenhum dado pessoal é gravado, apenas o progresso. Todo acesso passa por `tourStorage.ts` com `try/catch`: em janela anônima ou com armazenamento bloqueado, o módulo cai para uma implementação em memória e o tour continua funcionando durante a sessão. A interface `TourProgressStore` é a costura para migrar o progresso ao perfil do usuário no backend sem reescrever o motor.

A chave `lumis.tour.*` não colide com os marcadores `atrium_*` proibidos no bundle por `scripts/verify-production-bundle.mjs`.

## 8. Acessibilidade e teclado

O balão do passo usa `role="dialog"`, `aria-modal="true"` e `aria-labelledby` apontando para o título do passo. O foco vai para o balão a cada troca de passo e fica contido nele enquanto o tour roda. `Seta direita` e `Enter` avançam, `Seta esquerda` volta, `Escape` encerra o tour, o mesmo contrato de teclado já estabelecido por `Modal.tsx`. O botão "Pular tutorial" é sempre visível, nunca dependente de hover. O indicador de progresso é anunciado por `aria-live="polite"`.

## 9. Estilo visual

O overlay é um único elemento com `box-shadow: 0 0 0 9999px rgba(61, 61, 61, .55)` recortando o retângulo do alvo. Antes de medir, o alvo recebe `scrollIntoView({ block: 'center' })`; o retângulo é recalculado em `resize`, em scroll e por um `ResizeObserver` no alvo.

Os estilos entram em `styles.css` como uma seção nova, reaproveitando as variáveis existentes e a linguagem visual de `.modal-card`. Nenhuma biblioteca de tour é adicionada ao `package.json`.

## 10. Testes

De conteúdo:

- ids de passo únicos dentro de cada trilha
- todo `alvo` declarado existe como `data-tour` no código-fonte das telas, lendo os arquivos com `?raw`, técnica já usada em `production-isolation.test.tsx`
- toda `rota` declarada é uma rota registrada em `App.tsx`

De comportamento:

- o tour não inicia com `status` `loading`, `anonymous` ou `mustChangePassword`
- o tour não reinicia depois de concluído ou pulado, e reinicia quando `versao` muda
- alvo inexistente faz o tour pular o passo em vez de travar
- `Escape` encerra o tour e grava `pulado`
- `tourStorage` continua operando quando `localStorage` lança exceção
- `/ajuda` renderiza a trilha do Cliente sem sessão e a trilha do perfil quando há sessão

O `setup.ts` já executa `localStorage.clear()` após cada teste, de modo que o progresso não vaza entre casos.

## 11. Riscos e manutenção

O risco central é o conteúdo desatualizado. O tutorial descreve regras de módulos ainda em construção, e a distância entre texto e produto tende a crescer. O teste de `alvo` contra `data-tour` protege apenas a parte ancorada; nada verifica automaticamente que a trilha do Profissional continua verdadeira. Por isso fica registrado: quando as áreas de Profissional e Cliente forem implementadas, revisar e ancorar as respectivas trilhas faz parte daquele trabalho, não é tarefa posterior separada.

Os textos das trilhas são conteúdo de produto. São redigidos a partir das specs aprovadas, e sua validação final cabe ao responsável pelo produto, em revisão explícita antes do merge.

## 12. Fora de escopo

Nenhuma alteração de backend, entidade, migration ou endpoint. Nenhuma tela nova além de `/ajuda`. Sem vídeos, GIFs ou capturas de tela, que envelhecem junto com a interface e raramente são reexportadas. Sem internacionalização: pt-BR apenas. Sem analytics de conclusão. Sem tour no Totem ou Recepção enquanto `/recepcao` for módulo indisponível. Sem edição de conteúdo pelo Administrador em tela, decisão revisitável quando houver demanda real.

## 13. Correções ao desenho, feitas durante a implementação

Três afirmações das seções anteriores não sobreviveram ao contato com o código. Ficam registradas aqui em vez de reescritas acima, para que o histórico da decisão continue legível.

**A justificativa de mobile da seção 6 estava factualmente errada.** O texto diz que a barra lateral off-canvas "não está visível" e por isso seus alvos seriam pulados no celular. Não é o que acontece: o off-canvas é `transform: translateX(-102%)` sobre um elemento `position: fixed`, então a barra continua no DOM, `querySelector` a encontra de imediato e o pulo nunca dispararia. O holofote seria recortado fora da tela e o celular veria apenas o escurecimento. A regra passou a considerar ausente também o alvo cujo retângulo medido cai fora da viewport, depois do `scrollIntoView` — assim um elemento apenas rolado para fora continua sendo alcançado, e só o caso realmente off-canvas é pulado.

**O tour ganhou porta de saída, não só de entrada.** A seção 6 descrevia quando o tour começa, mas nada o encerrava quando a sessão deixava de ser autenticada ou quando a rota virava uma das proibidas. Sem isso, sair da conta no meio do tour deixava o balão sobre a tela de login e, a cada passo, o provider tentava voltar ao painel, era rebatido pelo `ProtectedRoute` e remontava o formulário, descartando o que estivesse digitado, até gravar a trilha como concluída para quem nunca a viu. O encerramento por sessão ou rota não grava progresso: quem não dispensou o tutorial volta a recebê-lo.

**Quatro passos do Administrador passaram a ser somente leitura.** `admin-acoes-linha`, `admin-vincular-conta`, `admin-ativar-desativar` e `admin-paginacao` estavam ancorados na linha da tabela e na paginação de Profissionais, que só existem depois que há profissionais cadastrados — isto é, não existem na primeira execução, que é justamente quando o tour roda. O resultado era alguns segundos de tela em branco por passo e a perda silenciosa da regra de desativar em vez de excluir. Os quatro continuam na trilha e na central de ajuda; apenas saíram do tour, pelo mesmo modelo que a seção 4 já define para passos sem alvo. O tour ancora hoje em sete elementos que a tela renderiza independentemente de dados.

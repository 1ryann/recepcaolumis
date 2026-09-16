# Room Rental UX Fixes — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Corrigir a galeria de fotos pública (maior, autoplay, lightbox), rebalancear o espaçamento da tela de detalhe da sala, eliminar o bug de overflow em cards do admin, e transformar o formulário de interesse em um modal com data de início/fim desejadas — sem alterar arquitetura, autenticação, infraestrutura ou o fluxo funcional já validado além do solicitado.

**Spec:** `docs/superpowers/specs/2026-09-15-room-rental-ux-fixes.md` (todas as seções).

**Base:** branch `codex/reception-backend`, worktree `.worktrees/reception-backend`, HEAD `6d09e7bab6467779c62a6657bacc253c688fdb6c` no momento da escrita (Plano 2 completo, revisado, aplicado em staging). Não usar o checkout principal `codex/leases-design` nem main/master.

## Global Constraints

- Fonte de verdade: o spec acima. Regras gerais do usuário (reproduzidas no spec) são vinculantes em todas as tarefas.
- **Migration gate (regra geral #4):** nenhuma tarefa deste plano roda `dotnet ef migrations add` (ou qualquer comando que crie/aplique uma migration) sem que o controller (sessão principal, não um implementer subagent) tenha primeiro parado, relatado ao usuário exatamente as colunas/tipos propostos, e recebido confirmação explícita. Um implementer que chegar a esse ponto retorna `BLOCKED` com esse motivo em vez de gerar a migration.
- Preservar estilo visual: apenas CSS hand-written em `recepcaototem/ClientApp/src/styles.css` com classes semânticas (nada de Tailwind utility classes, CSS Modules, styled-components). Reusar `components/Modal.tsx`, `.primary-button`/`.secondary-button`/`.ghost-button`/`.field-label`/`.field-input`/`.form-error` existentes.
- Nenhuma mudança em Railway, Supabase, variáveis de ambiente, autenticação, ou infraestrutura.
- Nenhuma mudança no cálculo de disponibilidade (`RoomAvailabilityCalculator`/`RoomAvailabilityFormatter`), no fluxo de conversão Inquiry→Lease, nem no `WhatsappOptionsValidator`/geração do link `wa.me` — esses já foram validados em staging (Plano 2) e estão fora de escopo.
- `RoomRentalInquiry.DesiredStartDate`/`DesiredEndDate`: colunas de banco **nullable** (`DateOnly?`), mesmo padrão de `PresentedAvailableFrom` — evita backfill de linhas existentes (há inquiries reais já criadas em staging/dev sem esses campos). A obrigatoriedade é uma regra de aplicação: `RoomRentalInquiry.Create(...)` e `RoomRentalInquiryInput.TryValidate(...)` exigem ambos os valores para todo **novo** registro (retornando o erro `400 INVALID_ROOM_RENTAL_INQUIRY` existente, sem inventar código de erro novo), e `DesiredEndDate >= DesiredStartDate`. Não validar "não pode ser no passado" — não foi pedido.
- Datas trafegam como `DateOnly` (formato `YYYY-MM-DD`), igual a `PresentedAvailableFrom` hoje — não criar um novo `JsonConverter`, o suporte nativo do `System.Text.Json` já é usado no projeto.
- Rodar testes do projeto ao final de cada tarefa aplicável: backend `dotnet test tests/GestaoPredio.UnitTests/GestaoPredio.UnitTests.csproj` e, quando a migration existir, `dotnet test tests/GestaoPredio.IntegrationTests/GestaoPredio.IntegrationTests.csproj`; frontend, em `recepcaototem/ClientApp`: `npx vitest run` e `npx tsc -b`.
- Nenhum commit deste plano faz push, merge, PR, ou toca Railway/Supabase — apenas trabalho local na worktree, como no Plano 2.

## Gates de execução

Raiz da worktree:
```powershell
git status --porcelain -uall
git branch --show-current
git rev-parse HEAD
dotnet build recepcaototem.sln
dotnet test tests/GestaoPredio.UnitTests/GestaoPredio.UnitTests.csproj
dotnet test tests/GestaoPredio.IntegrationTests/GestaoPredio.IntegrationTests.csproj
```
Frontend, em `recepcaototem/ClientApp`:
```powershell
npx vitest run
npx tsc -b
```

Dependências entre tarefas: 1 independente; 2 independente (gate de migration dentro dela); 3 independente; 4 depende de 2 (contrato de datas); 5 depende de 3+4 (composição final de layout).

---

### Task 1: Corrigir overflow de texto/ações nos cards do admin

**Files:**
- Modify: `recepcaototem/ClientApp/src/styles.css` (classes `.room-admin-actions`, `.room-admin-heading > div`, `.admin-mini-row`).
- Test: nenhum teste automatizado novo é exigido (é CSS puro) — validar visualmente via `preview_start`/browser conforme passo de verificação da tarefa.

**Achados confirmados (ver relatório de exploração, não re-investigar):**
- `.room-admin-actions` (usada por `Rooms.tsx`, `RoomRentalInquiries.tsx`, `Leases.tsx`) não tem `flex-wrap`. `Leases.tsx` chega a renderizar até 4 botões nessa linha.
- `.room-admin-heading > div` (nome da sala + status badge, em `Rooms.tsx`) não tem `min-width: 0`.
- `.admin-mini-row` (dashboard, `AdminDashboard.tsx`) tem `justify-content: space-between` sem `min-width: 0` no texto nem proteção no lado do badge.

**Steps:**
1. Em `.room-admin-actions`, adicionar `flex-wrap: wrap` e `row-gap`/`column-gap` consistentes com o `gap: 8px` já existente, garantindo que botões quebrem linha em vez de vazar quando o card é estreito.
2. Em `.room-admin-heading > div` (e qualquer seletor irmão equivalente reaproveitado em `RoomRentalInquiries.tsx`), adicionar `min-width: 0` e, no `<strong>` do nome, `overflow-wrap: break-word` (ou `text-overflow: ellipsis` com `white-space: nowrap` + `overflow: hidden` se truncamento for mais consistente com o resto do admin — escolher truncamento com `title` attribute no elemento se o nome puder ser muito longo, já que nomes de sala são curtos na prática; caso opte por truncar, adicionar `title={room.name}` no JSX de `Rooms.tsx` para acessibilidade).
3. Em `.admin-mini-row`, dar `min-width: 0` e `overflow-wrap: break-word` ao container do nome, e `flex-shrink: 0` explícito ao `.status-badge` para que ele nunca seja espremido a ponto de quebrar seu próprio texto.
4. Procurar (grep) outros usos de padrão idêntico (flex row com texto variável + botões/badges, sem `flex-wrap`/`min-width:0`) fora dos três pontos já listados — se achar, aplicar a mesma correção; se não achar mais nenhum, registrar isso no relatório da tarefa (não é obrigatório inventar um problema).
5. Validar visualmente: abrir `/admin/salas`, `/admin/interesses-locacao`, `/admin/locacoes` e o dashboard no browser (`preview_start`), redimensionar para larguras estreitas (ex.: 700px, 400px) e confirmar que nenhum botão/texto ultrapassa a borda do card/linha.

**Report contract:** DONE com lista de seletores CSS alterados, screenshot/descrição da verificação visual em pelo menos duas larguras, e confirmação de quaisquer outros locais com o mesmo padrão encontrados (ou ausência deles).

---

### Task 2: Backend — data de início/fim desejadas no interesse de locação

**Files:**
- Modify: `src/GestaoPredio.Domain/Rooms/RoomRentalInquiry.cs` (propriedades `DesiredStartDate`/`DesiredEndDate`, validação em `Create`).
- Modify: `src/GestaoPredio.Infrastructure/Persistence/Configurations/RoomRentalInquiryConfiguration.cs` (duas colunas `date` nullable).
- Modify: `recepcaototem/Features/Rooms/RoomRentalInquiryContracts.cs` (`RoomRentalInquiryRequest`, `RoomRentalInquiryAdminResponse`, `ValidRoomRentalInquiryInput`, `RoomRentalInquiryInput.TryValidate`).
- Modify: `recepcaototem/Features/Rooms/RoomRentalInquiryEndpoints.cs` (passar os novos valores validados para `RoomRentalInquiry.Create`, incluir no `RoomRentalInquiryAdminResponse`).
- **Create (GATED — ver abaixo): migration EF** `AddDesiredDatesToRoomRentalInquiry` (ou nome equivalente autodescritivo).
- Test: Modify/extend `tests/GestaoPredio.UnitTests/RoomRentalInquiryDomainTests.cs` (validação de datas: ambas obrigatórias, `End < Start` rejeitado, `End == Start` aceito). Modify `tests/GestaoPredio.IntegrationTests/RoomRentalInquiryTests.cs` (POST com datas válidas retorna 200 e persiste; POST sem datas ou com `End < Start` retorna 400 `INVALID_ROOM_RENTAL_INQUIRY`). Modify `tests/GestaoPredio.IntegrationTests/RoomRentalInquiryAdminTests.cs` se necessário para cobrir os novos campos no response de admin.

**Interfaces:**
- `RoomRentalInquiry.Create(Guid roomId, string fullName, string whatsApp, string professionOrCompany, string? note, PublicRoomAvailabilityStatus presentedStatus, DateOnly? presentedAvailableFrom, DateOnly desiredStartDate, DateOnly desiredEndDate)` — adicionar os dois novos parâmetros ao final da assinatura atual (conferir assinatura real no arquivo antes de editar; não adivinhar a ordem existente). Validação: `desiredEndDate >= desiredStartDate`, senão lançar a mesma exceção de domínio usada pelas outras validações do método (conferir o tipo exato já usado no arquivo, ex. `DomainValidationException` ou equivalente — reusar, não inventar um novo tipo).
- `RoomRentalInquiryRequest` ganha `DateOnly? DesiredStartDate, DateOnly? DesiredEndDate` (nullable no contrato de request porque a validação de "obrigatório" acontece em `TryValidate`, que já mapeia ausência/formato inválido para `400 INVALID_ROOM_RENTAL_INQUIRY` no padrão dos outros campos).
- `RoomRentalInquiryAdminResponse` ganha `DateOnly? DesiredStartDate, DateOnly? DesiredEndDate` no final do record (nullable no response porque inquiries antigas não terão valor).

**Steps:**
1. Ler os quatro arquivos a modificar por completo antes de editar (assinaturas exatas, ordem de parâmetros, estilo de validação existente) — não assumir a partir deste plano.
2. Estender o domínio: propriedades + parâmetros em `Create` + validação `End >= Start`.
3. Estender `RoomRentalInquiryConfiguration`: duas colunas `date` nullable, mesmo padrão de `PresentedAvailableFrom` (nome de coluna = nome da propriedade, sem customização extra).
4. Estender os contratos e `TryValidate`: ambos os campos obrigatórios (400 se nulos), validar `End >= Start` (400 se violado), mapear para o `Create` do domínio.
5. Atualizar o endpoint de criação para repassar os campos validados, e o de listagem/detalhe admin para incluí-los no response.
6. Escrever/estender os testes listados ANTES de rodar a migration (RED esperado: os testes de domínio/contrato podem já compilar e falhar na asserção; os testes de integração que persistem no Postgres vão falhar por falta de coluna — isso é esperado e não é motivo para pular a Etapa 7).
7. **GATE — não pular:** parar aqui. Reportar ao controller (não ao usuário diretamente — o implementer retorna `BLOCKED` com este texto): "Migration necessária: adicionar `DesiredStartDate date NULL` e `DesiredEndDate date NULL` à tabela `RoomRentalInquiries`. Aguardando confirmação explícita antes de rodar `dotnet ef migrations add`." O controller então pausa a execução do plano, relata ao usuário exatamente essas duas colunas/tipos, e só depois de confirmação explícita re-despacha esta tarefa autorizando a Etapa 8.
8. (Somente após confirmação explícita registrada no ledger) Gerar a migration com `dotnet ef migrations add AddDesiredDatesToRoomRentalInquiry --project src/GestaoPredio.Infrastructure --startup-project recepcaototem --context ApplicationDbContext`, inspecionar o SQL gerado (deve conter apenas `ADD COLUMN "DesiredStartDate" date NULL` e `ADD COLUMN "DesiredEndDate" date NULL`, nada mais), e então rodar os testes de unidade e integração.
9. Confirmar `dotnet build recepcaototem.sln` limpo.

**Report contract:** DONE com lista exata de arquivos alterados, o texto exato do SQL gerado pela migration, resultado dos testes (contagem passando), e confirmação de que nenhuma outra tabela/coluna foi tocada. Se a Etapa 7 for atingida sem confirmação prévia do controller, o status correto é `BLOCKED`, não `DONE_WITH_CONCERNS`.

---

### Task 3: Frontend — galeria de fotos maior, com autoplay e lightbox

**Files:**
- Create: `recepcaototem/ClientApp/src/components/RoomPhotoGallery.tsx` (novo componente, extraído da lógica de galeria hoje inline em `TotemRoomDetail.tsx`).
- Create: `recepcaototem/ClientApp/src/components/RoomPhotoGallery.test.tsx`.
- Modify: `recepcaototem/ClientApp/src/pages/TotemRoomDetail.tsx` (substituir o bloco de galeria inline pelo novo componente).
- Modify: `recepcaototem/ClientApp/src/styles.css` (novas classes para o componente: foto principal maior, lightbox/modal de imagem, indicadores de slide).
- Modify: `recepcaototem/ClientApp/src/pages/TotemRoomDetail.test.tsx` se necessário (ajustar seletores que hoje testam a galeria inline).

**Interfaces:**
- `RoomPhotoGallery({ photoUrls: string[], roomName: string }): JSX.Element` — componente autocontido: gerencia seu próprio estado de slide atual, autoplay, foto quebrada (reusar a lógica de `failedPhotos` hoje em `TotemRoomDetail.tsx` — mover para dentro do componente, não duplicar) e abertura/fechamento do lightbox.
- Reusar `components/Modal.tsx` para o lightbox OU um overlay dedicado mais simples se `Modal` não couber bem em tela cheia com navegação lateral — decisão de implementação: preferir um overlay próprio (`role="dialog" aria-modal="true"`) só se `Modal` (que é `place-items:center` com `max-width` fixo) não permitir uma imagem grande confortavelmente; caso contrário reusar `Modal` com `size="large"`. Documentar a escolha no relatório da tarefa.

**Steps:**
1. Ler `TotemRoomDetail.tsx` por completo (o arquivo já foi lido na exploração — usar essa leitura, mas reconfirmar linhas exatas antes de editar) e `styles.css` nos blocos `.totem-room-detail-gallery`/`.totem-room-detail-photo`/`.totem-room-detail-thumbs`/`.totem-room-detail-thumb`.
2. Extrair a lógica de galeria (foto principal, thumbnails, fallback de foto quebrada) para `RoomPhotoGallery.tsx`, preservando o comportamento hoje existente (clicar em thumbnail troca a foto principal; fallback de ícone se vazio ou todas quebradas).
3. Aumentar a área da foto principal: ajustar `.totem-room-detail-photo`/container para ocupar proporção maior da coluna de galeria (ex.: `aspect-ratio` maior, `min-height` maior), mantendo responsividade abaixo de 900px.
4. Adicionar autoplay: quando `photoUrls.length > 1`, avançar automaticamente a cada N segundos (ex.: 5s) usando `useEffect`/`setInterval`, limpando o timer no unmount; pausar o autoplay quando o usuário interage manualmente (clique em thumbnail/seta) e retomar depois de um período de inatividade OU parar definitivamente até a página recarregar — escolher a opção mais simples (parar definitivamente após interação manual é aceitável e mais previsível; documentar a escolha).
5. Adicionar lightbox: clique na foto principal ou numa thumbnail abre a visualização ampliada, com botão fechar (X + tecla Escape) e setas/botões de navegação entre fotos se `photoUrls.length > 1`. Com 1 foto, omitir setas mas manter abrir/fechar funcionando.
6. Fallback vazio (sem fotos) permanece igual ao comportamento atual (ícone `DoorOpen`).
7. Escrever testes em `RoomPhotoGallery.test.tsx` cobrindo: renderização com 0/1/N fotos; troca de foto ao clicar thumbnail; abertura do lightbox ao clicar na foto principal; fechamento via botão e via Escape; navegação entre fotos dentro do lightbox; fallback de foto quebrada. Usar `vi.useFakeTimers()` para testar o autoplay sem esperar tempo real.
8. Ajustar `TotemRoomDetail.tsx` para usar o novo componente e remover o código de galeria duplicado (incluindo o estado `selectedPhoto`/`failedPhotos` que migrou para o componente).
9. Rodar `npx vitest run` e `npx tsc -b` em `recepcaototem/ClientApp`.

**Report contract:** DONE com lista de arquivos, decisão tomada para o lightbox (Modal reusado vs overlay próprio) e por quê, decisão sobre o comportamento do autoplay após interação manual, contagem de testes passando.

---

### Task 4: Frontend — formulário de interesse em modal, com datas desejadas

**Depende de:** Task 2 (contrato `RoomRentalInquiryRequest` com `DesiredStartDate`/`DesiredEndDate`). Se Task 2 ainda não tiver sido concluída (aguardando o gate de migration), esta tarefa fica bloqueada — não implementar contra um contrato hipotético.

**Files:**
- Create: `recepcaototem/ClientApp/src/components/RoomInterestModal.tsx`.
- Create: `recepcaototem/ClientApp/src/components/RoomInterestModal.test.tsx`.
- Modify: `recepcaototem/ClientApp/src/pages/TotemRoomDetail.tsx` (remover form inline; renderizar botão "Tenho interesse" + `<RoomInterestModal open={showForm} onClose={...} .../>`).
- Modify: `recepcaototem/ClientApp/src/api/modules.ts` (ou onde `totemRoomApi.createInquiry` está definido) para incluir os dois novos campos no payload/tipo.
- Modify: `recepcaototem/ClientApp/src/pages/TotemRoomDetail.test.tsx` (ajustar testes que hoje simulam preencher/submeter o form inline).
- Modify: `recepcaototem/ClientApp/src/styles.css` (classes do conteúdo do modal, inputs de data).

**Interfaces:**
- `RoomInterestModal({ open, onClose, roomId, roomName, presentedAvailabilityLabel, onSuccess }): JSX.Element` — `onSuccess` recebe o mesmo shape hoje passado para `navigate(..., { state: {...} })` em `TotemRoomDetail.tsx` (não mudar o contrato de navegação para `TotemRoomInterestSuccess.tsx`, que já valida esse shape estritamente).
- Campos do form: `fullName`, `whatsApp`, `professionOrCompany` (obrigatórios, iguais a hoje), `note` (opcional, igual a hoje), `desiredStartDate`, `desiredEndDate` (novos, obrigatórios, `<input type="date">`).

**Steps:**
1. Ler o código atual do form inline em `TotemRoomDetail.tsx` (linhas ~239-318 segundo a exploração; reconfirmar antes de editar) e o `Modal.tsx` existente.
2. Criar `RoomInterestModal.tsx` reusando `Modal` (tamanho `medium` ou `large`, o que couber melhor com 6 campos), movendo os campos existentes e adicionando os dois campos de data com `<label className="field-label">`/`<input className="field-input" type="date">` (mesmo padrão visual dos outros inputs — conferir classe exata usada hoje pelos inputs de texto/textarea do form).
3. Validação client-side: manter a mensagem de obrigatórios existente e estendê-la para cobrir as duas datas (obrigatórias) e a regra `desiredEndDate >= desiredStartDate` (mensagem de erro específica e clara, ex. "A data final não pode ser anterior à data inicial.").
4. Trocar em `TotemRoomDetail.tsx` o botão que hoje alterna `showForm` (inline) para abrir o modal; a página passa a mostrar sempre: fotos (via `RoomPhotoGallery` da Task 3), nome, status/disponibilidade, botão "Tenho interesse".
5. Atualizar `totemRoomApi.createInquiry` (tipo do payload e chamada) para enviar `desiredStartDate`/`desiredEndDate` no formato `YYYY-MM-DD` (o valor nativo de `<input type="date">` já vem nesse formato — não fazer parsing/formatação manual além do necessário).
6. Ao sucesso, fechar o modal e navegar exatamente como hoje (mesmo `state` shape) para `TotemRoomInterestSuccess.tsx` — não adicionar novos campos a esse `state` (a página de sucesso já valida estritamente o shape esperado).
7. Escrever/ajustar testes: modal abre ao clicar "Tenho interesse"; fecha ao clicar fora/no X/Escape; validação bloqueia submit sem datas ou com `end < start`; submit válido chama `createInquiry` com exatamente os 6 campos e navega com o state esperado (seguir o padrão de asserção exata já usado em `TotemRoomDetail.test.tsx`, ex. "submit sends exactly six fields").
8. Rodar `npx vitest run` e `npx tsc -b`.

**Report contract:** DONE com lista de arquivos, confirmação do payload exato enviado ao backend (nomes de campo, formato de data), e contagem de testes passando.

---

### Task 5: Frontend — rebalanceamento final de layout/espaçamento da tela de detalhe

**Depende de:** Task 3 e Task 4 (precisa da galeria final e do form já removido da coluna de info para rebalancear com o conteúdo real).

**Files:**
- Modify: `recepcaototem/ClientApp/src/pages/TotemRoomDetail.tsx` (composição/JSX do grid principal, se necessário).
- Modify: `recepcaototem/ClientApp/src/styles.css` (`.totem-room-detail-content`, `.totem-room-detail-gallery`, `.totem-room-detail-info`, breakpoint de 900px, espaçamentos).

**Steps:**
1. Com a galeria (Task 3) e o botão+modal (Task 4) já no lugar, revisar a proporção de colunas do grid (`.totem-room-detail-content`) — a galeria maior (Task 3) provavelmente já pede uma proporção diferente de `fr` entre galeria e info.
2. Ajustar paddings/gaps para reduzir espaço vazio na coluna de info (agora mais enxuta: nome, status/disponibilidade, descrição se houver, botão) sem espremer os elementos — usar `justify-content`/`align-content`/`gap` em vez de margens soltas ad-hoc.
3. Confirmar responsividade: abaixo de 900px continua colapsando para 1 coluna (ou ajustar o breakpoint se o novo tamanho da galeria pedir um valor diferente — documentar se mudar).
4. Validar visualmente no browser (`preview_start`) em pelo menos 3 larguras: desktop largo (>1200px), tablet (~900px, no breakpoint), mobile (~400px) — confirmar que não há espaço vazio excessivo nem elementos "soltos", e que a galeria (autoplay + lightbox da Task 3) e o botão de interesse (Task 4) continuam funcionais em todas.
5. Rodar `npx vitest run` para garantir que nenhum teste de layout/snapshot quebrou (não deve haver testes de snapshot de CSS, mas confirmar que nenhum teste de comportamento foi afetado por reposicionamento de elementos).

**Report contract:** DONE com descrição objetiva do que mudou na proporção/espaçamento, confirmação visual nas três larguras testadas, e qualquer ajuste de breakpoint feito (com justificativa).

---

## Entrega final (após todas as tarefas + revisão final)

O relatório final deste plano deve responder explicitamente aos 7 itens da seção "Entrega esperada" do spec: arquivos alterados; o que foi corrigido; se houve alteração de backend; se foi necessário alterar DTO/model/endpoint para as datas; testes executados; pendências restantes; outros locais com o bug de overflow encontrados/corrigidos.

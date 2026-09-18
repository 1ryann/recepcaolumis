# Room Rental UX Fixes — Spec

**Origem:** instrução direta do usuário em 2026-09-15, transcrita abaixo sem reinterpretação de requisitos, apenas organizada por seção.

**Contexto:** o fluxo de aluguel de salas (Plano 2) já está funcional até a etapa do WhatsApp (branch `codex/reception-backend`, HEAD `6d09e7b` no momento da escrita deste spec). Foram identificados problemas de UI/UX no fluxo público de salas e bugs visuais em cards do admin. Objetivo: corrigir e melhorar a experiência sem alterar desnecessariamente o restante do sistema.

## Regras gerais (vinculantes para todas as tarefas)

1. Fazer somente as alterações necessárias para estas correções.
2. Preservar o estilo visual atual do sistema (CSS hand-written existente em `recepcaototem/ClientApp/src/styles.css`, sem introduzir Tailwind utility classes, CSS Modules ou styled-components).
3. Não mudar arquitetura, autenticação, Railway, Supabase, env vars ou infraestrutura.
4. Não criar migration a menos que seja absolutamente necessário — e, se for necessário, **parar e relatar antes** de gerar/aplicar a migration, aguardando confirmação explícita.
5. Não alterar o fluxo funcional já validado além do que foi solicitado.
6. Depois de implementar, rodar os testes e validar visualmente o fluxo.
7. Se encontrar outros pontos com o mesmo bug visual de overflow/texto saindo do card, aplicar a correção de forma consistente onde fizer sentido.

## Correção 1 — Galeria de fotos da sala maior e mais útil

**Problema:** na tela pública de detalhe da sala (`TotemRoomDetail.tsx`), a área das fotos é pequena e pouco aproveitada; hoje é uma foto principal + tira de miniaturas, sem lightbox nem autoplay.

**Requisitos:**
1. A área principal da foto deve ficar maior e com mais destaque visual.
2. As miniaturas podem continuar abaixo ou ao lado, mas a imagem principal precisa ter mais presença.
3. As fotos devem passar automaticamente sozinhas em formato de slideshow/carrossel caso o cliente não interaja.
4. Se o usuário interagir manualmente, o comportamento deve continuar funcional sem travar a galeria (interação do usuário pausa/reinicia o autoplay sem quebrá-lo).
5. Ao clicar na imagem principal (ou numa miniatura), deve abrir uma visualização em tamanho maior, preferencialmente em modal/lightbox, com: foco na imagem; botão para fechar; opção de navegar entre imagens, se houver mais de uma.
6. Se houver apenas 1 foto, não precisa autoplay agressivo, mas a visualização ampliada ao clicar deve continuar funcionando.
7. Se não houver fotos, manter fallback visual consistente (ícone/estado vazio já existente).

**Critérios de aceitação:** foto principal ocupa mais espaço; existe rotação automática entre fotos quando há mais de uma; é possível clicar e ampliar a foto; visual continua limpo, elegante e compatível com o restante do Totem/Lumis.

## Correção 2 — Espaçamento e aproveitamento do layout

**Problema:** a tela pública de detalhe da sala tem muito espaço mal aproveitado, principalmente na distribuição entre galeria e conteúdo.

**Requisitos:**
1. Melhorar o espaçamento geral da tela de detalhe da sala.
2. Aproveitar melhor a largura e altura útil do container/card principal.
3. Rebalancear a composição entre: galeria/fotos; título da sala; status; ações.
4. Evitar sensação de elementos "soltos" ou afastados demais.
5. Garantir boa responsividade sem quebrar em desktop e mobile (a página já colapsa para 1 coluna abaixo de 900px — preservar/ajustar esse breakpoint conforme necessário).

**Critérios de aceitação:** layout parece mais preenchido e proporcional; menos área vazia sem função; melhor distribuição visual entre imagem e conteúdo; continua responsivo.

## Correção 3 — Bug de texto/ação saindo dos cards (overflow)

**Problema confirmado por inspeção:** `.room-admin-actions` (definida em `styles.css`, reusada por `Rooms.tsx`, `RoomRentalInquiries.tsx` e `Leases.tsx`) não tem `flex-wrap`, e containers de texto irmãos (`.room-admin-heading > div`, `.admin-mini-row > div:first-child` no `AdminDashboard.tsx`) não têm `min-width: 0` — nomes longos ou 3-4 botões de ação em uma linha podem vazar da borda do card.

**Requisitos:**
1. Corrigir o bug de textos/labels/ações que ficam saindo para fora dos cards.
2. Aplicar a correção em todos os componentes semelhantes onde o mesmo bug existir, não apenas no ponto originalmente notado.
3. Revisar especialmente: cards de salas (`Rooms.tsx`); cards/listagens no admin (`RoomRentalInquiries.tsx`, `Leases.tsx`, `AdminDashboard.tsx`); botões/ações longas; labels de status; textos descritivos.
4. Ajustar: largura interna; flex/grid; wrapping; overflow; truncamento quando fizer sentido; alinhamento dos botões.
5. Garantir que ações não "vazem" para fora do card.

**Critérios de aceitação:** nenhum texto importante fica para fora do card; nenhum botão/ação aparece cortado ou vazando; ajuste consistente em outras áreas com o mesmo padrão visual.

## Correção 4 — Formulário de interesse em modal

**Problema:** hoje o formulário de interesse é renderizado inline, na mesma coluna de informação, abaixo da galeria — não é um modal.

**Novo comportamento esperado:**
1. Na tela pública da sala, o usuário vê: fotos; nome da sala; status; botão de ação (ex.: "Tenho interesse").
2. Ao clicar em "Tenho interesse", abrir um modal simples e limpo com o formulário (reusar o componente `Modal` já existente em `components/Modal.tsx`, mesmo padrão usado por `Rooms.tsx`/`RoomRentalInquiries.tsx`).
3. Campos do modal: Nome; WhatsApp; Profissão/Empresa; Observação (opcional); Data de início desejada; Data de fim desejada.
4. Manter a integração já existente com o backend e com o fluxo de geração do WhatsApp, adaptando os dados novos se necessário.
5. Validar corretamente: campos obrigatórios; formato mínimo dos dados; data inicial; data final; data final não menor que a inicial.
6. O modal deve ser fácil de fechar.
7. Após envio com sucesso, o sistema continua levando para a etapa de "Continuar no WhatsApp" (`TotemRoomInterestSuccess.tsx`), como já faz hoje.

**Observação de backend (confirmada por inspeção — ver relatório de exploração):** não existe hoje nenhum campo de data desejada de início/fim em `RoomRentalInquiry` (domínio, EF config, DTOs de request/response, nem no frontend). Adicionar esses campos é uma mudança de schema nova: `RoomRentalInquiry` (domínio), `RoomRentalInquiryConfiguration` (EF — **migration nova necessária**), `RoomRentalInquiryContracts.cs` (request/response), `RoomRentalInquiryEndpoints.cs` (validação), `totemRoomApi`/tipos do frontend, `TotemRoomDetail.tsx` (modal), e opcionalmente `RoomRentalInquiries.tsx` (admin). Por força da Regra Geral #4, a criação/aplicação dessa migration exige parar e relatar antes de prosseguir — implementação de domínio/DTO/testes pode avançar, mas a migration em si é um ponto de checagem explícito.

**Critérios de aceitação:** formulário não fica mais fixo ao lado da galeria; formulário abre em modal; modal contém início e fim desejados; envio continua funcionando; fluxo posterior de WhatsApp continua operacional.

**Complemento (2026-09-18, auditoria D1):** a mensagem enviada ao Financeiro pelo WhatsApp inclui o período desejado — `Período desejado: dd/MM/yyyy até dd/MM/yyyy`, logo após a linha de disponibilidade (ver spec 2026-09-13, §8.3).

## Escopo adicional implícito

Ao corrigir essas partes, revisar o fluxo completo: listagem pública de salas; detalhe da sala; modal de interesse; tela de sucesso/QR/WhatsApp; cards de administração de salas. Ajustes adicionais são aceitos apenas se pequenos, no mesmo escopo, e sem refactor desnecessário.

## Entrega esperada

1. Lista de arquivos alterados.
2. Descrição objetiva do que foi corrigido.
3. Se houve necessidade de alteração no backend.
4. Se foi necessário alterar DTO/model/endpoint para incluir data de início e fim.
5. Testes executados.
6. Pendências restantes.
7. Se foram encontrados mais lugares com o mesmo bug de overflow e onde foi corrigido.

Decisões de implementação ambíguas devem escolher a opção mais simples, elegante e consistente com o visual atual do Lumis.

# Aceite do carrossel nativo do totem — 2026-09-13

## Status de aceite

**ACEITE FÍSICO PENDENTE.** Este documento é um registro de preparação e de
evidência automatizada local; não é um GREEN de homologação física e não declara
o defeito mobile resolvido. Nenhum aparelho físico, toque real, URL publicada,
staging ou deploy foi usado nesta verificação.

Para a execução física futura, registrar no próprio aceite: aparelho, versão do
sistema, navegador e versão, URL, SHA/identidade do artefato e o resultado de
cada linha da matriz abaixo. O candidato atual só pode ser aceito após essa
execução no aparelho que reproduzia o problema.

## Identidade do candidato e contexto físico

| Campo | Evidência |
| --- | --- |
| Branch local | `codex/reception-backend` |
| SHA do código candidato | `c76c2be20edf5d3d6b122b76a9ba9c38de92c28a` (`fix(totem): enlarge responsive coverflow stage`) |
| Confirmação local da identidade | Antes de criar este registro, `git rev-parse HEAD` retornou `c76c2be20edf5d3d6b122b76a9ba9c38de92c28a`; o build local abaixo foi gerado desse checkout. |
| URL para aceite físico | **NÃO EXECUTADO / ACEITE FÍSICO PENDENTE** — nenhuma instância publicada ou local acessível por celular foi preparada. |
| Aparelho / SO / navegador | **NÃO EXECUTADO / ACEITE FÍSICO PENDENTE** — nenhum aparelho físico foi informado ou utilizado. |
| Handoff/QR em dispositivo físico | **NÃO EXECUTADO / ACEITE FÍSICO PENDENTE** — sem toque real e sem URL do build no aparelho. |

O SHA identifica o checkout usado nos gates locais; ele não comprova a identidade
de um artefato publicado. Não houve publicação autorizada para verificar
`/health`, `/health/ready` ou a identidade de deployment.

## RED manual inicial — matriz obrigatória

O RED desta tarefa é a ausência de execução física, não uma falha inventada de
teste automatizado. Cada caso permanece **NÃO EXECUTADO / ACEITE FÍSICO
PENDENTE** até ser realizado com toque real sobre foto e texto no aparelho que
reproduzia o bug.

| Caso físico | Resultado | Evidência física |
| --- | --- | --- |
| Resolução 1920×1080 | **NÃO EXECUTADO / ACEITE FÍSICO PENDENTE** | Sem aparelho/URL. |
| Resolução 1366×768 | **NÃO EXECUTADO / ACEITE FÍSICO PENDENTE** | Sem aparelho/URL. |
| Resolução 768×1024 | **NÃO EXECUTADO / ACEITE FÍSICO PENDENTE** | Sem aparelho/URL. |
| Resolução 390×844 | **NÃO EXECUTADO / ACEITE FÍSICO PENDENTE** | Sem aparelho/URL. |
| Swipe para esquerda e direita | **NÃO EXECUTADO / ACEITE FÍSICO PENDENTE** | Sem toque real. |
| Dez swipes seguidos | **NÃO EXECUTADO / ACEITE FÍSICO PENDENTE** | Sem toque real. |
| Swipe curto e longo nos extremos | **NÃO EXECUTADO / ACEITE FÍSICO PENDENTE** | Sem toque real. |
| Tap lateral centraliza o cartão | **NÃO EXECUTADO / ACEITE FÍSICO PENDENTE** | Sem toque real. |
| Tap central abre o handoff correto | **NÃO EXECUTADO / ACEITE FÍSICO PENDENTE** | Sem toque real. |
| Scroll não dispara handoff | **NÃO EXECUTADO / ACEITE FÍSICO PENDENTE** | Sem toque real. |
| Scroll vertical sobre imagem e texto | **NÃO EXECUTADO / ACEITE FÍSICO PENDENTE** | Sem toque real. |
| Sem seleção, callout ou arrasto de imagem | **NÃO EXECUTADO / ACEITE FÍSICO PENDENTE** | Sem toque real. |
| Nenhum travamento | **NÃO EXECUTADO / ACEITE FÍSICO PENDENTE** | Sem sessão física. |

## Gates locais executados no candidato

Estes resultados são automação local no checkout identificado acima e não
substituem a matriz física. A primeira tentativa sandbox da suíte focada falhou
ao carregar o Vite com `spawn EPERM`; a repetição local fora do sandbox foi
necessária e passou. Não houve mudança de código, dependência ou ambiente para
obter esses resultados.

| Comando | Resultado fresco |
| --- | --- |
| `npx vitest run src/features/totem/TotemProfessionalCarousel.test.tsx` | PASS — 1 arquivo, 13 testes. |
| `npx vitest run` | PASS — 74 arquivos, 432 testes. |
| `npx tsc -b` | PASS — saída vazia, exit 0. |
| `npm run build` | PASS — 1.936 módulos transformados. Permanece o aviso existente de bundle JavaScript `index-DbiRxaCJ.js` com 517,47 kB após minificação. |
| `npm run verify:production-bundle` | PASS — nenhum marcador de AppStore ou mock storage de desenvolvimento emitido. |

## Evidência automatizada de navegador e layout

As quatro capturas retidas são evidência local automatizada, não captura em
celular físico: `visual/after-1920x1080.png`,
`visual/after-1366x768.png`, `visual/after-768x1024.png` e
`visual/after-390x844.png`. As métricas vêm de `visual/final-metrics.log`.

| Viewport automatizado | Documento | Palco (x, y, largura, altura) | Cartão ativo (largura, altura) | Erro de centralização | Vizinhos visíveis |
| --- | --- | --- | --- | --- | --- |
| 1920×1080 | 1920×1080 | 518, 317,765625, 884, 540 | 339,199951, 477 | -5 px | 219,446533 px / 229,446533 px |
| 1366×768 | 1366×815 | 241, 295,6875, 884, 481,421875 | 339,199951, 445,199951 | 0 px | 224,446564 px / 224,446533 px |
| 768×1024 | 768×1024 | 30,71875, 241,71875, 706,5625, 540 | 296,800018, 466,731201 | -0,16 px | 169,285118 px / 169,597595 px |
| 390×844 | 390×844 | 8, 269,15625, 374, 482,453125 | 314,174072, 439,850311 | -0,23 px | 11,480377 px / 11,933502 px |

`visual/layout-checks.log` também registra, em automação de navegador local,
Home/End/setas, redução de movimento e scroll vertical sobre o cartão em
390×600: o documento tinha 753 px, e o `scrollY` passou de 0 para 153. Isso
apoia a regressão automatizada; não demonstra o gesto físico, o callout nativo
ou o comportamento de toque em aparelho real.

## Limitação de 200% e RED de Task 2

`visual/effective-200-zoom-equivalent.log` registra que cinco `Ctrl+plus`
despachados por automação mantiveram `innerWidth: 1920`, `innerHeight: 1080`,
`devicePixelRatio: 1` e `visualViewport.scale: 1`. Por isso, essa tentativa não
é prova de zoom literal da interface do Chrome.

A evidência alternativa usa emulação DevTools de viewport efetivo `960×540` em
DPR 2, equivalente ao contexto CSS de uma janela desktop 1920×1080 em DPR 1 a
200% de zoom. Ela registrou movimento normal, foco do carrossel, cartão com
`matrix(1.06, 0, 0, 1.06, 0, 0)`, erro de centralização 0, ausência de overflow
horizontal (960/960) e scroll vertical de 0 para 203. É uma equivalência de
viewport efetivo; **não é zoom literal da UI do navegador e não é evidência
física**.

O relatório da Task 2 informa que o stdout RED original não estava retido quando
a retomada começou. Há hoje um arquivo local `task-2-red.log` com uma falha de
13 testes que procura `--totem-card-width:`, mas ele não traz uma associação
verificável ao commit candidato nem substitui o stdout RED original ausente.
Portanto, a linhagem RED inicial da Task 2 permanece incompleta; os gates GREEN
frescos acima são registrados separadamente e não corrigem essa ausência.

## Conclusão de escopo

Nenhuma alteração foi feita em backend, aluguel de salas, especificação, plano,
Railway, Supabase, migrations, ambiente, publicação, push, deploy ou merge.
O aceite físico continua pendente e o bug mobile não está encerrado por este
registro.

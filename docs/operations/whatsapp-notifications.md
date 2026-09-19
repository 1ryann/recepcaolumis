# Notificações operacionais via WhatsApp

O WhatsApp é um canal operacional do LUMIS: avisa o **profissional** quando o cliente chega e avisa o **cliente**
sobre cancelamento, atraso, reagendamento e confirmação. Tudo usa a integração oficial existente
(`IWhatsAppService` → `WhatsAppCloudApiService`, `WhatsAppMessage`, `IWhatsAppMessageStore`, webhook). Não há
segundo cliente HTTP da Meta.

## Arquitetura (outbox)

```
check-in / cancelamento / reagendamento / confirmação (transação de negócio)
  └─ grava a linha WhatsAppNotifications (PENDING) NO MESMO COMMIT
       └─ WhatsAppNotificationWorker (BackgroundService, a cada PollIntervalSeconds, se Enabled)
            ├─ enfileira avisos de atraso devidos (PROFESSIONAL_DELAYED)
            ├─ reivindica pendentes: UPDATE … FOR UPDATE SKIP LOCKED  (PENDING → PROCESSING, lease)
            ├─ monta o template a partir dos registros reais (WhatsAppNotificationComposer)
            └─ IWhatsAppService.SendTemplateAsync → ACCEPTED (wamid) | retry | FAILED | SKIPPED
webhook existente (sent/delivered/read/failed) → WhatsAppMessage E a notificação com o mesmo wamid
```

- A operação de negócio **nunca** chama a Meta nem espera por ela. Meta fora do ar não afeta check-in, cancelamento
  etc.; a notificação só fica pendente.
- A fila guarda apenas identificadores (reserva, visita, profissional, cliente), tipo, status, tentativas, próximo
  horário, wamid e um código de erro estável. **Não** guarda telefone, nomes, texto da mensagem, parâmetros do
  template, link de reagendamento, token, app secret, verify token nem payload da Meta.
- O destinatário é sempre decidido pelo backend a partir do registro real (profissional da visita, cliente da reserva).
  O frontend não escolhe telefone, template nem parâmetros. A única rota administrativa é de leitura.

## Eventos e origens

| Tipo | Destinatário | Origem real | Chave de idempotência |
|---|---|---|---|
| `CLIENT_CHECKED_IN` | profissional | `Visit.Arrive` — check-in no Totem (`/api/totem/check-in/confirm`), check-in manual da recepção, visita registrada pelo Admin | `CHECKIN:{VisitId}` |
| `PROFESSIONAL_CANCELLED` | cliente | imprevisto do profissional (`/api/professional/incidents`) — reserva cancelada com motivo `ProfessionalUnavailable`; mensagem com **link de reagendamento** | `CANCEL:{ReservationId}` |
| `APPOINTMENT_CANCELLED` | cliente | Admin cancela (`/api/admin/reservations/{id}/cancel`) ou aprova o pedido de cancelamento do profissional | `CANCEL:{ReservationId}` |
| `APPOINTMENT_RESCHEDULED` | cliente | Admin reagenda (`/reschedule`) ou aprova o pedido de reagendamento do profissional; chave pela reserva **substituta** | `RESCHEDULE:{ReservaNovaId}` |
| `APPOINTMENT_CONFIRMED` | cliente | agendamento do próprio cliente (inclui o fluxo QR do Totem) e agendamento assistido pela recepção; aprovação de reserva nova que tenha cliente | `CONFIRM:{ReservationId}` |
| `PROFESSIONAL_DELAYED` | cliente | calculado pelo dispatcher (ver política abaixo) | `DELAY:{ReservationId}:{passo}` |
| `APPOINTMENT_REMINDER` | cliente | **preparado, não implementado** (sem scheduler de lembrete nesta fase) | — |

Não há aviso ao cliente quando **ele mesmo** cancela ou reagenda. Reservas sem cliente (criadas por Admin/profissional
sem `CustomerId`) não geram aviso ao cliente.

A chave é única no banco (`UX_WhatsAppNotifications_IdempotencyKey`): o mesmo evento nunca gera duas mensagens. Um
check-in repetido reutiliza a visita existente e não cria nova linha; uma reserva só é cancelada uma vez.

## Política de atraso (`PROFESSIONAL_DELAYED`)

Condição: reserva `Approved` com cliente, cliente **já chegou** (visita `WAITING` da reserva) e atendimento **não
iniciado**, com `StartAt` há pelo menos `DelayFirstNoticeMinutes`.

- passo `n` fica devido em `DelayFirstNoticeMinutes + n × DelayRepeatMinutes` de atraso;
- no máximo `DelayMaxNotices` passos por reserva;
- a cada ciclo só o passo **mais recente** devido é enfileirado (um scheduler que ficou parado não dispara rajada);
- a chave `DELAY:{reserva}:{passo}` impede repetir o mesmo passo, não importa quantos ciclos rodem;
- no envio, se o atendimento já começou (visita não está mais `WAITING`), o aviso é `SKIPPED/OBSOLETE`.

Padrão: 1º aviso aos 10 min, 2º aos 25 min, máximo 2. Minutos informados = `agora − StartAt` no envio. Tudo em
instantes UTC; a exibição usa `Scheduling:TimeZoneId` (America/Porto_Velho) — nunca o fuso do servidor.

## Retry, falhas e expiração

- Cada reivindicação é uma tentativa. Falhas **temporárias** (`WHATSAPP_PROVIDER_UNAVAILABLE`, `WHATSAPP_TIMEOUT`,
  `WHATSAPP_NETWORK_ERROR`, `WHATSAPP_RATE_LIMITED`) voltam para `PENDING` com espera `RetryDelaysSeconds`
  (padrão 30 s, 2 min, 10 min; o último se repete) até `MaxAttempts` (padrão 4) → depois `FAILED`.
- Falhas **permanentes** (template recusado, destinatário não permitido, autenticação, não configurado, resposta
  inválida…) → `FAILED` na hora, sem retry.
- Telefone ausente/inválido → `FAILED/RECIPIENT_PHONE_INVALID`; destinatário inativo/inexistente →
  `FAILED/RECIPIENT_UNAVAILABLE`. O fluxo principal nunca é afetado.
- Situação mudou (visita já em atendimento, reserva não mais aprovada, link já usado) → `SKIPPED/OBSOLETE`.
- Template do tipo não configurado → `SKIPPED/TEMPLATE_NOT_CONFIGURED` (nunca vira texto livre).
- Aviso velho demais para ser útil → `SKIPPED/EXPIRED`: check-in e atraso após `OperationalMaxAgeMinutes` (30);
  demais após `SchedulingMaxAgeHours` (24). Evita disparar avisos antigos quando o envio é ligado.
- Worker que morre segurando um item: o lease (`LeaseSeconds`) expira e outro ciclo o reivindica (conta como nova
  tentativa). Único caso de duplicidade possível: morte **depois** de a Meta aceitar e **antes** de gravar o wamid.

## Status de entrega

O webhook existente atualiza `WhatsAppMessage` e, no mesmo `SaveChanges`, a notificação com o mesmo wamid:
`ACCEPTED → SENT → DELIVERED → READ`, só para frente; `FAILED` é terminal com `WHATSAPP_DELIVERY_FAILED:{código}`.
Duplicados e fora de ordem não mudam nada. Se o status chegar antes de o dispatcher gravar o wamid, o dispatcher o
aplica ao registrar o aceite.

## Templates para aprovação no WhatsApp Manager

Criar manualmente no WhatsApp Manager (nada é criado automaticamente). Idioma **Portuguese (BR) — `pt_BR`**.
Categoria sugerida: **Utility** (mensagens transacionais ligadas a um atendimento já existente; a Meta decide a
classificação final). Os nomes abaixo são sugestões — o que vale é o configurado em `Whatsapp__Templates__*`.

| Configuração | Nome sugerido | Corpo | Variáveis |
|---|---|---|---|
| `ClientCheckedIn` | `professional_client_checked_in` | Olá, {{1}}. O cliente {{2}} já chegou para o atendimento das {{3}}. | 1 nome do profissional · 2 primeiro nome do cliente · 3 hora (HH:mm) |
| `ProfessionalDelayed` | `client_professional_delayed` | Olá, {{1}}. Seu atendimento com {{2}} está com um atraso de aproximadamente {{3}} minutos. Pedimos que aguarde. | 1 primeiro nome do cliente · 2 profissional · 3 minutos |
| `ProfessionalCancelled` | `client_professional_cancelled_reschedule` | Olá, {{1}}. Seu atendimento com {{2}}, previsto para {{3}} às {{4}}, precisou ser cancelado por um imprevisto do profissional. Toque no botão abaixo para escolher um novo horário (link válido por 48 horas). | 1 cliente · 2 profissional · 3 data (dd/MM/aaaa) · 4 hora — **botão URL dinâmico**: `https://<domínio público>/reagendar/{{1}}` |
| `AppointmentCancelled` | `client_appointment_cancelled` | Olá, {{1}}. Seu atendimento com {{2}}, previsto para {{3}} às {{4}}, foi cancelado. Entre em contato com a recepção para reagendar. | 1 cliente · 2 profissional · 3 data · 4 hora |
| `AppointmentRescheduled` | `client_appointment_rescheduled` | Olá, {{1}}. Seu atendimento com {{2}} foi reagendado para {{3}} às {{4}}. | 1 cliente · 2 profissional · 3 nova data · 4 nova hora |
| `AppointmentConfirmed` | `client_appointment_confirmed` | Olá, {{1}}. Seu atendimento com {{2}} está confirmado para {{3}} às {{4}}. | 1 cliente · 2 profissional · 3 data · 4 hora |
| `AppointmentReminder` | `client_appointment_reminder` | Olá, {{1}}. Lembrete: você tem atendimento com {{2}} amanhã às {{3}}. | preparado para fase futura |

Link de reagendamento: o token de uso único é gerado **no momento do envio** (o hash é gravado em
`RescheduleTokens`, como antes) e vai só no sufixo do botão; o valor bruto nunca é persistido nem logado. A base da
URL fica no template aprovado e deve ser o mesmo domínio público de `Rescheduling:PublicBaseUrl`. Validade:
`Rescheduling:LinkTtlHours` (48 h), contada a partir do envio.

## Decisão: token do link de reagendamento no envio assíncrono

**Problema.** O cancelamento por imprevisto emite um token de uso único (48 h). O valor bruto só existe durante a
requisição; o banco guarda apenas `SHA256(token)` em `RescheduleTokens` (um por reserva). Com o envio assíncrono, o
token bruto **não existe mais** quando o `BackgroundService` processa a notificação.

**Opções avaliadas.**

| Opção | Avaliação |
|---|---|
| 1. Regenerar o token no envio | O modelo já prevê rotação (`RescheduleToken.Rotate`); resolve/confirm localizam o token só pelo hash e não mudam. Nenhum segredo fica em repouso além do hash que já existia. **Escolhida.** |
| 2. Persistir o token (cifrado) para enviar depois | Cria um segredo em repouso na fila (mesmo cifrado com Data Protection: chave, rotação de chaves, backup, vida útil maior que a do link). Sem ganho sobre a opção 1. **Rejeitada.** |
| 3. Template sem link, "procure a recepção" | Perde o autoatendimento que existe hoje. Só faria sentido se 1 não fosse compatível. **Rejeitada** (mantida para `APPOINTMENT_CANCELLED`, em que não há fluxo de reagendamento por link). |

**Como funciona (opção 1).**
- O imprevisto continua criando a linha `RescheduleTokens` (hash de um valor aleatório que nunca sai do servidor) na
  mesma transação do cancelamento, e a fila grava só `CANCEL:{reserva}` — **nunca** o token.
- No envio, o dispatcher: (a) pula a notificação como `OBSOLETE` se o token já foi **usado** ou **revogado** — sem
  rotacionar, porque `Rotate` limparia `UsedAt`/`RevokedAt` e ressuscitaria um link encerrado; (b) caso contrário gera
  32 bytes aleatórios, grava o novo hash com validade `agora + Rescheduling:LinkTtlHours` e **commita antes de chamar a
  Meta**; (c) envia o valor bruto apenas como sufixo do botão URL do template; (d) registra auditoria
  `RESCHEDULE_LINK_ISSUED` com o id da notificação como correlação. O valor bruto não vai para tabela, log ou auditoria.
- Concorrência: `RescheduleTokens` tem token de concorrência (`xmin`); se o cliente usar o link no mesmo instante da
  rotação, o `SaveChanges` falha, a notificação volta para retry e a próxima tentativa vê `UsedAt` e pula.

**Riscos aceitos.**
- Cada tentativa de envio rotaciona o token: só o link da **última** mensagem enviada vale. Se uma tentativa
  terminou em timeout mas a Meta tinha entregue, o retry envia uma mensagem nova com link válido e o link da anterior
  deixa de funcionar (a página de reagendamento mostra "link inválido ou expirado").
- Se uma tentativa ambígua (timeout) de fato entregou e **todas** as seguintes falharem de forma permanente, o
  cliente fica com um link inválido. É o mesmo desfecho da opção 3, e continua visível em
  `/api/admin/whatsapp/notifications` como `FAILED`, para a recepção contatar o cliente.

## Configuração

| Variável de ambiente | Padrão | Uso |
|---|---|---|
| `Whatsapp__Notifications__Enabled` | `false` | Liga o envio. Desligado: eventos continuam registrados, nada é enviado. |
| `Whatsapp__Notifications__PollIntervalSeconds` | 15 | Intervalo do worker. |
| `Whatsapp__Notifications__BatchSize` | 20 | Itens por ciclo. |
| `Whatsapp__Notifications__LeaseSeconds` | 120 | Lock de um item em envio (> `Whatsapp__TimeoutSeconds`). |
| `Whatsapp__Notifications__MaxAttempts` | 4 | Tentativas totais. |
| `Whatsapp__Notifications__RetryDelaysSeconds__0..n` | 30, 120, 600 | Espera antes da 2ª, 3ª, … tentativa. |
| `Whatsapp__Notifications__LanguageCode` | `pt_BR` | Idioma dos templates. |
| `Whatsapp__Notifications__DelayFirstNoticeMinutes` | 10 | 1º aviso de atraso (X). |
| `Whatsapp__Notifications__DelayRepeatMinutes` | 15 | Intervalo mínimo entre avisos de atraso (Y). |
| `Whatsapp__Notifications__DelayMaxNotices` | 2 | Máximo de avisos de atraso por atendimento (0 desliga). |
| `Whatsapp__Notifications__DelayLookbackMinutes` | 180 | Janela de busca de atendimentos atrasados. |
| `Whatsapp__Notifications__OperationalMaxAgeMinutes` | 30 | Validade de check-in/atraso. |
| `Whatsapp__Notifications__SchedulingMaxAgeHours` | 24 | Validade dos demais. |
| `Whatsapp__Templates__ClientCheckedIn` … `__AppointmentReminder` | vazio | Nome do template aprovado por tipo. Vazio = tipo não enviado. |

Os valores são validados na inicialização fora de Development/Testing (mensagens citam a chave, nunca o valor). Os
nomes de template não são segredo. Credenciais continuam as da Cloud API (`Whatsapp__AccessToken` etc.).

## Privacidade / LGPD

- Conteúdo mínimo: primeiro nome do cliente, nome do profissional, data, hora, minutos de atraso e (só no
  cancelamento por imprevisto) o link de reagendamento. Nunca diagnóstico, informação clínica, prontuário,
  documentos, valores, observações ou motivo do imprevisto.
- Logs: tipo, id da notificação, reserva/visita, tentativa, resultado, wamid e código — sem telefone, nomes,
  parâmetros, link ou credenciais (coberto por teste). O webhook continua mascarando o destinatário.
- **Decisão de consentimento**: o LUMIS não tem hoje mecanismo de preferência/consentimento de comunicação. Estas
  mensagens são **transacionais**, ligadas a um atendimento que o próprio cliente agendou (ou a um check-in do
  próprio profissional), e são enviadas com base na execução do serviço solicitado. **Marketing não é enviado por
  este canal** e exigiria consentimento explícito. Ponto de extensão: o `WhatsAppNotificationComposer` resolve o
  destinatário antes de enviar; uma futura preferência de canal entra ali, marcando o aviso como `SKIPPED` com um
  código próprio, sem mudar a fila nem os endpoints.

## Administração

`GET /api/admin/whatsapp/notifications?status=&type=&page=&pageSize=` (somente papel Administrador): pendentes,
enviadas, entregues, lidas, falhas, motivo e tentativas. Não expõe telefone, nomes, link, token ou segredo. Somente
leitura: não há reenvio manual nem escolha de destinatário.

## Ativação (pendências manuais — nada disso foi feito)

1. Aplicar a migration `20260919031847_AddWhatsAppNotifications` (cria só a tabela `WhatsAppNotifications`).
2. Criar e aprovar os templates acima no WhatsApp Manager (incluindo o botão URL do reagendamento).
3. Configurar `Whatsapp__Templates__*` com os nomes aprovados.
4. Confirmar credenciais da Cloud API e o webhook já existentes.
5. Só então `Whatsapp__Notifications__Enabled=true`. Avisos criados antes disso expiram pelas regras de validade.

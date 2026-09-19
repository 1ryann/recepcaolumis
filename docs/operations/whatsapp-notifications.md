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
            ├─ resolve envios interrompidos (SENDING com lease vencido → UNCONFIRMED; UNCONFIRMED vencido → FAILED)
            └─ um item por vez, até BatchSize:
                 ├─ reivindica UM item: UPDATE … LIMIT 1 FOR UPDATE SKIP LOCKED  (PENDING → PROCESSING, lease)
                 ├─ monta o template a partir dos registros reais (WhatsAppNotificationComposer)
                 ├─ commita PROCESSING → SENDING (+ link de reagendamento, se houver) ANTES de chamar a Meta
                 └─ IWhatsAppService.SendTemplateAsync(…, biz_opaque_callback_data)
                      → ACCEPTED (wamid) | retry (nada saiu) | UNCONFIRMED (resultado desconhecido) | FAILED | SKIPPED
webhook existente (sent/delivered/read/failed) → WhatsAppMessage E a notificação (pelo wamid ou pelo callback data)
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

A regra central: **só se reenvia o que comprovadamente não chegou à Meta.** Uma segunda mensagem é pior do que um
aviso que não saiu (este fica visível ao Admin).

- Cada reivindicação é uma tentativa. Retry só quando **nada saiu**: a Meta respondeu com erro temporário
  (`WHATSAPP_PROVIDER_UNAVAILABLE` — HTTP 5xx, `WHATSAPP_RATE_LIMITED`) ou a conexão nem foi estabelecida
  (`WHATSAPP_NETWORK_ERROR`: DNS, conexão recusada, TLS, proxy). Volta para `PENDING` com espera `RetryDelaysSeconds`
  (padrão 30 s, 2 min, 10 min; o último se repete) até `MaxAttempts` (padrão 4) → depois `FAILED`.
- **Resultado desconhecido** — a requisição pode ter chegado à Meta: `WHATSAPP_TIMEOUT` (sem resposta no prazo),
  `WHATSAPP_OUTCOME_UNKNOWN` (conexão caiu depois do envio), `WHATSAPP_INVALID_RESPONSE` (HTTP 2xx sem wamid legível),
  exceção durante a chamada (`DISPATCH_ERROR`) ou processo que parou dentro da chamada (`DISPATCH_INTERRUPTED`) →
  `UNCONFIRMED`, **nunca retry**. O envio leva `biz_opaque_callback_data = lumis-notification:{id}` (só o id da
  notificação); se a Meta aceitou, o webhook de status traz esse valor e o wamid, e a notificação passa a
  `ACCEPTED/SENT/…` com o wamid. Sem evidência em `UnconfirmedWindowMinutes` (15) → `FAILED/WHATSAPP_OUTCOME_UNKNOWN`,
  visível em `/api/admin/whatsapp/notifications` para a recepção decidir se contata o cliente. Nenhuma confirmação é
  inventada: só o webhook da Meta resolve um `UNCONFIRMED`.
- Falhas **permanentes** (template recusado, destinatário não permitido, autenticação, não configurado…) → `FAILED`
  na hora, sem retry.
- Se a Meta aceitou mas gravar o wamid em `WhatsAppMessages` falhar, o envio continua **aceito** (o webhook cria o
  registro no primeiro status); nunca vira falha nem retry.
- Telefone ausente/inválido → `FAILED/RECIPIENT_PHONE_INVALID`; destinatário inativo/inexistente →
  `FAILED/RECIPIENT_UNAVAILABLE`. O fluxo principal nunca é afetado.
- Situação mudou (visita já em atendimento, reserva não mais aprovada, link já usado) → `SKIPPED/OBSOLETE`.
- Template do tipo não configurado → `SKIPPED/TEMPLATE_NOT_CONFIGURED` (nunca vira texto livre).
- Aviso velho demais para ser útil → `SKIPPED/EXPIRED`: check-in e atraso após `OperationalMaxAgeMinutes` (30);
  demais após `SchedulingMaxAgeHours` (24). Evita disparar avisos antigos quando o envio é ligado.

## Concorrência entre instâncias (lock / lease)

- **Um item por vez.** O dispatcher reivindica uma notificação só quando vai processá-la (`LIMIT 1 … FOR UPDATE SKIP
  LOCKED`). O lease cobre um item, nunca o lote inteiro: `BatchSize` pode crescer sem que itens esperando na fila
  tenham o lease vencido.
- **Dois leases.** `LeaseSeconds` (120) cobre a preparação (`PROCESSING`); ao começar a chamada à Meta o item passa a
  `SENDING` com `SendLeaseSeconds` (90, mínimo 70 > `Whatsapp:TimeoutSeconds` máximo de 60).
- **Escrita condicionada (`xmin`).** Toda escrita do dispatcher depende da versão da linha. Quem perdeu a reivindicação
  (lease vencido e item reivindicado de novo) não consegue gravar — em particular não consegue passar para `SENDING`,
  então **não chama a Meta**.
- **`SENDING` nunca é reivindicado de novo.** Lease de `SENDING` vencido = processo parou dentro da chamada: vira
  `UNCONFIRMED/DISPATCH_INTERRUPTED`, sem reenvio. Se a resposta da chamada original chegar depois, ela ainda grava o
  wamid (mesma tentativa).
- `PROCESSING` com lease vencido (processo morreu antes de enviar) é reivindicado normalmente: nada saiu.

Resultado: duas instâncias nunca enviam a mesma notificação. Coberto por testes que seguram uma chamada "na Meta",
vencem o lease e rodam um segundo dispatcher, e por um teste de escrita com versão obsoleta.

## Status de entrega

O webhook existente atualiza `WhatsAppMessage` e depois a notificação que enviou a mensagem — pelo wamid ou, se o
dispatcher nunca soube o wamid (resultado desconhecido), pelo `biz_opaque_callback_data` ecoado pela Meta, que então
anexa o wamid. `ACCEPTED → SENT → DELIVERED → READ`, só para frente; `FAILED` é terminal com
`WHATSAPP_DELIVERY_FAILED:{código}`. Duplicados e fora de ordem não mudam nada; um wamid já conhecido nunca é
substituído; uma notificação que não foi enviada nunca é resolvida por callback. A notificação é gravada com a mesma
escrita condicionada e reprocessada se o dispatcher a alterou no mesmo instante; como o webhook reaplica o status até
em reentregas, um conflito não perde a atualização. Se o status chegar antes de o dispatcher gravar o wamid, o
dispatcher o aplica ao registrar o aceite.

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
  32 bytes aleatórios e prepara o novo hash com validade `agora + Rescheduling:LinkTtlHours` e a auditoria
  `RESCHEDULE_LINK_ISSUED` (id da notificação como correlação); (c) **commita a rotação no mesmo `SaveChanges` que
  passa a notificação para `SENDING`, antes de chamar a Meta** — se essa escrita falhar (reivindicação perdida ou link
  usado no mesmo instante), nada é rotacionado nem enviado; (d) envia o valor bruto apenas como sufixo do botão URL.
  O valor bruto não vai para tabela, log ou auditoria.
- Concorrência: `RescheduleTokens` e `WhatsAppNotifications` têm token de concorrência (`xmin`). Cliente usando o
  link no mesmo instante → a escrita falha, a notificação volta para retry e a próxima tentativa vê `UsedAt` e pula.
  Um dispatcher que perdeu a reivindicação não consegue rotacionar o link que outro está enviando.

**Qual link vale após uma falha.**
- Meta respondeu com erro / conexão não estabelecida (nada saiu): o retry rotaciona e envia um link novo. O link
  anterior nunca chegou a ninguém, então invalidá-lo não custa nada; vale o link da mensagem entregue.
- Resultado desconhecido (timeout, conexão caída, processo parado): **não há retry nem nova rotação**. O link da
  única mensagem possivelmente entregue continua válido até o fim da validade (contada daquele envio) e não é
  revogado, mesmo que a notificação termine `FAILED/WHATSAPP_OUTCOME_UNKNOWN`.

**Risco aceito.** Um resultado desconhecido em que a Meta *não* entregou e o webhook não trouxe evidência termina em
`FAILED/WHATSAPP_OUTCOME_UNKNOWN`: o cliente não recebe o aviso. Fica visível em `/api/admin/whatsapp/notifications`
para a recepção contatar o cliente. Preferimos isso a arriscar duas mensagens (e, antes desta correção, uma segunda
mensagem invalidava o link da primeira).

## Configuração

| Variável de ambiente | Padrão | Uso |
|---|---|---|
| `Whatsapp__Notifications__Enabled` | `false` | Liga o envio. Desligado: eventos continuam registrados, nada é enviado. |
| `Whatsapp__Notifications__PollIntervalSeconds` | 15 | Intervalo do worker. |
| `Whatsapp__Notifications__BatchSize` | 20 | Itens por ciclo. |
| `Whatsapp__Notifications__LeaseSeconds` | 120 | Lease de um item em preparação (`PROCESSING`). |
| `Whatsapp__Notifications__SendLeaseSeconds` | 90 | Lease durante a chamada à Meta (`SENDING`); mínimo 70. |
| `Whatsapp__Notifications__UnconfirmedWindowMinutes` | 15 | Espera por evidência do webhook antes de `FAILED/WHATSAPP_OUTCOME_UNKNOWN`. |
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
- **Consentimento (opt-in)**: ver [whatsapp-consent.md](whatsapp-consent.md). Nada é enviado a quem não aceitou
  explicitamente: o composer lê o aceite atual do destinatário no envio e marca `SKIPPED/RECIPIENT_NOT_OPTED_IN` ou
  `SKIPPED/RECIPIENT_OPTED_OUT`. Estas mensagens são operacionais; **marketing não é enviado por este canal**.

## Administração

`GET /api/admin/whatsapp/notifications?status=&type=&page=&pageSize=` (somente papel Administrador): pendentes,
enviadas, entregues, lidas, falhas, motivo e tentativas. Não expõe telefone, nomes, link, token ou segredo. Somente
leitura: não há reenvio manual nem escolha de destinatário.

## Implantação e ativação

**Migrations antes do deploy do código.** Os endpoints de check-in, agendamento, cancelamento, reagendamento e
imprevisto gravam em `WhatsAppNotifications` dentro da própria transação, mesmo com o envio desligado. O código só pode
ser publicado num banco que já tenha:

1. `20260919031847_AddWhatsAppNotifications` — cria a tabela (aplicada no staging em 2026-09-19).
2. `20260919042836_WhatsAppNotificationUnknownSendOutcome` — só troca 3 CHECK constraints por versões mais amplas
   (`SENDING`, `UNCONFIRMED`); `xmin` é coluna de sistema e não gera SQL. Compatível com o código anterior
   (aplicada no staging em 2026-09-19).
3. `20260919162155_WhatsAppOptIn` — 4 colunas de aceite em `Customers` e `Professionals` (status com
   `DEFAULT 'NOT_RECORDED'`) e 3 CHECK por tabela. Aditiva; o código anterior continua gravando linhas válidas
   (aplicada no staging em 2026-09-19).

Ativação do envio (pendências manuais — nada disso foi feito):

1. Criar e aprovar os templates acima no WhatsApp Manager (incluindo o botão URL do reagendamento).
2. Configurar `Whatsapp__Templates__*` com os nomes aprovados.
3. Confirmar credenciais da Cloud API e o webhook já existentes, e que os status de entrega trazem
   `biz_opaque_callback_data` (sem isso um resultado desconhecido termina em `FAILED/WHATSAPP_OUTCOME_UNKNOWN`, nunca
   em mensagem duplicada).
4. Pendências de [whatsapp-consent.md](whatsapp-consent.md): texto do aceite aprovado e captura na interface.
5. Só então `Whatsapp__Notifications__Enabled=true`. Avisos criados antes disso expiram pelas regras de validade.

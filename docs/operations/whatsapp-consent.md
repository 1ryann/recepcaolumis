# Decisão: consentimento (opt-in) para WhatsApp

Status: **decidida e implementada (backend e interface)**; migration `20260919162155_WhatsAppOptIn` aplicada no
staging. O envio continua desligado (`Whatsapp__Notifications__Enabled=false`) — ver "Pendências antes de ligar o
envio".

## Contexto

A auditoria do código confirmou que o LUMIS **não tinha nenhum registro** de consentimento ou preferência de
comunicação: `Customer` guardava só nome, telefone e ativo; `Professional`, o WhatsApp de contato. Nenhuma tela
perguntava nada sobre WhatsApp. As notificações operacionais (fila `WhatsAppNotifications`) enviariam a qualquer
número cadastrado.

## Duas coisas diferentes

1. **Base legal (LGPD) para usar o telefone.** As mensagens desta fase são **operacionais/transacionais**, sobre um
   atendimento que a própria pessoa agendou (cliente) ou sobre a rotina de trabalho no prédio (profissional). A base
   é a execução do contrato / procedimentos a pedido do titular (art. 7º, V), não o consentimento (art. 7º, I).
   Continuam valendo transparência, minimização e o direito de oposição (art. 18).
2. **Permissão do canal WhatsApp.** A política do WhatsApp Business exige que o destinatário **aceite (opt-in)**
   receber mensagens da empresa no WhatsApp antes de mensagens iniciadas pela empresa, e que pedidos de saída sejam
   respeitados. Isso vale mesmo para mensagens transacionais.

Por isso o LUMIS **não trata "informou o telefone" como aceite**: registra um opt-in explícito de canal, por
destinatário, e só envia com ele. A interpretação jurídica acima deve ser validada pelo controlador (jurídico do
LUMIS); o mecanismo foi desenhado para funcionar também se a base escolhida for o consentimento.

**Marketing não faz parte desta fase.** Nenhum template de marketing existe; este opt-in cobre só avisos
operacionais. Comunicação de marketing exigiria um consentimento próprio, separado, específico e opcional — não é
este registro e não pode ser inferido dele.

## Decisão

- **Registro por destinatário** (`Customers` e `Professionals`), quatro colunas:
  `WhatsAppOptInStatus` (`NOT_RECORDED` | `GRANTED` | `REVOKED`), `WhatsAppOptInChangedAt`, `WhatsAppOptInSource`
  (onde foi coletado) e `WhatsAppOptInTextVersion` (qual texto a pessoa viu). O padrão é `NOT_RECORDED`: **nenhum
  opt-in é presumido**, nem para os registros que já existem.
- **Coleta só por ação explícita** (caixa desmarcada por padrão, que a pessoa marca):

  | Onde (tela) | API | Origem gravada | Quem decide |
  |---|---|---|---|
  | Cadastro do cliente (`/cliente/cadastro`) | `POST /api/customer/register`, `whatsAppOptIn` | `CUSTOMER_REGISTRATION` | o cliente |
  | Agendamento iniciado no Totem pelo QR, feito no celular e na conta do cliente (`/cliente/agendar?handoff=…`) | `POST /api/customer/reservations`, `whatsAppOptIn` + `handoffToken` | `TOTEM` | o cliente, no próprio celular |
  | Agendamento na área do cliente (`/cliente/agendar`) | `POST /api/customer/reservations`, `whatsAppOptIn` | `CUSTOMER_PORTAL` | o cliente |
  | Área do cliente (card "Avisos por WhatsApp") | `PUT /api/customer/me/whatsapp-opt-in` | `CUSTOMER_PORTAL` | o cliente |
  | Área do profissional (`/profissional/perfil`) | `PUT /api/professional/me/whatsapp-opt-in` | `PROFESSIONAL_PORTAL` | o profissional (só ele) |
  | Recepção, pessoa no balcão (`/recepcao/whatsapp`) | `POST /api/reception/whatsapp-opt-in` | `RECEPTION` | o cliente presente; o atendente confirma que leu o texto e é auditado |
  | Agendamento assistido (sem tela hoje) | `POST /api/reception/reservations`, `whatsAppOptIn` | `RECEPTION` | o cliente presente |

  Campo ausente ou `false` num cadastro/agendamento **não muda nada** — um agendamento nunca retira nem concede sem
  a caixa marcada. A caixa só aparece no agendamento enquanto o cliente não autorizou.
- **Retirada a qualquer momento, sem condição:** área do cliente, área do profissional e, para quem não tem conta
  (clientes criados pelo Totem), a recepção por número (`/recepcao/whatsapp` → `POST /api/reception/whatsapp-opt-out`),
  que retira em todos os registros com aquele número. Uma oposição é registrada mesmo sem opt-in anterior.
- **A recepção nunca autoriza um profissional**; só registra retirada. O painel mostra apenas o nome mascarado
  ("Maria S."), se o cadastro tem conta e o status — nunca o nome completo nem o número. O número vai só no corpo das
  requisições, nunca na URL.
- **Portão no envio.** O `WhatsAppNotificationComposer` lê o estado **atual** do registro real no momento do envio:
  sem opt-in → `SKIPPED/RECIPIENT_NOT_OPTED_IN`; retirado → `SKIPPED/RECIPIENT_OPTED_OUT`. Uma retirada feita
  depois de o evento entrar na fila é respeitada. A verificação vem antes de qualquer preparo: nenhum link de
  reagendamento é emitido para quem não vai receber.
- **Número novo, opt-in novo.** O aceite vale para o número em que foi dado. Se o profissional troca o WhatsApp, o
  registro volta a `NOT_RECORDED`. (O telefone do cliente não é editável hoje.)
- **Prova.** Cada mudança gera auditoria `WHATSAPP_OPT_IN_GRANTED` / `WHATSAPP_OPT_IN_REVOKED` com ator, registro
  alvo, IP e correlação — **sem o número**. O registro guarda origem, instante e versão do texto.
- **Versão do texto.** `whatsapp-operacional-v1`. Mudança material do texto → nova versão no código; cada aceite
  continua registrado com a versão que a pessoa viu.

### Texto v1 (proposto — precisa de aprovação antes de ir para a interface)

- Cliente: "Quero receber no WhatsApp deste número avisos do LUMIS sobre meus atendimentos (confirmação,
  reagendamento, cancelamento e atraso). Posso cancelar quando quiser na minha área do cliente ou na recepção."
- Profissional: "Quero receber no WhatsApp deste número avisos operacionais do LUMIS, como a chegada dos meus
  clientes. Posso cancelar quando quiser na minha área."

## Totem e a posse do número (decisão de 2026-09-19)

**Problema.** O Totem é uma tela pública e anônima. Uma caixa de aceite ali permitiria a qualquer pessoa digitar o
número de outra e autorizar mensagens para ela — um checkbox no quiosque não prova nada sobre o número.

**Como o Totem funciona de fato.** O agendamento que começa no Totem não é feito no quiosque: o Totem mostra um QR
(`/api/totem/booking-handoffs`), a pessoa o lê com o **próprio celular**, entra (ou cria) a **sua conta** e agenda em
`/cliente/agendar?handoff=…`. O endpoint anônimo `POST /api/totem/reservations` existe, mas nenhuma tela o usa.

**Decisão.**
- O aceite da jornada do Totem é coletado **no celular da pessoa, dentro da conta dela**, na tela de agendamento do
  QR, e gravado como origem `TOTEM` (a jornada começou no Totem; a decisão foi tomada no celular).
- O endpoint anônimo do quiosque **recusa** um aceite (`400 WHATSAPP_OPT_IN_NOT_AVAILABLE_HERE`): o Totem público não
  registra opt-in em nenhuma hipótese.
- **Isso não prova a posse do número**: o telefone da conta foi informado pela própria pessoa no cadastro e não é
  verificado (não há código por SMS/WhatsApp). O que muda é quem decide: a própria pessoa, autenticada, no próprio
  aparelho — não qualquer um diante de uma tela pública. O mesmo vale para o cadastro e para a recepção (onde o
  atendente atesta a presença e a concordância, mas não a titularidade da linha).
- **Risco residual aceito**, com mitigação: a primeira mensagem identifica o LUMIS e o atendimento; a retirada é
  imediata pela área do cliente ou pela recepção. **Evolução prevista** para prova de posse: aceite iniciado pela
  própria pessoa pelo WhatsApp (link `wa.me` com uma palavra-chave), que chega pelo webhook já vinculado ao número de
  quem enviou — o mesmo canal de entrada do "SAIR" abaixo.

## Preparado para "SAIR" pelo WhatsApp (não implementado)

Hoje o webhook só **conta** as mensagens recebidas, não as processa. Para quando for implementado:
- **Um único caminho de retirada por número:** `WhatsAppOptInService.RevokeByPhoneAsync(numero, origem, ator)`, já
  usado pela recepção. O tratamento de mensagem recebida deve chamá-lo com o número de quem enviou (`wa_id`), um ator
  sem usuário e uma origem nova (ex.: `WHATSAPP_REPLY`), sem lógica própria. A auditoria e o portão no envio já
  funcionam a partir daí.
- **O que falta:** processar mensagens recebidas no webhook (palavras como SAIR/PARAR/STOP), a origem nova no enum e
  na CHECK `CK_*_WhatsAppOptInSource` (migration própria), e a resposta de confirmação dentro da janela de 24 h.

## Alternativas descartadas

| Alternativa | Por que não |
|---|---|
| Considerar o telefone informado como aceite | A política do WhatsApp exige opt-in explícito; seria um aceite "para passar no teste". |
| Caixa já marcada / aceite embutido nos termos | Não é manifestação livre e inequívoca. |
| Marcar clientes existentes como aceitos (backfill) | Ninguém viu texto nenhum. Existentes ficam `NOT_RECORDED`. |
| Tabela de opt-in por número | Duplicaria o telefone (mais dado pessoal) sem ganho; o registro fica no próprio destinatário. |
| Profissional aceito pelo Admin | O aceite é da pessoa; o Admin só pode registrar retirada via recepção. |

## Limitações conhecidas

- **Posse do número não é verificada** em nenhuma origem (ver "Totem e a posse do número").
- **Saída respondendo "SAIR" no WhatsApp** não existe ainda (ver seção própria).
- **Não há tela de agendamento assistido** na recepção; o aceite presencial é feito no painel `/recepcao/whatsapp`.
- Profissional sem conta vinculada não tem como aceitar; os avisos de chegada para ele ficam `SKIPPED`.

## Pendências antes de ligar o envio

1. Validação jurídica do texto v1 (aprovado pelo responsável para uso na interface em 2026-09-19).
2. Deploy do código com as migrations já aplicadas no ambiente (staging: as três aplicadas em 2026-09-19).
3. Só então considerar `Whatsapp__Notifications__Enabled=true`. Até lá, e para todo destinatário sem aceite, os avisos
   ficam registrados e terminam `SKIPPED` — nada é enviado.

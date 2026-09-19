# WhatsApp Cloud API — infraestrutura base

Escopo desta base: configuração, um cliente único (`IWhatsAppService`: texto livre e **templates**), um envio de
teste acionado manualmente por administrador e o webhook (verificação + recebimento assinado). As notificações
operacionais automáticas (check-in, cancelamento, atraso, reagendamento, confirmação) usam este mesmo cliente por
meio de uma fila durável — ver [whatsapp-notifications.md](whatsapp-notifications.md). Não há chatbot. O antigo
provider síncrono `Notifications:Provider` (Demo/Meta) foi removido.

## Configuração (seção `Whatsapp`)

| Variável de ambiente | Tipo | Uso |
|---|---|---|
| `Whatsapp__FinanceiroPhoneNumber` | pública | Já existente: link do WhatsApp financeiro (Room Rental). Preservada. |
| `Whatsapp__PhoneNumberId` | pública | Phone Number ID do número remetente na Meta. Somente dígitos. |
| `Whatsapp__BusinessAccountId` | pública | ID numérico da WABA. Opcional nesta fase (não é usado no envio). |
| `Whatsapp__ApiVersion` | pública | Versão da Graph API no formato `vNN.N`, conforme o app da Meta. Sem valor padrão. |
| `Whatsapp__BaseUrl` | pública | Padrão `https://graph.facebook.com`. Deve ser HTTPS absoluta. |
| `Whatsapp__TimeoutSeconds` | pública | Padrão 10; aceito de 1 a 60. |
| `Whatsapp__AccessToken` | **secret** | Token do System User. Nunca em appsettings, código, logs ou commits. |
| `Whatsapp__VerifyToken` | **secret** | Valor escolhido pelo operador e repetido no painel de webhook da Meta. |
| `Whatsapp__AppSecret` | **secret** | App Secret do app da Meta; valida `X-Hub-Signature-256`. |

Todos os valores são opcionais para a aplicação subir. Sem `PhoneNumberId`, `AccessToken`, `ApiVersion` e `BaseUrl`
o envio responde `WHATSAPP_NOT_CONFIGURED` sem chamar a Meta. Sem `VerifyToken` a verificação do webhook responde 403;
sem `AppSecret` todo POST do webhook responde 401. Fora de Development/Testing, valores **malformados** impedem o
startup; as mensagens de erro citam a chave, nunca o valor.

### Local (Development)

Os secrets ficam em user-secrets do projeto `recepcaototem`, inseridos pelo próprio operador no terminal
(não colar em chat, arquivo versionado ou `.env`). Não usar `dotnet user-secrets list`, que imprime todos os valores.

## Endpoints

| Método e rota | Acesso | Função |
|---|---|---|
| `POST /api/admin/whatsapp/test` | Política `Administration` (somente `ADMINISTRADOR`) + header `X-CSRF-TOKEN` | Envia um texto de teste. Body estrito `{ "phoneNumber": "...", "message": "..." }` (1–1000 caracteres). |
| `GET /api/whatsapp/webhook` | Anônimo; autenticado por `hub.verify_token` | Handshake de assinatura: devolve `hub.challenge` em `text/plain`. |
| `POST /api/whatsapp/webhook` | Anônimo; autenticado por HMAC-SHA256 do corpo bruto com `AppSecret` | Recebe eventos (limite 1 MiB). Registra apenas contagens; não persiste. |

Respostas do teste: 200 (`messageId`), 400 (payload inválido), 503 (`WHATSAPP_NOT_CONFIGURED`), 504
(`WHATSAPP_TIMEOUT`), 502 (demais falhas da Meta, com código estável como `WHATSAPP_AUTH_FAILED`,
`WHATSAPP_RECIPIENT_NOT_ALLOWED` ou `WHATSAPP_OUTSIDE_SERVICE_WINDOW`). Cada tentativa gera auditoria
`WHATSAPP_TEST_REQUESTED` e `WHATSAPP_TEST_SUCCEEDED`/`WHATSAPP_TEST_FAILED`, sem telefone nem texto.

## Eventos de status recebidos pelo webhook

`POST /api/whatsapp/webhook` processa os status de mensagens enviadas: `sent`, `delivered`, `read` e `failed`
(qualquer outro valor entra como `Unknown` e é registrado sem interpretação). De cada status são extraídos o
message id (`wamid`), o destinatário, o instante, o Phone Number ID, o WABA ID e, quando há falha, o código e o
título do erro da Meta. Mensagens recebidas (inbound) são apenas contadas: conteúdo, nome e número de quem
escreveu não são lidos nem registrados.

A fonte de verdade é a tabela **`WhatsAppMessages`** (`IWhatsAppMessageStore`), com índice único no `wamid`. O
`WhatsAppStatusRecorder` em memória continua existindo, limitado a 200 eventos, só para telemetria e testes.

Idempotência: o `wamid` único e as transições só para frente (`ACCEPTED → SENT → DELIVERED → READ`, com `FAILED`
terminal) garantem que reentrega, evento fora de ordem ou segunda instância não dupliquem linha nem façam o
status regredir. O envio grava o `wamid` assim que a Cloud API aceita; se o webhook chegar primeiro, ele cria a
linha e o envio apenas reconcilia.

A migration `AddWhatsAppMessages` cria só essa tabela e **não foi aplicada em nenhum banco**.

Nos logs o destinatário aparece mascarado (`***8007`), e token, verify token, app secret e conteúdo de mensagem
nunca são registrados.

## Railway (apenas documentação — nada foi alterado)

Quando autorizado, cadastrar no serviço do ambiente correspondente: `Whatsapp__PhoneNumberId`, `Whatsapp__ApiVersion`
e, como variáveis sensíveis, `Whatsapp__AccessToken`, `Whatsapp__VerifyToken` e `Whatsapp__AppSecret`.
`Whatsapp__BaseUrl` e `Whatsapp__TimeoutSeconds` só se diferirem do padrão. A URL pública do webhook a registrar
no painel da Meta é `https://<host-do-ambiente>/api/whatsapp/webhook`.

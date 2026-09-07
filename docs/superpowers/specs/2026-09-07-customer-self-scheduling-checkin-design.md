# LUMIS — Customer, autoagendamento e QR check-in

**Status:** especificação final para revisão; nenhuma implementação ou migration nesta etapa.

**Escopo:** introduzir `Customer` como cliente/visitante, permitir autoagendamento autenticado e agendamento presencial sem conta, e permitir check-in restrito por QR/token.

**Fora do escopo:** frontend, Totem UI, webcam, foto, documento, CPF, OTP, SMS, WhatsApp, Intelbras, Financeiro novo, Supabase, produção, deploy e alteração de banco.

## 1. Pré-condições e divergência observada

O desenho assume a existência dos módulos já aprovados de `Professional`, `Room`, `Lease`, `Reservation`, `Visit`, expediente, bloqueios, disponibilidade, auditoria e PostgreSQL. A inspeção da branch atual `codex/leases-design` encontrou no código versionado apenas a fundação, Identity, auditoria inicial, `Professional`, `Room`, `PrivateFile` e APIs correspondentes. Os tipos e endpoints de `Reservation`, `Visit`, `Lease` e disponibilidade ainda não estão presentes nessa branch, embora existam documentos de desenho e o contexto do projeto os trate como módulos concluídos em outra linha de trabalho.

Essa divergência não será resolvida por esta spec. A implementação do núcleo Customer deve começar somente na branch que contenha os módulos operacionais reais ou, alternativamente, após portar esses módulos sem mudar suas regras aprovadas. A migration Customer não pode ser gerada contra um modelo parcial.

O código existente confirma os seguintes contratos que devem ser preservados:

- `ApplicationUser` é ASP.NET Core Identity com `DisplayName`, `IsActive` e `MustChangePassword`.
- As roles atuais são `ADMINISTRADOR`, `GERENTE` e `PROFISSIONAL`.
- A sessão usa cookie seguro, antiforgery por `X-CSRF-TOKEN`, políticas server-side e erros genéricos.
- O telefone brasileiro já é normalizado por `WhatsAppNormalizer` para E.164, com `+55` implícito para 10 ou 11 dígitos nacionais.
- O timezone técnico configurado para o produto é `America/Porto_Velho`.
- A arquitetura usa PostgreSQL local em Development e não executa migrations automaticamente no startup.

## 2. Vocabulário e separação de conceitos

`Tenant` continua sendo o locatário/pagador de uma `Lease`. Ele não é um cliente de atendimento e não será reutilizado para identificar visitante.

`Customer` é a pessoa que recebe o atendimento. Uma `Reservation` pode apontar para um `Customer`; uma `Visit` pode preservar o mesmo vínculo. `Customer` pode existir sem conta Identity ou estar vinculado a uma conta própria.

Não haverá deduplicação por nome. O telefone canônico será a chave operacional do fluxo presencial, e o GUID continuará sendo a identidade técnica.

## 3. Modelo Customer

```text
Customer
  Id: Guid
  Name: string, obrigatório, máximo 200
  Phone: string, obrigatório, E.164 canônico, máximo 16 caracteres
  NormalizedPhone: string, obrigatório, máximo 16, único
  ApplicationUserId: string?, FK para AspNetUsers, índice único filtrado
  IsActive: bool, padrão true
  CreatedAt: DateTimeOffset UTC
  UpdatedAt: DateTimeOffset UTC
  Version: xmin/concurrency token PostgreSQL
```

`Phone` será armazenado somente no formato E.164 produzido pelo backend. `NormalizedPhone` será o mesmo valor canônico nesta etapa, mantendo uma invariável explícita entre os dois campos e uma chave de lookup estável. Nenhum formato digitado, máscara ou filename é persistido.

Regras:

- `Name` recebe trim e colapso de whitespace; não é chave única.
- `Phone` é obrigatório para Customer com e sem conta.
- `ApplicationUserId` é opcional e único quando preenchido.
- A FK Customer → Identity usa `NoAction`; não apagar Customer quando uma conta for removida.
- Customer inativo não pode criar reserva, emitir token ou fazer check-in. O histórico permanece consultável por administradores conforme a política existente.
- Desativar Customer não desativa sua conta Identity; desativar a conta não apaga nem altera Customer automaticamente.
- Não haverá CPF, CNPJ, endereço, documento, foto ou dados financeiros no modelo.

### 3.1 Normalização de telefone

Reutilizar uma única função de normalização compartilhada pelo domínio, baseada no comportamento E.164 existente:

- aceitar máscara, espaços e pontuação para números brasileiros;
- aceitar 10 ou 11 dígitos nacionais e prefixar `+55` quando não houver DDI;
- aceitar número internacional somente com `+` e no máximo 15 dígitos após o sinal;
- rejeitar letras, ramais, valores vazios, quantidade inválida e internacional sem `+` ambíguo;
- comparar e persistir o valor canônico, sem depender de collation;
- criar índice único permanente em `NormalizedPhone`, incluindo Customers inativos.

Criação concorrente deve tentar a gravação e tratar a violação do índice conhecido como erro controlado `CUSTOMER_PHONE_ALREADY_EXISTS`, sem expor SQL. A validação prévia é apenas amigável; o índice é a garantia definitiva.

## 4. Contas Customer e autorização

Adicionar a role controlada `CUSTOMER` e uma policy de usuário ativo para recursos próprios. A role não substitui `ADMINISTRADOR`, `GERENTE` ou `PROFISSIONAL`, e uma conta de Customer não pode receber acesso às policies administrativas. A implementação não criará uma constraint geral de uma role por usuário, mas o provisionamento Customer recusará contas que já possuam role administrativa ou profissional.

O login existente por cookie poderá autenticar Customer depois que o conjunto permitido de roles e a policy forem ampliados. A semântica de cookie seguro, `HttpOnly`, `Secure`, `SameSite` e antiforgery não será relaxada.

### 4.1 Cadastro remoto

Propor `POST /api/customer/register`, anônimo, com antiforgery e rate limit próprio. O request contém `name`, `phone`, `email`, `password` e `confirmation`; não contém `CustomerId`, roles ou flags de ativação.

O fluxo cria Identity e Customer somente se o e-mail e o telefone ainda não estiverem usados. A role atribuída é apenas `CUSTOMER`, `IsActive` inicia `true` e a conta não recebe roles administrativas. Respostas para e-mail ou telefone já existentes devem ser genéricas, sem confirmar qual dado provocou a colisão.

Sem OTP/SMS nesta etapa, uma conta nova **não** será vinculada automaticamente a um Customer presencial já existente. Se o telefone já estiver cadastrado, o cadastro remoto será recusado de forma genérica; o vínculo posterior exigirá operação administrativa específica ou uma etapa futura de verificação de posse. Nome e telefone não são prova de identidade.

Auditar criação e vínculo de conta sem registrar senha, hash, token, telefone completo ou e-mail em `ChangedFields`.

## 5. Reservation e Visit

Adicionar `CustomerId` nullable a `Reservation` e `Visit` para preservar linhas históricas administrativas existentes. Linhas antigas permanecem com `NULL`; não haverá backfill inferido por nome ou telefone.

```text
Reservation.CustomerId: Guid?, FK Customer, NoAction, índice não único
Visit.CustomerId: Guid?, FK Customer, NoAction, índice não único
```

Quando uma Reservation tiver Customer, a aplicação valida que o Customer está ativo no momento da criação ou alteração. O cliente autenticado nunca informa `CustomerId`; o backend resolve `Customer.ApplicationUserId` a partir do principal.

Quando uma Visit vier de Reservation, `Visit.CustomerId` deve ser igual ao Customer da Reservation e `VisitorName` continua sendo um snapshot histórico para compatibilidade e auditoria operacional. Uma visita manual sem Customer pode permanecer sem esse vínculo se o contrato atual permitir. O check-in nunca inicia atendimento: a Visit nasce `AGUARDANDO`.

Reservation e Visit mantêm ciclos independentes:

- término de Reservation não encerra Visit;
- término de Lease não encerra Visit;
- cancelamento/rejeição de Reservation impede novo check-in;
- nenhuma operação Customer cancela ou reescreve Reservation automaticamente;
- o fluxo de Visit continua `AGUARDANDO → EM_ATENDIMENTO → ENCERRADA`, com cancelamento conforme o domínio atual.

## 6. Disponibilidade e autoagendamento

Não criar entidade `Slot`. Disponibilidade é uma projeção calculada pelo serviço central existente ou pelo adaptador equivalente da branch que contenha Reservations/Leases.

O serviço deve considerar, no mesmo fuso operacional:

- Professional ativo;
- Room ativa;
- OperatingHours;
- RoomBlocks ativos;
- Reservations que bloqueiam recursos;
- Lease e LeaseOccurrence válidos;
- conflitos de sala e de profissional;
- concorrência no momento da gravação.

O cliente escolhe Professional, data e intervalo retornado. O cliente nunca escolhe RoomId; o backend resolve uma sala compatível dentro da transação. O request de criação repete toda a validação, mesmo que o intervalo tenha vindo de uma consulta anterior.

Para evitar uma regra de negócio implícita, o contrato deve trabalhar com intervalo exato `startAt`/`endAt` e uma duração validada pelo backend. A primeira implementação usará uma grade técnica de 15 minutos, duração mínima de 15 minutos, máxima de 8 horas e duração em múltiplos de 15; isso é granularidade de consulta, não arredondamento de cobrança. O endpoint retorna somente inícios que cabem integralmente no expediente e não conflitam. O horário recebido é interpretado no timezone operacional e persistido como UTC.

Datas civis são convertidas com `OperationalTimeZone.GetCivilDayInterval`. Não usar `DateTime.Now`, collation do banco ou conversão local do navegador como fonte de verdade.

### 6.1 Transação de confirmação

A criação de Reservation Customer deve:

1. resolver Customer pelo usuário autenticado ou pelo fluxo presencial;
2. revalidar Professional/Room/OperatingHours/Blocks/Leases/Reservations;
3. adquirir os locks de recurso já definidos para a implementação de agenda, em ordem determinística para Professional e Room;
4. repetir a consulta depois do lock;
5. inserir Reservation e eventual token de check-in;
6. inserir auditoria e fazer commit na mesma transação.

Se outra solicitação ocupar o intervalo primeiro, responder conflito controlado, sem Reservation parcial e sem auditoria de sucesso. `xmin`/`concurrencyToken` continua protegendo mutações de entidades que já possuem esse contrato; não aceitar token enviado pelo cliente para substituir a revalidação de disponibilidade.

## 7. Endpoints Customer

Os nomes abaixo são contratos propostos; a implementação deve seguir os padrões reais de route groups, DTOs estritos, policies e erros da branch operacional.

```text
POST /api/customer/register
GET  /api/customer/me
GET  /api/customer/professionals
GET  /api/customer/availability?professionalId={guid}&date=YYYY-MM-DD&durationMinutes=60
GET  /api/customer/reservations
GET  /api/customer/reservations/{id:guid}
POST /api/customer/reservations
POST /api/customer/reservations/{id:guid}/cancel
POST /api/customer/reservations/{id:guid}/reschedule
POST /api/customer/reservations/{id:guid}/check-in-token
```

Regras:

- `CUSTOMER` autenticado só consulta Reservations cujo CustomerId pertence ao principal.
- Rota por GUID de outro Customer retorna `404`, sem revelar existência.
- Cancelamento e reagendamento respeitam estados e antecedência mínima já definidos para Reservation.
- `POST /reservations` aceita ProfessionalId e intervalo; não aceita CustomerId nem RoomId.
- O vínculo com Customer é resolvido pelo `ApplicationUserId` autenticado.
- O endpoint de token só retorna o token bruto na resposta autorizada de emissão; não retorna hash nem dados internos.
- GETs não usam antiforgery; POSTs usam antiforgery e rate limit apropriado.

DTOs Customer não expõem roles Identity, hashes, stamps, claims, tokens, Tenant, reservas de terceiros ou dados financeiros globais.

## 8. Fluxo presencial sem conta

O Totem futuro terá uma superfície separada de `/api/totem`, sem reutilizar endpoints administrativos ou exigir sessão completa.

```text
GET  /api/totem/professionals
GET  /api/totem/availability?professionalId={guid}&date=YYYY-MM-DD&durationMinutes=60
POST /api/totem/customers/resolve
POST /api/totem/reservations
POST /api/totem/check-in/resolve
POST /api/totem/check-in/confirm
```

O request de resolução contém `name` e `phone`. O backend normaliza o telefone e responde apenas:

```json
{ "exists": true, "maskedName": "Carlos O." }
```

ou:

```json
{ "exists": false }
```

Não retornar CustomerId, telefone, e-mail, histórico ou objeto Customer. A confirmação `SIM, CONTINUAR` serve somente para prosseguir com uma nova Reservation. Ela não autentica o visitante nem permite consultar reservas antigas. `NÃO SOU EU` não modifica Customer e encerra o fluxo de identidade.

Na criação presencial, o backend repete a resolução por telefone, nunca aceita CustomerId e nunca altera automaticamente o nome de um Customer existente. Se não existir, cria Customer com nome e telefone normalizados; se existir, reutiliza-o para a nova Reservation após a confirmação do fluxo. A confirmação é uma decisão de UX, não uma autenticação: no MVP não haverá token de continuação separado. O request de criação reenviará nome e telefone, que serão normalizados e resolvidos novamente; isso pode criar somente uma nova Reservation e nunca libera histórico, perfil ou check-in de outra reserva. Race conditions são tratadas pelo índice único e o abuso é limitado por rate limiting.

## 9. Check-in token e QR

Criar uma entidade técnica separada, sem colocar o token dentro de Reservation:

```text
CheckInToken
  Id: Guid
  ReservationId: Guid, FK NoAction, índice único por Reservation
  TokenHash: bytea, SHA-256 do token bruto, índice único
  IssuedAt: DateTimeOffset UTC
  ExpiresAt: DateTimeOffset UTC
  RevokedAt: DateTimeOffset?
  UsedAt: DateTimeOffset?
  Version: xmin
```

O token bruto tem pelo menos 32 bytes aleatórios criptograficamente seguros, codificados em Base64Url. Não é derivado de ReservationId, CustomerId ou horário. O banco armazena somente o hash; o bruto aparece uma vez na resposta de emissão para compor o QR. Não persistir imagem QR.

Um token ativo corresponde a uma Reservation Customer aprovada. Cancelamento, rejeição e reagendamento revogam o token anterior; a Reservation substituta recebe token novo quando elegível. A rotação ocorre na mesma transação da mudança de estado.

Janela objetiva de MVP:

```text
Reservation.Status = APPROVED
e Customer ativo
e now >= StartAt - 1 hora
e now < EndAt
e token não revogado e não expirado
```

Assim, o agendamento aparece operacionalmente uma hora antes, não aceita check-in dias antes e deixa de aceitar nova chegada no instante de `EndAt`. O fim do horário não encerra Visit já aberta.

## 10. QR não é login

O token só autoriza localizar uma Reservation elegível e confirmar chegada. Ele não cria cookie, sessão Customer, claim, login administrativo ou acesso a histórico.

`POST /api/totem/check-in/resolve` valida o token e retorna apenas dados mínimos: Professional, Room, janela local e estado de elegibilidade. Não retorna CustomerId, telefone completo, e-mail, Tenant ou outras Reservations.

`POST /api/totem/check-in/confirm` revalida o token e todos os vínculos dentro de uma transação. Se não houver Visit aberta para a Reservation, cria Visit `AGUARDANDO`, com CustomerId e snapshot de `VisitorName`. Se já existir Visit aberta equivalente, retorna a mesma representação mínima de forma idempotente. Visit encerrada/cancelada não é reaberta pelo QR.

Resolução, confirmação e erros usam respostas genéricas. Não diferenciar externamente token inexistente, expirado, revogado ou pertencente a Reservation inválida. Nenhum token bruto aparece em log, auditoria, URL, exception ou mensagem de erro.

## 11. Rate limiting e privacidade

Os endpoints Customer/Totem terão limites próprios, separados dos limites administrativos e do login:

- cadastro: controles cumulativos por IP e identificador de e-mail derivado;
- lookup de telefone: por IP e identificador derivado do telefone;
- criação de Reservation: por IP e por contexto/telefone derivado;
- resolução e confirmação de QR: por IP e identificador derivado do hash do token.

Usar `RemoteIpAddress` até que proxies confiáveis sejam configurados. Não confiar em `X-Forwarded-For`. Limites, janela e contagem serão configuráveis fora do código somente quando a infraestrutura já suportar configuração segura.

Logs e auditoria não conterão token bruto, hash de token, telefone completo, senha, cookie, body, QR, dados de Identity ou histórico desnecessário. O lookup presencial não gera evento de auditoria por tentativa; criação, vínculo, emissão/revogação e check-in geram eventos técnicos mínimos.

## 12. Auditoria

Adicionar alvos controlados `CUSTOMER`, `RESERVATION`, `CHECK_IN_TOKEN` e `VISIT`, conforme os módulos existentes já permitirem. Ações mínimas:

```text
CUSTOMER_CREATED
CUSTOMER_ACCOUNT_LINKED
RESERVATION_CREATED
CHECK_IN_TOKEN_ISSUED
CHECK_IN_TOKEN_REVOKED
VISIT_CHECKED_IN
```

Cada sucesso de mutação grava ator, timestamp, alvo técnico e resultado na mesma transação da mutação. `ChangedFields` contém apenas nomes controlados quando aplicável; nunca valores pessoais, token ou telefone. Tentativas inválidas anteriores à mutação não geram auditoria de negócio.

## 13. Persistência e migration prevista

A implementação futura deverá gerar uma migration PostgreSQL **aditiva**, sem alterar collation, apagar dados, reescrever histórico ou tocar em Identity além de dados de role.

Mudanças previstas:

1. criar `Customers` com índice único permanente em `NormalizedPhone` e índice único filtrado em `ApplicationUserId`;
2. adicionar nullable `CustomerId` em `Reservations` e `Visits`, índices e FKs `NoAction`;
3. criar `CheckInTokens` com FKs, índices únicos e campos de revogação/expiração;
4. não backfillar linhas antigas;
5. não criar tabela de Slots;
6. não armazenar QR, senha ou token em texto claro;
7. provisionar role `CUSTOMER` por ferramenta/rotina controlada, nunca pela migration e nunca automaticamente no startup.

Antes de aplicar em Development, gerar e revisar SQL idempotente. Nenhuma migration será aplicada a Supabase ou produção nesta etapa.

## 14. Compatibilidade

- Reservations administrativas sem Customer continuam válidas.
- Visits históricas sem Customer continuam válidas.
- Finance usa Lease/Tenant e não assume `Tenant == Customer`.
- Leases e LeaseOccurrences continuam sendo fonte de ocupação; Customer não cria exceção de disponibilidade.
- Alertas e Dashboard passam a poder projetar Customer somente quando a relação existir; suas regras atuais não são duplicadas.
- Endpoints administrativos existentes continuam aceitando o fluxo sem Customer quando esse comportamento já for suportado.
- Nenhum endpoint Customer recebe `ProfessionalId` para resolver identidade de usuário; o vínculo profissional futuro continua sendo derivado pelo principal autenticado nos endpoints próprios.

## 15. Testes obrigatórios antes da implementação ser considerada pronta

### Domínio e persistência

- normalização de telefone brasileiro com máscara e E.164;
- rejeição de letras, ramais, vazio e internacional ambíguo;
- índice único aceitando Customer inativo como bloqueador;
- corrida de dois Customers com o mesmo telefone;
- vínculo único de ApplicationUser;
- FK NoAction e preservação de histórico;
- migration aditiva e ausência de DROP/ALTER destrutivo;
- Customer sem conta e Customer com conta.

### Cadastro e autorização

- cadastro remoto cria somente role `CUSTOMER`;
- conta Customer não acessa Operations;
- duplicidade de e-mail/telefone não permite enumeração;
- Customer consulta somente o próprio perfil e Reservations;
- IDOR por Reservation/Customer de terceiros retorna `404`;
- cadastro, cancelamento e reagendamento exigem antiforgery;
- rate limits de IP e identificador são cumulativos.

### Disponibilidade e Reservation

- slot usa `America/Porto_Velho` e limites civis corretos;
- expediente fechado e RoomBlock removem o intervalo;
- Lease, LeaseOccurrence, Reservation e Professional conflitando removem o intervalo;
- request com RoomId ou CustomerId arbitrário é rejeitado/ignorado;
- duas confirmações simultâneas resultam em uma Reservation e um conflito controlado;
- criação Customer não altera Reservation de terceiros;
- cancelamento/reagendamento respeita a antecedência mínima atual.

### QR e Visit

- token possui alta entropia, não contém IDs/PII e somente seu hash é persistido;
- janela de uma hora antes até `EndAt` é aplicada;
- token cancelado, rejeitado, expirado ou rotacionado não funciona;
- resolve QR retorna somente dados mínimos;
- confirmar QR cria Visit `AGUARDANDO`;
- confirmar duas vezes é idempotente e não cria duas Visits abertas;
- QR não cria sessão nem libera histórico;
- visita encerrada/cancelada não reabre;
- CustomerId da Visit corresponde ao Customer da Reservation;
- nenhuma operação termina Visit pelo simples fim da Reservation/Lease.

### Segurança e privacidade

- nenhum log/auditoria contém token bruto, telefone completo, senha ou cookie;
- telefone existente retorna somente nome mascarado;
- `NÃO SOU EU` não altera Customer;
- rate limit bloqueia enumeração e brute force de telefone/token;
- Professional/Room são resolvidos no backend;
- policies ADMIN/GERENTE/PROFISSIONAL/CUSTOMER permanecem separadas.

## 16. Self-review da spec

As seguintes verificações foram realizadas antes de registrar o documento:

- `Tenant` e `Customer` permanecem conceitos separados;
- `Reservation.CustomerId` e `Visit.CustomerId` são nullable para preservar dados históricos;
- o fim de Reservation/Lease não encerra Visit;
- a janela de check-in é objetiva e usa instante UTC comparado após conversão do timezone operacional;
- token QR não contém dados internos e não é credencial de login;
- replay é limitado por expiração, revogação, estado da Reservation e idempotência de Visit aberta;
- concorrência de telefone usa índice único e concorrência de slot usa locks/revalidação já aprovados;
- nenhuma API Customer/Totem aceita CustomerId ou RoomId fornecido pelo cliente como autoridade;
- não há endpoint que devolva histórico por nome + telefone;
- não foram criadas tabelas de slots, WhatsApp, OTP ou configurações artificiais;
- a divergência entre o contexto mais recente e a branch atual está registrada como pré-condição, sem fingir que os módulos ausentes já existem;
- não restam placeholders de implementação na spec: os pontos que dependem da portabilidade de Reservation/Visit/Lease estão explicitamente marcados como pré-condições técnicas, não como comportamento em produção.

## 17. Fora do escopo desta entrega

Não implementar frontend, Totem UI, câmera, foto, documentos, CPF/CNPJ, OTP, SMS, WhatsApp, Intelbras, notificações, pagamentos, PIX, boleto, NFS-e, Supabase, produção, deploy, migration, alteração de banco, ou conta administrativa para Customer.

# LUMIS — Disponibilidade individual do profissional

**Status:** especificação final para revisão; nenhuma implementação, migration ou alteração de banco nesta etapa.

**Escopo:** adicionar agenda semanal e exceções de indisponibilidade por `Professional`, subordinadas ao horário global, e incorporá-las ao cálculo central usado por Customer, Totem, Reception e fluxos administrativos.

**Fora do escopo:** frontend nesta etapa, aprovação de agenda, disponibilidade extra, calendários externos, feriados automáticos, slots persistidos, jobs, WhatsApp, Supabase, produção e deploy.

## 1. Problema atual e contratos existentes

Hoje `GET /api/customer/availability` gera uma grade de 15 minutos diretamente no endpoint. Ele valida `Professional.IsActive`, lê `OperatingHourIntervals`, percorre salas ativas e combina `IRoomAvailabilityService` com `IReservationConflictDetector`. `GET /api/totem/availability` delega a esse mesmo método, portanto Customer e Totem já compartilham o resultado, mas a orquestração ainda está presa à superfície Customer.

Não existe agenda individual. Qualquer Professional ativo é considerado disponível durante todo o expediente global quando há sala e não existe conflito de Reservation, Lease ou LeaseOccurrence.

O código real também apresenta uma divergência a eliminar durante a futura implementação: a consulta Customer não oferece slots quando não há `OperatingHourIntervals`, enquanto `IRoomAvailabilityService.CheckScheduleAndBlocksAsync` considera o expediente somente se `OperatingHoursSchedule` existe. Consulta e confirmação devem passar pela mesma regra central, inclusive no estado não configurado, para que um horário nunca apareça ou seja aceito por caminhos diferentes.

Contratos que permanecem válidos:

- `OperatingHoursSchedule` é a configuração global singleton.
- `OperatingHourInterval` usa `DayOfWeek`, `TimeOnly` e PostgreSQL `time without time zone`.
- `IRoomAvailabilityService` continua responsável por expediente global, bloqueios e disponibilidade de Room.
- `IReservationConflictDetector` continua responsável por conflitos de Room e Professional com Reservations, Leases e LeaseOccurrences.
- mutações de agenda adquirem locks de `Professional` e `Room` em transação por `ILeaseResourceLock`.
- concorrência exposta usa `xmin` codificado em Base64 por `ConcurrencyToken`.
- somente Reservations `APPROVED` cujo `Kind` não seja `CANCELLATION` bloqueiam recursos.
- timezone operacional é `America/Porto_Velho`; instantes concretos são UTC.

## 2. Princípios e precedência

A disponibilidade individual afeta somente novos agendamentos. Alterar modo, intervalos ou exceções nunca cancela, rejeita, move, substitui ou modifica uma Reservation existente.

Toda alteração válida entra em vigor no commit da própria transação. Não há aprovação, publicação posterior, cache de agenda ou job intermediário, independentemente de o ator ser o próprio Professional ou Operations.

O cálculo de slots seguirá esta composição:

```text
OperatingHours
∩ ProfessionalAvailability semanal
− ProfessionalAvailabilityExceptions
− RoomBlocks
− Reservations que bloqueiam recursos
− Leases e LeaseOccurrences que bloqueiam recursos
− conflitos do Professional
− indisponibilidade da Room
= slots oferecidos
```

`Professional.IsActive = false` sempre produz indisponibilidade, independentemente do modo e dos intervalos armazenados.

Não haverá tabela `Slot`, agenda materializada ou job. Slots são projeções calculadas no momento da consulta e revalidadas, sob lock, no momento da criação ou do reagendamento.

Se `OperatingHoursSchedule` ainda não estiver configurado, a disponibilidade de novos agendamentos opera em modo fail-closed: consultas retornam zero slots e criação/reagendamento retorna `409 OPERATING_HOURS_NOT_CONFIGURED`. Essa regra única substitui a divergência atual entre consulta e confirmação.

## 3. Modelo de domínio

### 3.1 Modo do Professional

Adicionar a `Professional`:

```text
AvailabilityMode
  INHERIT_GLOBAL = 0
  CUSTOM = 1
```

O enum .NET deverá usar nomes `InheritGlobal` e `Custom`; contratos HTTP usarão `INHERIT_GLOBAL` e `CUSTOM`. O banco armazenará `smallint` com check constraint aceitando somente `0` e `1`.

Todo Professional existente recebe `INHERIT_GLOBAL` pela migration. Novos Professionals também começam nesse modo. A mudança de modo atualiza `Professional.UpdatedAt` e, portanto, seu `xmin`.

Em `INHERIT_GLOBAL`, nenhum intervalo global é copiado. A disponibilidade semanal base é lida diretamente de `OperatingHourIntervals`.

Em `CUSTOM`, a disponibilidade semanal base vem dos intervalos próprios e continua limitada pelo expediente global.

Ao trocar explicitamente de `CUSTOM` para `INHERIT_GLOBAL`, os intervalos customizados são removidos na mesma transação. Isso evita configuração oculta e obsoleta. Para voltar a `CUSTOM`, o Professional ou Operations envia novamente a semana desejada.

### 3.2 Intervalos semanais

```text
ProfessionalAvailabilityInterval
  Id: Guid
  ProfessionalId: Guid, FK NoAction
  DayOfWeek: DayOfWeek/smallint
  StartTime: TimeOnly/time without time zone
  EndTime: TimeOnly/time without time zone
```

Intervalos são filhos da configuração agregada de `Professional`; não possuem token de concorrência individual. O `PUT` substitui a coleção inteira sob o `xmin` do Professional e lock da linha do Professional.

Invariantes:

- `DayOfWeek` usa a enumeração .NET: `SUNDAY = 0` até `SATURDAY = 6`.
- a API usa os sete nomes em inglês, maiúsculos, iguais ao contrato de OperatingHours.
- `StartTime < EndTime`.
- o intervalo pertence a um único dia civil; não cruza meia-noite.
- múltiplos intervalos no mesmo dia são permitidos.
- intervalos sobrepostos ou logicamente duplicados são rejeitados.
- intervalos adjacentes, como `08:00–12:00` e `12:00–18:00`, são válidos.
- dia sem intervalos significa que o Professional não atende naquele dia.
- uma agenda `CUSTOM` com todos os dias vazios é válida e impede novos agendamentos até nova alteração.

Ao gravar uma agenda `CUSTOM`, cada intervalo enviado deve caber integralmente em um intervalo do expediente global vigente. Se o expediente ainda não estiver configurado, responder `409 OPERATING_HOURS_NOT_CONFIGURED`. Intervalo fora do expediente retorna `400 PROFESSIONAL_AVAILABILITY_OUTSIDE_OPERATING_HOURS`.

Se o expediente global for reduzido depois, os intervalos `CUSTOM` já persistidos não são apagados nem reescritos. A parte fora do novo expediente apenas deixa de ser efetiva pela interseção em runtime. Se o expediente voltar a aumentar, essa parte volta a ser efetiva. Um `PUT` posterior da agenda `CUSTOM` é uma substituição consciente e todo o payload novo precisa respeitar o expediente vigente.

### 3.3 Exceções de indisponibilidade

```text
ProfessionalAvailabilityException
  Id: Guid
  ProfessionalId: Guid, FK NoAction
  Date: DateOnly/date
  AllDay: bool
  StartTime: TimeOnly?/time without time zone
  EndTime: TimeOnly?/time without time zone
  Reason: string?, máximo 300, trim; whitespace vira null
  CreatedAt: DateTimeOffset UTC
  UpdatedAt: DateTimeOffset UTC
  Version: xmin
```

Exceções apenas retiram disponibilidade. Não haverá exceção de disponibilidade extra.

Invariantes:

- `AllDay = true` exige `StartTime = null` e `EndTime = null`.
- `AllDay = false` exige ambos os horários e `StartTime < EndTime`.
- uma exceção parcial pertence a uma única data civil e não cruza meia-noite.
- uma exceção de dia inteiro não pode coexistir com outra exceção na mesma data para o mesmo Professional.
- exceções parciais do mesmo Professional e data não podem se sobrepor nem ser duplicadas; adjacência é permitida.
- a exceção pode cobrir apenas parte, todo ou até uma faixa externa à agenda efetiva; a subtração em runtime nunca cria disponibilidade.
- `Reason` é operacional e não deve conter dados pessoais de Customer.

Criação e alteração de exceção adquirem lock da linha do Professional antes de verificar sobreposição. O lock é a garantia contra duas gravações concorrentes que passariam por validações prévias. Índices únicos continuam protegendo duplicatas exatas.

A remoção é física, dentro de transação, e a auditoria imutável preserva ator, momento e identidade técnica do recurso removido. Não será criado status lógico apenas para exceções.

## 4. Disponibilidade efetiva e serviço central

A implementação deverá extrair a orquestração hoje existente em `CustomerSchedulingEndpoints.Availability` para um serviço central de slots, por exemplo `IAppointmentAvailabilityService`. Esse serviço será a única entrada para consultar slots ou validar um intervalo de novo agendamento.

O serviço central:

1. valida Professional ativo, data, duração e timezone;
2. obtém os intervalos globais do dia;
3. resolve a base individual: global para `INHERIT_GLOBAL`, customizada para `CUSTOM`;
4. calcula a interseção civil entre base individual e expediente global;
5. subtrai exceções do Professional naquela data;
6. consulta Rooms ativas sem N+1 evitável;
7. reutiliza `IRoomAvailabilityService` para expediente e RoomBlocks;
8. reutiliza `IReservationConflictDetector` para Reservations, Leases, LeaseOccurrences e conflito do Professional;
9. retorna somente slots integralmente contidos na disponibilidade efetiva e com pelo menos uma Room elegível.

Não será criado outro detector de conflitos. `IRoomAvailabilityService` permanece focado em Room; o novo serviço somente compõe os serviços existentes com a agenda do Professional.

Customer, Totem, Reception e qualquer consulta administrativa chamarão o mesmo serviço central. O endpoint Totem poderá continuar como adaptador da mesma operação, sem regra própria.

Na criação e no reagendamento, a aplicação adquire os locks determinísticos já existentes para Professional e Room, e então chama a validação central novamente dentro da transação. Uma consulta de slot anterior nunca autoriza a gravação por si só.

## 5. Efeito sobre Reservations existentes

Ao alterar semana ou criar/editar uma exceção, o backend calcula quantas Reservations futuras do Professional, com `ReservationAvailability.BlocksResources = true` e `EndAt > now`, deixarão de caber integralmente na disponibilidade efetiva proposta.

O salvamento não é bloqueado. Essas Reservations permanecem com os mesmos IDs, horários, Room, Professional, Customer, status e `xmin`. A resposta da mutação da semana inclui `availability` com a representação atualizada e:

```json
{
  "existingReservationsOutsideAvailabilityCount": 2
}
```

Na criação ou edição de exceção, a resposta contém `exception` com a representação atualizada e o mesmo campo de contagem.

Não são retornados nome, telefone ou outros dados do Customer nesse aviso. A contagem considera a agenda semanal proposta, todas as demais exceções vigentes e o expediente global atual. Remover uma exceção somente aumenta disponibilidade e não exige aviso.

Reservations pendentes, rejeitadas, canceladas e pedidos de cancelamento que não bloqueiam recursos não entram na contagem.

## 6. Alteração posterior de OperatingHours

O contrato existente de `PUT /api/admin/operating-hours` permanece: ele rejeita com `409 OPERATING_HOURS_CONFLICT` uma redução que colocaria Reservations aprovadas ou ocupações de Lease protegidas fora do funcionamento. Esta spec não relaxa essa proteção.

Quando uma alteração global for válida:

- Professionals `INHERIT_GLOBAL` passam a usar o novo expediente automaticamente.
- Professionals `CUSTOM` mantêm seus intervalos armazenados e passam a usar a interseção com o novo expediente.
- exceções continuam sendo subtraídas depois da interseção.
- nenhuma Reservation é alterada.
- nenhum intervalo `CUSTOM` é apagado, encurtado ou expandido.

Assim, uma redução global sem conflito operacional pode esconder temporariamente parte de uma agenda `CUSTOM`; uma expansão posterior pode torná-la efetiva novamente.

## 7. Contratos HTTP

### 7.1 Professional autenticado

```text
GET    /api/professional/availability
PUT    /api/professional/availability
GET    /api/professional/availability/exceptions?from=YYYY-MM-DD&to=YYYY-MM-DD
POST   /api/professional/availability/exceptions
PUT    /api/professional/availability/exceptions/{id:guid}
DELETE /api/professional/availability/exceptions/{id:guid}
```

O backend resolve o Professional por `ApplicationUserId` autenticado. Nenhum request próprio aceita `ProfessionalId`. Professional sem vínculo ativo recebe o mesmo erro controlado `PROFESSIONAL_PROFILE_NOT_LINKED` usado pelo perfil atual.

### 7.2 Operations

O projeto já agrupa gestão de Professional sob `/api/admin/professionals` e protege esse grupo com policy `Operations`, que aceita ADMINISTRADOR e GERENTE. A nova API seguirá essa superfície:

```text
GET    /api/admin/professionals/{professionalId:guid}/availability
PUT    /api/admin/professionals/{professionalId:guid}/availability
GET    /api/admin/professionals/{professionalId:guid}/availability/exceptions?from=YYYY-MM-DD&to=YYYY-MM-DD
POST   /api/admin/professionals/{professionalId:guid}/availability/exceptions
PUT    /api/admin/professionals/{professionalId:guid}/availability/exceptions/{exceptionId:guid}
DELETE /api/admin/professionals/{professionalId:guid}/availability/exceptions/{exceptionId:guid}
```

Um `exceptionId` que não pertence ao `professionalId` da rota retorna `404`.

### 7.3 DTO da semana

`GET` retorna os sete dias para manter semântica explícita de dia fechado:

```json
{
  "professionalId": "guid",
  "mode": "CUSTOM",
  "days": [
    {
      "dayOfWeek": "MONDAY",
      "intervals": [
        { "startTime": "08:00", "endTime": "12:00" },
        { "startTime": "14:00", "endTime": "18:00" }
      ]
    }
  ],
  "effectiveDays": [],
  "concurrencyToken": "base64-xmin"
}
```

`days` representa a configuração própria armazenada; em `INHERIT_GLOBAL`, contém os sete dias com listas vazias. `effectiveDays` representa a agenda semanal após interseção com o expediente global, antes das exceções por data.

O `PUT` recebe `mode`, `days` e `concurrencyToken`. O payload contém exatamente os sete dias, uma única vez cada. Em `INHERIT_GLOBAL`, todos devem ter lista vazia. Em `CUSTOM`, listas vazias representam dias sem atendimento.

Horários aceitam somente `HH:mm` ou `HH:mm:ss` e respostas usam `HH:mm`, seguindo `OperatingHoursEndpoints`.

### 7.4 DTO da exceção

```json
{
  "id": "guid",
  "professionalId": "guid",
  "date": "2026-09-15",
  "allDay": false,
  "startTime": "14:00",
  "endTime": "16:00",
  "reason": "Compromisso externo",
  "createdAt": "2026-09-07T20:00:00Z",
  "updatedAt": "2026-09-07T20:00:00Z",
  "concurrencyToken": "base64-xmin"
}
```

`POST` não recebe token porque cria um recurso novo; o lock do Professional protege a validação contra sobreposição concorrente. `PUT` recebe `concurrencyToken` no DTO. `DELETE` recebe um body JSON estrito `{ "concurrencyToken": "base64-xmin" }`, seguindo o padrão de requests de concorrência do projeto. Token ausente ou inválido retorna `400 INVALID_CONCURRENCY_TOKEN`; token stale retorna `409 RESOURCE_MODIFIED`.

Os GETs de exceções exigem `from` e `to`, aceitam intervalo máximo de 366 dias e retornam a lista ordenada por `Date`, `StartTime` e `Id`. O limite temporal dispensa paginação nesta primeira versão e evita carregar histórico ilimitado.

## 8. Autorização, antiforgery e IDOR

- endpoints próprios usam policy `Professional`.
- endpoints administrativos usam policy `Operations`.
- todas as mutações usam o filtro antiforgery existente.
- CUSTOMER e PROFESSIONAL_APPLICANT recebem `403` nas superfícies administrativas e não possuem API de escrita.
- Totem não possui acesso de escrita ou leitura à configuração individual.
- o Professional autenticado nunca escolhe sua identidade por GUID.
- Operations recebe o GUID do Professional como alvo administrativo; alvo inexistente retorna `404`.
- DTOs não retornam Customer, Identity, telefone do Professional, Finance ou detalhes de Reservations conflitantes.

## 9. Concorrência e transações

O `Professional.Version`/`xmin` é o token agregado da agenda semanal. `PUT availability`:

1. decodifica o token Base64;
2. abre transação;
3. carrega e bloqueia a linha do Professional por `ILeaseResourceLock`;
4. revalida `xmin`;
5. valida expediente e intervalos;
6. substitui a coleção semanal e altera o modo/`UpdatedAt`;
7. calcula o aviso de Reservations futuras;
8. grava auditoria;
9. salva e faz commit.

Cada exceção usa seu próprio `xmin` em edição e remoção. Criação, edição e remoção também bloqueiam primeiro o Professional para serializar a verificação de sobreposição e a leitura usada no aviso.

Erros seguem o contrato existente:

- token inválido: `400 INVALID_CONCURRENCY_TOKEN`;
- versão stale: `409 RESOURCE_MODIFIED`;
- agenda inválida: `400 INVALID_PROFESSIONAL_AVAILABILITY`;
- exceção inválida ou sobreposta: `400 INVALID_PROFESSIONAL_AVAILABILITY_EXCEPTION`;
- Professional ou exceção alheia/inexistente: `404`.

Falha em qualquer etapa reverte agenda, exceção e auditoria da operação. A Reservation nunca participa como entidade modificada dessa transação.

## 10. Auditoria

Adicionar ações controladas para edição própria:

```text
PROFESSIONAL_AVAILABILITY_UPDATED
PROFESSIONAL_AVAILABILITY_EXCEPTION_CREATED
PROFESSIONAL_AVAILABILITY_EXCEPTION_UPDATED
PROFESSIONAL_AVAILABILITY_EXCEPTION_REMOVED
```

As rotas administrativas usam as ações correspondentes com sufixo `_BY_OPERATIONS`, por exemplo `PROFESSIONAL_AVAILABILITY_UPDATED_BY_OPERATIONS` e `PROFESSIONAL_AVAILABILITY_EXCEPTION_CREATED_BY_OPERATIONS`. Agenda semanal usa `PROFESSIONAL` como alvo. Exceções usam `PROFESSIONAL_AVAILABILITY_EXCEPTION`. `ActorUserId` identifica quem realizou a operação, e o nome controlado da ação preserva o escopo do ator mesmo que suas roles mudem posteriormente.

Auditoria registra apenas IDs técnicos, nomes de campos aprovados, ator, timestamp UTC, resultado e correlação. `AuditFields` será ampliado somente com nomes controlados necessários, como `AvailabilityMode`, `AvailabilityIntervals`, `Date`, `AllDay`, `StartTime` e `EndTime`. Não registra Customer, conteúdo de Reservation, telefone, descrição livre da exceção, cookie ou request body.

GETs e consultas de slots não geram `AuditEntry`.

## 11. Migration PostgreSQL prevista

A migration será aditiva e conterá:

1. `Professionals.AvailabilityMode smallint NOT NULL DEFAULT 0`, com check constraint para `0..1`.
2. tabela `ProfessionalAvailabilityIntervals`.
3. tabela `ProfessionalAvailabilityExceptions`.
4. FKs para `Professionals` com `DeleteBehavior.NoAction`.
5. índices de consulta e unicidade descritos abaixo.

Índices e constraints:

- check `DayOfWeek BETWEEN 0 AND 6`;
- check `EndTime > StartTime` nos intervalos semanais;
- índice único `(ProfessionalId, DayOfWeek, StartTime)`;
- check de exceção garantindo combinação coerente de `AllDay` e horários;
- índice `(ProfessionalId, Date, StartTime)` para consulta por calendário;
- índice único parcial `(ProfessionalId, Date)` onde `AllDay = true`;
- índice único parcial `(ProfessionalId, Date, StartTime)` onde `AllDay = false`.

Sobreposições não idênticas são impedidas pela validação sob lock de Professional; não será exigida extensão PostgreSQL nova apenas para uma exclusion constraint.

`Version` das exceções usa `xmin` e não cria coluna comum de versão. Intervalos semanais usam o `xmin` do Professional agregado.

Não haverá backfill de intervalos, exceções, Slots, dias gerados ou agendas materializadas. A migration não aplica dados no Supabase e não executa automaticamente em produção.

## 12. Integrações com Customer, Totem, Reception e Admin

`GET /api/customer/availability` e `GET /api/totem/availability` preservam seus contratos atuais de Professional, data e duração. Internamente passam a chamar o serviço central, portanto recebem automaticamente modo individual e exceções.

Criação e reagendamento Customer/Totem/Reception também usam a validação central depois dos locks. Uma Reservation fora da agenda individual retorna `409 PROFESSIONAL_UNAVAILABLE`; ela não é criada parcialmente.

Reception e Admin usarão o mesmo serviço ao consultar horários ou criar Reservation assistida. Nenhuma dessas superfícies replica interseção de agenda ou subtração de exceções.

`IRoomAvailabilityService` e `IReservationConflictDetector` continuam sendo reutilizados. RoomBlock, Reservation, Lease e LeaseOccurrence mantêm sua precedência atual; disponibilidade individual não pode transformar um conflito desses em horário válido.

Finance, Visit, Dashboard e Operational Alerts não ganham dependência da agenda individual. O fim de uma faixa disponível não encerra Visit nem altera Reservation.

## 13. Timezone e datas civis

- agenda semanal é armazenada como `DayOfWeek` + `TimeOnly`, sem UTC.
- exceção usa `DateOnly`, interpretada em `America/Porto_Velho`.
- slots concretos são convertidos para `DateTimeOffset` UTC usando o timezone operacional.
- comparação de exceção ocorre depois de converter o instante para data e hora civis operacionais.
- não usar `DateTime.Now`, `DateTime.Today` ou `TimeZoneInfo.Local` em regras.
- os limites são intervalos semiabertos `[start, end)`: um slot que termina exatamente no início de uma exceção e um que começa exatamente no fim são válidos.

## 14. Contratos futuros de frontend

Nenhum frontend será implementado nesta etapa de spec.

A futura área `Minha disponibilidade` do Professional oferecerá modo herdado ou personalizado, sete dias, múltiplos intervalos e exceções de dia inteiro ou faixa. Após salvar, `existingReservationsOutsideAvailabilityCount > 0` gera aviso não bloqueante de que agendamentos existentes foram mantidos.

Na gestão de Professional, ADMINISTRADOR e GERENTE terão o mesmo editor apontando para as rotas Operations. A interface real de horário do estabelecimento consumirá `GET/PUT /api/admin/operating-hours`, e bloqueios consumirão as APIs `room-blocks` existentes; não serão criados modelos alternativos no frontend.

Customer e Totem continuarão vendo somente slots finais. Eles não recebem modo, intervalos internos, exceções ou motivos.

## 15. Testes obrigatórios da futura implementação

### Domínio e persistência

1. Professional existente e novo iniciam em `INHERIT_GLOBAL`.
2. enum e banco usam `0 = SUNDAY` até `6 = SATURDAY` para os dias.
3. múltiplos intervalos válidos no mesmo dia são aceitos.
4. `StartTime >= EndTime`, sobreposição e duplicata são rejeitados; adjacência é aceita.
5. exceção AllDay exige horários nulos e elimina o dia.
6. exceção parcial exige horários válidos e remove somente a interseção.
7. exceções sobrepostas são rejeitadas mesmo sob duas gravações concorrentes.
8. tipos PostgreSQL são `smallint`, `time without time zone`, `date` e `timestamptz` conforme definido.
9. migration é aditiva, mantém Professionals existentes e não cria Slot.

### Cálculo e integração

10. `INHERIT_GLOBAL` acompanha OperatingHours sem copiar intervalos.
11. `CUSTOM` limita disponibilidade e dia vazio não oferece slots.
12. intervalo CUSTOM fora do expediente vigente é rejeitado na gravação.
13. redução válida de OperatingHours preserva CUSTOM armazenado e aplica interseção efetiva.
14. expansão posterior do expediente volta a tornar efetiva a porção CUSTOM preservada.
15. exceção AllDay remove todos os slots da data.
16. exceção parcial remove apenas slots que a sobrepõem, usando `[start, end)`.
17. Customer e Totem retornam exatamente os mesmos slots para a mesma entrada.
18. Reception/Admin usam o mesmo resultado central.
19. RoomBlock, Reservation, Lease e LeaseOccurrence continuam bloqueando conforme regras atuais.
20. dois clientes concorrendo pelo mesmo slot continuam produzindo uma única Reservation aceita.
21. consulta e confirmação têm a mesma semântica quando OperatingHours não está configurado.
22. timezone `America/Porto_Velho` determina dia da semana e data da exceção nas bordas UTC.

### Reservations existentes

23. redução de agenda e criação de exceção não alteram nem cancelam Reservation existente.
24. novo agendamento fora da agenda deixa de aparecer e é rejeitado na gravação.
25. alteração retorna a contagem correta de Reservations futuras aprovadas fora da agenda.
26. aviso não expõe Customer nem conteúdo pessoal.
27. redução de OperatingHours que conflita com Reservation/Lease mantém o `409 OPERATING_HOURS_CONFLICT` atual.

### API, autorização, concorrência e auditoria

28. Professional consulta e altera apenas a própria agenda, resolvida por `ApplicationUserId`.
29. GUID de exceção de outro Professional retorna `404`.
30. GERENTE e ADMINISTRADOR alteram qualquer Professional pelas rotas Operations.
31. CUSTOMER, PROFESSIONAL_APPLICANT e anônimo não escrevem agenda.
32. mutações exigem antiforgery.
33. token Base64 inválido retorna `400 INVALID_CONCURRENCY_TOKEN`.
34. `xmin` stale retorna `409 RESOURCE_MODIFIED` sem lost update.
35. auditoria registra criação, alteração e remoção com ator e alvo corretos, sem PII.
36. GETs e consulta de slots não geram auditoria.

## 16. Compatibilidade e limites

Continuam inalterados:

- modelo e ciclo de vida de Reservation;
- independência de Visit;
- contratos financeiros baseados em Lease e Tenant;
- disponibilidade de Room e RoomBlocks;
- regras de Lease e LeaseOccurrence;
- Customer, QR/check-in, Totem, Reception, Dashboard e Alertas;
- APIs públicas de slots, salvo novos erros controlados na confirmação.

O lock em linha do Professional funciona entre múltiplas instâncias conectadas ao mesmo PostgreSQL. O cálculo não depende de cache local, Redis ou estado em memória.

## 17. Fora do escopo

- frontend ou redesign;
- aprovação de mudanças pela gerência;
- disponibilidade extra fora do expediente;
- feriados externos ou calendário de feriados;
- Google Calendar ou Outlook Calendar;
- WhatsApp;
- slots, dias ou schedules materializados;
- background jobs;
- Supabase, produção e deploy.

## 18. Decisões finais

1. `INHERIT_GLOBAL` é o padrão persistido para compatibilidade.
2. `CUSTOM` é uma coleção semanal substituída atomicamente e protegida pelo `xmin` do Professional.
3. intervalos enviados devem caber no expediente atual; reduções globais posteriores preservam a configuração e usam interseção em runtime.
4. exceções apenas reduzem disponibilidade e têm `xmin` próprio.
5. Reservations existentes nunca são alteradas; mutações retornam apenas a contagem de conflitos futuros.
6. o bloqueio atual de redução global sobre Reservations/Leases válidas permanece.
7. Customer, Totem, Reception e Admin usam um único serviço central de slots, que compõe os detectores existentes.
8. não existe entidade Slot, agenda gerada ou job.

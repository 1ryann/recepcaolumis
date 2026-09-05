# LUMIS — design de Locações

**Status:** aprovado para planejamento e implementação local.  
**Escopo:** contratos operacionais de uso de salas, locatários mínimos e ocorrências operacionais.  
**Fora do escopo:** Financeiro, Reservas, Visitas, Totem, Intelbras, expediente administrativo completo e pagamentos.

## Objetivo

Uma locação representa o contrato operacional pelo qual um `Professional` ocupa uma `Room` em nome de um `Tenant`. O locatário pode ser pessoa ou empresa e não precisa ser o profissional ocupante. O contrato preserva a tarifa acordada e é a fonte definitiva de disponibilidade, inclusive quando suas ocorrências futuras ainda não foram materializadas.

## Limites da etapa

- Não criar cobrança, fatura, pagamento, estado de atraso, PIX, boleto, NFS-e ou integração bancária.
- Não criar Visitas, Reservas, agenda administrativa, bloqueios de sala, totem ou integração de acesso.
- Não permitir exclusão física de `Lease`, `LeaseOccurrence` ou `Tenant` usados.
- Não reintroduzir dados de `AppStore` ou mocks no fluxo real de Locações.
- Não executar migrations automaticamente no startup.

## Vocabulário e entidades

### Tenant

Cadastro mínimo de locatário, reutilizável por várias locações:

```text
Tenant
  Id: GUID
  Name: nvarchar(200)
  NormalizedName: nvarchar(400)
  Kind: INDIVIDUAL | LEGAL_ENTITY
  IsActive: bit
  CreatedAt, UpdatedAt: datetimeoffset
  RowVersion: rowversion
```

`NormalizedName` é calculado somente por `TextNormalizer`. Não há unicidade por nome, documento, telefone ou e-mail. Documentos, endereço, contatos e dados financeiros não fazem parte desta etapa.

### Lease

Contrato operacional e fonte definitiva de conflito de ocupação:

```text
Lease
  Id: GUID
  TenantId: GUID
  ProfessionalId: GUID
  RoomId: GUID
  Mode: MONTHLY | DAILY | HOURLY
  ContractedRate: decimal(18,2)
  BillingStartAt: datetimeoffset
  BillingDueDay: tinyint?          // 1..31; somente metadado contratual
  OccupancyStartAt: datetimeoffset
  OccupancyEndAt: datetimeoffset? // null significa prazo indeterminado
  LifecycleState: OPEN | ENDING_PENDING | ENDED | CANCELLED
  MonthlyAnchorDay: tinyint?       // calculado para MONTHLY; 1..31
  MaterializedThroughAt: datetimeoffset?
  CreatedAt, UpdatedAt: datetimeoffset
  RowVersion: rowversion
```

`ContractedRate` aceita zero, no máximo duas casas e o mesmo teto comercial de `RoomRate.Maximum`. A tarifa da sala é apenas uma sugestão no formulário; nunca atualiza retroativamente uma locação.

`BillingStartAt` é contratual/financeiro. `OccupancyStartAt` é operacional. Postergar ocupação nunca modifica `BillingStartAt`. A postergação mantém `OccupancyEndAt` inalterado; portanto, se houver término definido, o novo início deve continuar anterior a ele.

`BillingStartAt` deve ser anterior ou igual ao início efetivo. Não há cobrança retroativamente deslocada por postergação, nem início financeiro posterior à primeira ocupação nesta etapa.

### LeaseOccurrence

Representação operacional materializada para agenda, Visitas e Totem futuros:

```text
LeaseOccurrence
  Id: GUID
  LeaseId: GUID
  StartAt, EndAt: datetimeoffset
  State: PLANNED | CANCELLED | COMPLETED
  CreatedAt, UpdatedAt: datetimeoffset
  RowVersion: rowversion
```

Uma ocorrência não estabelece exclusividade contratual. Apenas `Lease` e seu intervalo de ocupação definem disponibilidade. Ocorrências passadas são preservadas; ocorrências futuras canceladas são marcadas `CANCELLED`, nunca removidas.

## Tempo, modalidades e status

### Fuso operacional

`Scheduling:TimeZoneId` é uma configuração obrigatória validada no startup quando o módulo for ativado. O valor de Development é `America/Porto_Velho`. A configuração é usada para converter datas civis e calcular limites mensais; instantes persistidos permanecem UTC em `datetimeoffset`.

Não há fuso implícito no cliente. O frontend usa a configuração operacional recebida em DTO de metadados ou apresenta campos de data/hora no fuso configurado; ele não converte datas financeiras para `Date` sem necessidade.

### MONTHLY

`MONTHLY` significa ocupação contínua e exclusiva da sala de `OccupancyStartAt` até `OccupancyEndAt`, ou sem limite quando o término é nulo. A recorrência existe somente para materializar períodos operacionais mensais.

`MonthlyAnchorDay` é derivado do dia local do início. Cada fronteira mensal usa `min(MonthlyAnchorDay, último dia do mês)`, evitando que uma locação iniciada no dia 31 passe a ancorar permanentemente no dia 28 ou 30. A última ocorrência pode ser truncada pelo término contratual.

### DAILY

`DAILY` representa um dia civil de utilização no fuso operacional. Nesta etapa o intervalo operacional inicial é o dia civil completo, de 00:00 inclusive a 00:00 do dia seguinte exclusivo, convertido para instantes UTC. Quando o módulo de expediente existir, ele poderá gerar as ocorrências diárias dentro do expediente sem reescrever contratos ou histórico existentes.

### HOURLY

`HOURLY` recebe intervalo exato `OccupancyStartAt`/`OccupancyEndAt`, com término obrigatório e estritamente posterior ao início.

### Status derivado

O estado persistido não depende de jobs. A API expõe `status` assim:

| Condição | Status exposto |
|---|---|
| `LifecycleState = CANCELLED` | `CANCELADA` |
| `LifecycleState = ENDED` | `ENCERRADA` |
| `LifecycleState = ENDING_PENDING` | `ENCERRAMENTO_PENDENTE` |
| `OPEN` e início efetivo no futuro | `AGENDADA` |
| `OPEN`, início já chegou e sem término vencido | `ATIVA` |
| `OPEN` e término já chegou, antes da reconciliação | `ENCERRAMENTO_PENDENTE` |

Assim, uma locação passa a `ATIVA` quando chega o início mesmo sem job. Quando um término agendado chega, ela deixa de ser utilizável para novas chegadas ou alocações imediatamente; o coordenador reconcilia-a para `ENDED` ou mantém `ENDING_PENDING` conforme existam visitas abertas no futuro módulo.

## Ciclo de vida

1. Criação válida produz `OPEN`; a projeção será `AGENDADA` ou `ATIVA` conforme `OccupancyStartAt`.
2. Enquanto a projeção for `AGENDADA`, ADMINISTRADOR e GERENTE podem alterar sala, profissional, período, valor, locatário, modalidade e metadados contratuais permitidos.
3. Depois de `ATIVA`, alterações estruturais são recusadas. A correção ocorre por encerramento e nova locação; isso preserva histórico de sala, profissional e período.
4. `postpone-occupancy` é permitido somente antes do início efetivo. Revalida disponibilidade, preserva cobrança e mantém o término já definido.
5. `cancel` é permitido somente antes do início efetivo. Muda para `CANCELLED` e cancela ocorrências futuras próprias.
6. `end` aceita término imediato ou uma data futura. O término futuro permanece `ATIVA` até vencer. O imediato é reconciliado na própria transação.
7. Ao vencer um término, ou ao terminar imediatamente, `LeaseLifecycleCoordinator` consulta `ILeaseOpenVisitProbe`. Sem visita aberta, conclui `ENDED`; com visita aberta, usa `ENDING_PENDING`. Ambos cancelam ocorrências futuras e impedem novas chegadas.
8. Visitas não são implementadas agora. A implementação inicial do probe informa ausência de pendências; uma futura implementação de Visitas substitui esse adaptador e chama o coordenador ao fechar a última visita.

`ENDING_PENDING` continua bloqueando a disponibilidade da sala e do profissional. Nenhuma ação de encerramento encerra uma visita automaticamente.

## Recorrência e disponibilidade

O `LeaseOccurrencePlanner` mantém uma janela móvel de 180 dias à frente, incluindo a ocorrência atual quando aplicável. Ele é acionado na criação, edição permitida, postergação, cancelamento, encerramento, consulta de disponibilidade e operações que precisam de ocorrência atual. Um job futuro pode antecipar o planejamento, mas não é requisito de correção.

Conflitos fora da janela são verificados diretamente no intervalo de `Lease`, que cobre toda a ocupação mensal contínua e todos os intervalos diários/horários. Não há dependência de ocorrências infinitas.

Uma sobreposição existe quando:

```text
candidate.Start < existing.End (ou existing.End é nulo)
e candidate.End (ou infinito) > existing.Start
```

São inválidas duas locações válidas e sobrepostas para a mesma sala ou para o mesmo profissional. `OPEN` e `ENDING_PENDING` continuam ocupando os recursos; `CANCELLED` e `ENDED` não.

## Concorrência e transações

SQL Server não possui constraint de exclusão por intervalo. A garantia definitiva usa uma única transação de banco e `sp_getapplock` com proprietário `Transaction`.

Antes de validar sobreposição, a operação adquire locks para os recursos envolvidos, em ordem ordinal determinística e sem duplicatas:

```text
LumisLease:professional:{ProfessionalId}
LumisLease:room:{RoomId}
```

Em troca de sala ou profissional, bloqueia recursos antigos e novos. Depois do lock, a operação reconcilia encerramentos vencidos pertinentes, consulta sobreposições e grava contrato, auditoria e ocorrências na mesma transação. Falha ao adquirir lock ou violação de regra retorna erro controlado, sem SQL interno.

Toda mutação pública também exige o `concurrencyToken` Base64 do `rowversion` de `Lease`. Token ausente ou inválido retorna `400 INVALID_CONCURRENCY_TOKEN`; token desatualizado retorna `409 RESOURCE_MODIFIED`. Nenhuma auditoria de sucesso é criada para mutação rejeitada.

## Persistência, constraints e índices

A migration será somente aditiva e não alterará collation, Identity nem tabelas existentes além de acrescentar valores controlados à auditoria quando necessário.

- FKs `Lease -> Tenant`, `Lease -> Professional`, `Lease -> Room` e `LeaseOccurrence -> Lease` usam `NoAction`.
- `Tenant`, `Lease` e `LeaseOccurrence` possuem `rowversion`.
- Checks: enums controlados, tarifa não negativa, dia de vencimento entre 1 e 31, início financeiro não posterior ao efetivo, término posterior ao início e regras obrigatórias de término para `DAILY` e `HOURLY`.
- `DAILY` persiste término obrigatório calculado no fuso operacional; somente `MONTHLY` pode usar término nulo.
- `UX_LeaseOccurrences_LeaseId_StartAt` impede duplicação de materialização.
- Índices de leitura incluem `Tenant.NormalizedName`, `Lease(RoomId, LifecycleState, OccupancyStartAt)`, `Lease(ProfessionalId, LifecycleState, OccupancyStartAt)` e `Lease(TenantId, LifecycleState, BillingStartAt)`.
- Não haverá índice único de nome de locatário nem deduplicação automática.

## Auditoria

`AuditEntry` recebe o alvo controlado `LEASE`. As ações mínimas são:

```text
LEASE_CREATED
LEASE_UPDATED
LEASE_OCCUPANCY_POSTPONED
LEASE_CANCELLED
LEASE_END_SCHEDULED
LEASE_ENDING_PENDING
LEASE_ENDED
```

Em updates, `ChangedFields` contém somente nomes aprovados, em ordem determinística: `TenantId`, `ProfessionalId`, `RoomId`, `Mode`, `ContractedRate`, `BillingStartAt`, `BillingDueDay`, `OccupancyStartAt` e `OccupancyEndAt`. Não registrar valores, dados de documento, informações de pagamento, tokens, exceções ou conteúdo de requests.

## Autorização e API

As APIs administrativas exigem `Operations`, portanto ADMINISTRADOR ou GERENTE ativos, com senha já alterada. Todas as mutações usam antiforgery e contratos JSON estritos.

```text
GET    /api/admin/tenants
GET    /api/admin/tenants/{id:guid}
POST   /api/admin/tenants
PUT    /api/admin/tenants/{id:guid}
POST   /api/admin/tenants/{id:guid}/activate
POST   /api/admin/tenants/{id:guid}/deactivate

GET    /api/admin/leases
GET    /api/admin/leases/{id:guid}
POST   /api/admin/leases
PUT    /api/admin/leases/{id:guid}
POST   /api/admin/leases/{id:guid}/postpone-occupancy
POST   /api/admin/leases/{id:guid}/cancel
POST   /api/admin/leases/{id:guid}/end

GET    /api/professional/leases
GET    /api/professional/leases/{id:guid}
```

O endpoint profissional nunca recebe `ProfessionalId`. Ele resolve o usuário autenticado por `Professional.ApplicationUserId` e filtra pelo `Lease.ProfessionalId` correspondente. Um recurso de outro profissional retorna `404`, evitando IDOR/BOLA. O DTO profissional expõe somente dados necessários à própria locação, sem dados internos de Identity ou locatário além do nome contratual necessário à leitura.

A busca administrativa de Locações consulta, no banco, nome normalizado do locatário, nome/profissão normalizados do profissional e nome normalizado da sala. Os filtros de `status`, `roomId`, `professionalId` e `tenantId` são validados antes da consulta; `status` aceita somente os cinco valores expostos e `all`.

Desativar sala, profissional ou locatário não modifica contratos existentes. A operação bloqueia somente a criação, edição estrutural, postergação ou ativação lógica que passaria a usar um recurso inativo.

## Frontend

A rota `/admin/locacoes` passa a usar uma tela real sem importar `src/dev`, `AppStore` ou mocks. A mesma tela é usada em Development e no pacote de produção.

Ela contém listagem paginada, busca com debounce, filtros server-side por status, sala e profissional, criação, detalhe, edição permitida, postergação, cancelamento e encerramento. A listagem mostra locatário, profissional, sala, modalidade, período, valor contratado e status. Não há resumo de pagamentos, rótulos pago/pendente/atrasado ou dashboard financeiro.

## Estratégia de testes

- Unidade: validações de entidade, tarifas, transições, status derivado, âncoras mensais, geração de janela e campos de auditoria.
- Integração SQL Server de testes: FKs, checks, migration, paginação, filtros, DTOs, CSRF, JSON estrito, policies e BOLA.
- Concorrência: duas requisições concorrentes para mesma sala/profissional, alteração estrutural concorrente, token obsoleto e ausência de auditoria falsa.
- Recorrência: janela limitada, preservação de passado, cancelamento apenas futuro e conflito além da janela.
- Frontend: cliente relativo/CSRF, estados de loading/erro/vazio, filtros, `RESOURCE_MODIFIED`, ações permitidas e ausência de AppStore.
- Bundle: a verificação de produção rejeita marcadores e imports de mock de Locações.

## Revisão interna da spec

| Risco revisado | Regra definida |
|---|---|
| Status agendado sem job | Status é derivado de `OccupancyStartAt`. |
| Término com data futura e sem job | Fim vencido deixa de ser utilizável; coordenador reconcilia sob lock. |
| Prazo indeterminado | `OccupancyEndAt = null` ocupa até cancelamento/encerramento; participa de conflito como infinito. |
| Race condition de intervalos | `sp_getapplock` por sala e profissional, em ordem determinística, dentro da transação. |
| IDOR/BOLA profissional | Associação é resolvida no backend pelo usuário autenticado; não há `ProfessionalId` no request. |
| Fuso e recorrência | Fuso configurável validado; datas civis e âncora mensal usam o fuso operacional. |
| Reescrita de histórico | Updates estruturais somente enquanto agendada; contratos ativos exigem encerramento e nova locação. |
| Visita aberta no fim | Estado conservador `ENDING_PENDING`; nenhuma visita é encerrada automaticamente. |
| Dados financeiros antecipados | Apenas metadados contratuais; sem cobrança ou estados financeiros. |

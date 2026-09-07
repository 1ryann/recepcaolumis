# Plano TDD — Customer, autoagendamento e QR check-in

Spec vinculante: `docs/superpowers/specs/2026-09-07-customer-self-scheduling-checkin-design.md`.

Base: `codex/operating-hours-room-blocks` em `f60f858`, com a spec aprovada trazida por `c32e73a`. Não portar código de `codex/leases-design`.

Regras de execução: cada task começa com teste RED, recebe a menor implementação compatível, termina com testes focados GREEN e commit pequeno. Nenhuma task executa `database update` fora do banco de testes da aplicação. Não alterar frontend, Supabase ou produção.

## Task 1 — Preparação de contratos e role Customer

**RED**

- Adicionar testes unitários para `CUSTOMER` em `SystemRoles`/policies.
- Adicionar testes de registro garantindo que role, flags e campos administrativos não possam ser enviados.

**GREEN**

- Adicionar `SystemRoles.Customer` e policy de usuário Customer ativo, sem alterar as três roles existentes.
- Registrar a policy no mecanismo de autorização atual.
- Atualizar login/sessão apenas para reconhecer Customer sem abrir qualquer endpoint `Operations`.

**Verificação:** testes de Identity/policy e build da API.

## Task 2 — Entidade Customer e normalização de telefone

**RED**

- Testar nome com trim/whitespace, todos os casos E.164 já cobertos pelo normalizador e invariantes `Phone == NormalizedPhone`.
- Testar Customer sem conta, Customer com conta, inativação e rejeição de entradas inválidas.

**GREEN**

- Criar `Customer` no Domain com factory/mutações mínimas e reutilizar uma implementação única de normalização.
- Não adicionar campos de CPF, endereço, documento ou financeiro.

**Verificação:** suíte unitária de Customer/telefone.

## Task 3 — EF Core Customer e migration aditiva

**RED**

- Testar metadados EF: limites, índice único de `NormalizedPhone`, índice filtrado de `ApplicationUserId`, FKs `NoAction` e xmin.
- Testar proteção de modelo contra cascade e exposição de campos indevidos.

**GREEN**

- Adicionar `DbSet<Customer>` e configuração PostgreSQL.
- Criar migration `CustomersAndCheckIn` somente depois de todos os tipos persistidos estarem no modelo, incluindo apenas mudanças aditivas.
- Gerar script idempotente para revisão; não aplicar em Supabase/produção.

**Verificação:** testes de modelo e inspeção textual do SQL sem `DROP`, `TRUNCATE`, `ALTER DATABASE` ou alteração de collation.

## Task 4 — Registro remoto e Customer me

**RED**

- Testar `POST /api/customer/register` com dados válidos, propriedades desconhecidas, senha inválida e colisões genéricas de e-mail/telefone.
- Testar `GET /api/customer/me`, usuário não autenticado, Customer inativo e conta sem vínculo.

**GREEN**

- Criar contratos estritos e endpoints com antiforgery e rate limit próprio.
- Criar Identity + Customer + role Customer em transação coerente; não vincular automaticamente telefone já existente.
- Resolver `me` exclusivamente pelo `ApplicationUserId` do principal.
- Registrar auditoria sem segredo ou PII desnecessária.

**Verificação:** testes de integração em banco PostgreSQL de testes.

## Task 5 — Campos CustomerId em Reservation e Visit

**RED**

- Testar modelo nullable e preservação de registros existentes.
- Testar que CustomerId recebido pelo cliente não substitui o vínculo resolvido pelo backend.

**GREEN**

- Adicionar `CustomerId` nullable e FKs/indexes `NoAction` a `Reservation` e `Visit`.
- Atualizar entidades, configurações, respostas administrativas sem quebrar contratos existentes.
- Não fazer backfill.

**Verificação:** migration/model tests e testes de API de Reservations/Visits.

## Task 6 — Serviço de disponibilidade para Customer

**RED**

- Testar grade de 15 minutos, duração de 15 minutos a 8 horas em múltiplos de 15.
- Testar America/Porto_Velho, expediente fechado, bloqueio, Reservation, Lease, LeaseOccurrence e conflito profissional.
- Testar que RoomId nunca é aceito como autoridade.

**GREEN**

- Criar reader de disponibilidade como projeção; não criar entidade Slot.
- Reutilizar `IRoomAvailabilityService`, `IReservationConflictDetector`, leases e locks existentes.
- Retornar apenas Professional, data/hora local, duração e identificador lógico do slot; a sala fica resolvida apenas na confirmação.

**Verificação:** testes de integração SQL e consultas sem materialização antecipada.

## Task 7 — Autoagendamento autenticado

**RED**

- Testar listagem de profissionais ativos, disponibilidade, criação com Customer próprio e conflito concorrente.
- Testar request contendo CustomerId/RoomId, Customer inativo, Professional inativo e conflito 409.

**GREEN**

- Criar `GET /api/customer/professionals`, `GET /api/customer/availability` e `POST /api/customer/reservations`.
- Resolver Customer pelo principal, escolher Room no backend e adquirir locks em ordem determinística.
- Revalidar tudo após lock; gravar Reservation, token elegível e auditoria na mesma transação.

**Verificação:** testes focados de criação e corrida de mesmo intervalo.

## Task 8 — Leitura, cancelamento e reagendamento Customer

**RED**

- Testar listagem/detalhe próprios, IDOR 404, antecedência mínima, cancelamento, reagendamento e histórico preservado.
- Testar revogação de token em cancelamento/substituição.

**GREEN**

- Criar `GET /api/customer/reservations`, detalhe, cancel e reschedule com policies Customer e antiforgery.
- Reutilizar transições de Reservation; não duplicar regra de conflito.
- Fazer rotação/revogação de token dentro da transação da mudança.

**Verificação:** testes de integração de ciclo de Reservation.

## Task 9 — CheckInToken seguro

**RED**

- Testar entropia mínima, Base64Url, armazenamento somente do SHA-256, unicidade por Reservation e ausência do bruto em logs/auditoria.
- Testar emissão somente para dono e Reservation aprovada Customer.

**GREEN**

- Criar entidade, factory, configuração e serviço de token com `RandomNumberGenerator`.
- Adicionar emissão `POST /api/customer/reservations/{id}/check-in-token`, retornando o bruto uma única vez.
- Definir expiração em `EndAt`; revogar cancelado, rejeitado e substituído.

**Verificação:** testes de persistência e segurança do token.

## Task 10 — Totem: resolução Customer e agendamento sem conta

**RED**

- Testar lookup inexistente/existente com nome mascarado, ausência de CustomerId/telefone/e-mail na resposta e `NÃO SOU EU` sem mutação.
- Testar criação com nome+telefone, reutilização sem alterar nome, criação concorrente e ausência de conta Identity.

**GREEN**

- Criar GETs públicos de profissionais/disponibilidade e `POST /api/totem/customers/resolve`, `POST /api/totem/reservations`.
- Usar somente nome/telefone no input; resolver Room e Customer no backend.
- Não criar token de continuação; repetir normalização no POST final.

**Verificação:** testes de privacidade, rate limit e agendamento presencial.

## Task 11 — Resolve e confirma QR

**RED**

- Testar janela `StartAt - 1h <= now < EndAt`, estados inválidos, token revogado/expirado e respostas genéricas.
- Testar `resolve` sem sessão e `confirm` criando Visit AGUARDANDO com CustomerId.
- Testar repetição idempotente, Visit terminal não reaberta e ausência de sessão/porta/alteração de Reservation.

**GREEN**

- Criar `POST /api/totem/check-in/resolve` e `/confirm` com rate limits públicos, antiforgery e transação.
- Hash do token no body, nunca URL; revalidar Reservation, Customer, Professional, Room e Visit após lock.
- Criar `VisitTransition`, auditoria e `UsedAt` de forma segura; repetição de Visit aberta retorna estado atual mínimo.

**Verificação:** testes de integração do fluxo completo QR → Visit.

## Task 12 — Rate limiting e auditoria consolidada

**RED**

- Testar limites cumulativos por IP e identificadores derivados para registro, lookup, Reservation e QR.
- Testar ações de auditoria e ausência de token/telefone completo em `AuditEntry`.

**GREEN**

- Implementar particionadores específicos usando `RemoteIpAddress`, sem confiar em `X-Forwarded-For`.
- Adicionar targets/actions controlados para Customer, Reservation, CheckInToken e Visit.
- Garantir transação única para cada mutação e rollback sem auditoria de sucesso.

**Verificação:** testes de segurança e auditoria.

## Task 13 — Compatibilidade e regressão dos módulos existentes

**RED**

- Executar testes focados de Auth, Professionals, Rooms, Leases, Reservations, Visits, Alerts, Operating Hours, Room Blocks, Finance e Dashboard.
- Adicionar somente testes para regressões objetivamente encontradas.

**GREEN**

- Corrigir compatibilidade de DTOs, consultas e constraints sem alterar regras aprovadas.
- Confirmar que Admin/Manager continuam podendo criar Reservations sem Customer quando o fluxo atual permite.

**Verificação:** suíte de integração completa.

## Task 14 — Verificação final e entrega local

Executar uma única rodada final:

```powershell
dotnet build recepcaototem.sln --no-restore
dotnet test recepcaototem.sln --no-build --no-restore -m:1 -nr:false
git diff --check
```

Revisar a migration e o SQL, calcular SHA-256, confirmar ausência de operações destrutivas e verificar `git status`. Não rodar frontend/npm, não publicar, não executar Supabase/produção e não fazer push/merge.

Entrega final: branch/worktree, commits, migration, entidades, endpoints Customer/Totem, token, integrações Reservation/Visit, rate limiting, auditoria, testes, divergências e pendências reais.

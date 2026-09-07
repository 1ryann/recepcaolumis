# Status do backend MVP do Lumis

## Estado

O backend do MVP está concluído na branch `codex/reception-backend`. A suíte atual possui 472 testes .NET aprovados e nenhum teste falho; o build da solution termina com zero avisos e zero erros.

## Módulos concluídos

- Auth e provisionamento de Identity
- Professionals e fotos privadas
- Rooms
- Leases e LeaseOccurrences
- Reservations
- Visits
- Operational Alerts
- Operating Hours e Room Blocks
- Finance
- Dashboard administrativo
- Customer e perfil próprio
- Autoagendamento
- Totem backend e QR/check-in
- Reception backend
- Notifications backend
- Access Control backend

## Superfícies principais

- `/api/auth`: CSRF, login, sessão, logout e troca de senha
- `/api/admin`: profissionais, salas, usuários, tenants, locações, reservas, visitas, alertas, horários, bloqueios, financeiro e dashboard
- `/api/professional`: recursos próprios de visitas, locações, reservas e financeiro
- `/api/customer`: cadastro, perfil, profissionais, disponibilidade e reservas próprias
- `/api/totem`: profissionais, disponibilidade, criação de reserva, resolução/confirmação de check-in
- `/api/reception`: overview, agenda, profissionais, salas, reservas, visitas, check-in manual, agendamento assistido e liberação de porta

As policies principais são `Administration` para administração de Identity, `Operations` para ADMINISTRADOR/GERENTE, `Professional` para o escopo do profissional e `Customer` para o escopo do cliente. Mutations autenticadas usam antiforgery; endpoints públicos do Totem têm limites próprios.

## Banco e tempo

O desenvolvimento usa PostgreSQL local no banco `LumisDev`. A persistência temporal concreta usa UTC; regras civis usam `America/Porto_Velho`, configurada por `Scheduling:TimeZoneId`. O processo não executa migrations automaticamente no startup.

Migrations PostgreSQL versionadas:

1. `20260905234344_PostgreSqlBaseline`
2. `20260906034221_LeasesFoundation`
3. `20260906180400_ReservationsFoundation`
4. `20260906205535_VisitsFoundation`
5. `20260906232432_OperatingHoursAndRoomBlocks`
6. `20260907020228_FinancialChargesFoundation`
7. `20260907044031_CustomersAndCheckIn`
8. `20260907044154_CustomerLinks`
9. `20260907142242_ProfessionalDescription`

As migrations são aditivas, preservam referências históricas e usam `xmin`/`concurrencyToken` para concorrência onde aplicável. Nenhuma migration foi criada nesta consolidação.

## Providers locais e integrações futuras

Development/Testing usa o provider Demo para notificações e controle de acesso. Os adapters Meta WhatsApp e Intelbras permanecem fail-closed e não fazem chamadas externas sem uma implementação/protocolo e configuração aprovados.

O controle de acesso usa uma porta lógica configurada no backend e cooldown local ao processo. Em múltiplas instâncias, esse cooldown deverá ser substituído por uma coordenação compartilhada antes da operação física.

## Como executar localmente

Configure a connection string somente no User Secrets do projeto web, apontando para `localhost` e `LumisDev`, sem colocá-la em arquivo versionado:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<connection-string-local>" --project .\recepcaototem
dotnet user-secrets set "Security:DataProtectionPath" "<diretorio-local-de-chaves>" --project .\recepcaototem
dotnet user-secrets set "Storage:PrivateFilesPath" "<diretorio-local-privado>" --project .\recepcaotem
```

Com o banco local preparado, execute a aplicação pelo perfil HTTPS para que os cookies Secure funcionem no desenvolvimento:

```powershell
dotnet run --project .\recepcaototem --launch-profile https
```

Os endpoints locais de saúde são `https://localhost:7266/health` e `https://localhost:7266/health/ready`. A aplicação não cria usuários ou roles automaticamente; use o mecanismo controlado de provisionamento local existente.

## O que falta para produção

- aplicar e reconciliar as migrations no ambiente PostgreSQL de produção, em procedimento separado e aprovado;
- configurar chaves de Data Protection e armazenamento privado externos;
- definir o modelo/protocolo/credenciais reais da controladora Intelbras;
- implementar e configurar o provider Meta WhatsApp;
- concluir o frontend e a publicação operacional.

Nenhuma dessas etapas foi executada nesta consolidação.

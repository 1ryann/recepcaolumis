# PROJECT CONTEXT — LUMIS Gestão Predial

Atualizado em 05/09/2026. Este é o ponto de entrada para continuar o projeto em outra máquina. A fonte de verdade para Profissionais e Salas é `docs/superpowers/specs/2026-09-05-professionals-rooms-design.md`; este arquivo resume o estado executável atual sem substituí-la.

## Objetivo

Sistema integrado de recepção e gestão predial para relacionar profissionais, salas, locações, reservas e visitantes. O visitante usa um totem touch para informar sua chegada e capturar uma foto; profissionais acompanham suas visitas e solicitações; Gerente e Administrador controlam a operação. A integração entre locação, agenda e totem é central: o profissional só aparece na janela permitida de uma ocorrência válida.

Cronograma aprovado: MVP básico nos primeiros 10 dias e versão complementada nos 20 dias seguintes. O desenho visual existente deve ser preservado. Mudanças visuais significativas exigem aprovação.

## Escopo congelado do MVP de 10 dias

- Totem touch com foto, nome, profissão e sala do profissional; seleção, nome do visitante, webcam, aviso de privacidade, confirmação, sucesso e retorno automático.
- Cadastro/edição/ativação de profissionais, cadastro de salas e tarifas, locatários e locações mensais, diárias e por hora.
- Agenda por período, recorrências mensais por horário fixo, expediente global e bloqueios manuais de sala.
- Solicitações profissionais de nova reserva, reagendamento e cancelamento, sempre com aprovação de Gerente/Administrador.
- Visitas, transições de atendimento, histórico e correções gerenciais auditadas.
- Alertas em tempo real no painel para visitas fora do horário, risco de conflito e conflito ativo.
- Liberação de acesso simulada, separada do estado da visita; integração física Intelbras não condiciona o MVP.
- Financeiro básico: valor, vencimento e pago/pendente/atrasado; cálculo por minuto ou diária e valor mensal informado.
- WhatsApp real somente se Meta/credenciais estiverem disponíveis; caso contrário, modo de teste explícito.
- Segurança e LGPD desde a primeira etapa: autenticação, autorização por recurso, validação, rate limiting, auditoria, fotos privadas e retenção.

Fora dos 10 dias: financeiro avançado, créditos/estornos automáticos, alteração de toda a série recorrente, troca de sala pelo profissional, expediente por sala, alertas externos gerenciais, MFA funcional e relatórios avançados. Banco, boleto/PIX, NFS-e, assinatura e contabilidade ficam apenas como “Não configurado”. Reconhecimento facial está excluído.

## Regras de negócio definidas

### Agenda, salas e profissionais

- Uma sala e um profissional não podem ter reservas sobrepostas. Podem existir vínculos diferentes em períodos distintos.
- Locatário pode ser pessoa ou empresa, ser diferente do profissional ocupante e possuir várias locações.
- Sala não possui ocupante fixo no cadastro; ocupação e disponibilidade são derivadas das ocorrências.
- Locação imediata inicia ativa. Locação agendada válida passa automaticamente para ATIVA na data, sem nova aprovação; conferência gerencial não bloqueia.
- Adiamento da ocupação mantém a cobrança original. Início financeiro e início de ocupação são separados.
- Locação mensal recorrente exige início, aceita término opcional e gera ocorrências por janela futura controlada, nunca infinitas. Conflitos devem considerar também o período ainda não materializado.
- Expediente é global por dia da semana. Diária cobre o expediente da data, não 24 horas corridas.
- Bloqueio de sala ou redução do expediente não pode invalidar reserva existente; conflitos devem ser listados e resolvidos manualmente.

### Solicitações e cancelamentos

- Nova reserva, reagendamento e cancelamento exigem antecedência mínima de 1 hora. Nova reserva depende de disponibilidade.
- Solicitações ficam PENDENTES até aprovação; podem ser APROVADAS ou RECUSADAS. Recusa exige justificativa e data da resposta.
- Profissional não altera a agenda diretamente. Reagendamento no MVP altera somente a ocorrência escolhida e permanece na mesma sala.
- A reserva original continua válida durante a análise. A disponibilidade é revalidada ao aprovar.
- Cancelamento aprovado libera o período, preserva ocorrência CANCELADA e cobrança integral; não há crédito, estorno ou abatimento automático.
- Reagendamento/cancelamento são bloqueados quando a ocorrência tem visita AGUARDANDO ou EM ATENDIMENTO.

### Totem, visitas e acesso

- Uma ocorrência aparece no totem desde 1 hora antes até o término. Depois, por 30 minutos, permanece aviso de atendimento encerrado sem aceitar nova chegada; em seguida desaparece.
- Chegada cria visita AGUARDANDO. Fluxo normal: AGUARDANDO → EM ATENDIMENTO → ENCERRADA. AGUARDANDO/EM ATENDIMENTO podem ir para CANCELADA. AGUARDANDO não vai diretamente para ENCERRADA.
- Profissional atua somente nas próprias visitas; gestão atua em qualquer visita. Correção excepcional exige justificativa, autor, data/hora e estados anterior/novo.
- Fim da reserva não encerra visitas abertas e impede novas chegadas. Visita aberta continua até encerramento/cancelamento.
- Encerrar locação é bloqueado por qualquer visita aberta. Na data final com visita aberta, a locação entra em ENCERRAMENTO PENDENTE, não gera ocorrências/chegadas novas e encerra automaticamente após resolver a última visita.
- Após o horário: AGUARDANDO gera VISITANTE AGUARDANDO FORA DO HORÁRIO; EM ATENDIMENTO gera ATENDIMENTO EXCEDIDO. A 1 hora da próxima reserva eleva para RISCO DE CONFLITO; no início, CONFLITO ATIVO. O sistema nunca desloca reservas automaticamente.
- Liberação de acesso não muda a visita. Profissional solicita nas próprias visitas; Gerente/Admin em qualquer. Toda ação é confirmada, limitada e auditada. O MVP registra demonstração sem afirmar abertura física.

### Financeiro e perfis

- Estado financeiro é separado da locação. Atraso não encerra locação nem retira profissional do totem.
- Hora é calculada proporcionalmente aos minutos; diária por data no expediente. Preservar tarifa, quantidade, calculado, final e autor do ajuste. Mudança posterior da tarifa não recalcula contrato existente.
- Profissional consulta valor, vencimento e estado das próprias locações, sem alterar.
- ADMINISTRADOR: acesso total, incluindo usuários, permissões, configurações e integrações. GERENTE: gestão operacional. PROFISSIONAL: somente recursos próprios e solicitações. Todos continuam sujeitos às regras e à auditoria.

Questões que não devem ser decididas implicitamente: vínculo contratual/financeiro de uma nova reserva solicitada; efeito da pendência sobre disponibilidade; aprovação após o horário pretendido; chegadas simultâneas para um profissional; fallback sem câmera; competência/vencimento de cobranças futuras; feriados e fuso definitivo do prédio.

## Arquitetura e stack atuais

- .NET SDK fixado em 10.0.400 (`global.json`), aplicação `net10.0`.
- C#, ASP.NET Core Web API, ASP.NET Core Identity, EF Core/SQL Server 10.0.11.
- API hospedável InProcess no IIS pelo AspNetCoreModuleV2.
- React/TypeScript/Vite em `recepcaototem/ClientApp`; o build é incorporado ao publish da API e servido na mesma origem. Em produção, somente Profissionais e Salas operam dados reais; mocks ficam isolados em `ClientApp/src/dev` e não entram no bundle.
- Testes xUnit + `Microsoft.AspNetCore.Mvc.Testing`.
- OpenAPI JSON somente em Development; não há Swagger UI nem OpenAPI em Production.

```text
src/GestaoPredio.Domain
  Auditing/AuditEntry.cs
  Security/SystemRoles.cs
src/GestaoPredio.Application
  Abstractions/IAuditWriter.cs
  Abstractions/IDatabaseProbe.cs
src/GestaoPredio.Infrastructure
  Auditing/EfAuditWriter.cs
  Identity/ApplicationUser.cs
  Persistence/ApplicationDbContext.cs
  Persistence/DesignTimeDbContextFactory.cs
  Persistence/EfDatabaseProbe.cs
  Persistence/Migrations/
recepcaototem
  Api/Health/DatabaseHealthCheck.cs
  Api/Middleware/GlobalExceptionMiddleware.cs
  Program.cs
tests/GestaoPredio.IntegrationTests
```

Dependências: Application → Domain; Infrastructure → Application/Domain; API → Infrastructure. Domain não depende de ASP.NET, EF ou Identity. O host API existente permanece para não deslocar o frontend/layout.

## Produção: SQL Server e IIS

Topologia informada:

```text
IIS :8082 → ASP.NET Core → SQL Server Express local → GestaoPredioDB
```

- Servidor informado: `10.255.36.167`.
- Site IIS: `LumisApi`; Application Pool: `LumisApiPool`.
- Porta interna: `8082`; diretório de publicação: `C:\Sites\Lumis\Api`.
- Conta Windows do processo: `SOPH-SISPONTO\LumisApi`.
- Instância SQL: `localhost\SQLEXPRESS`; banco: `GestaoPredioDB`.
- A conta já possui acesso ao banco segundo o usuário. A aplicação exige autenticação integrada do SQL em Production e rejeita usuário/senha SQL.
- Connection string fica em `ConnectionStrings__DefaultConnection` no ambiente do IIS, nunca no Git. `Security__DataProtectionPath` também é externo e obrigatório em Production.
- Conta runtime mantém privilégio mínimo e não usa `sa`, sysadmin ou db_owner por conveniência. Alteração de esquema usa identidade separada de deploy.
- API não executa `Database.Migrate()` ou `EnsureCreated()` no startup.
- SQL Server não deve ser exposto à internet. Apenas `/health` e `/health/ready` aceitam o binding HTTP interno solicitado; demais rotas exigem HTTPS.
- Nenhuma configuração do IIS, ACL ou banco foi alterada. A tentativa remota ao health em `10.255.36.167:8082` expirou; IIS real ainda não foi validado.

Configurações externas e orientações de ACL/Data Protection: `docs/operations/configuration.md`. Plano de implantação sujeito a aprovação: `docs/operations/iis-foundation.md`.

## Segurança e LGPD

Implementado na fundação: cookies Identity HttpOnly/Secure/SameSite, sessão de 30 minutos, lockout, senha mínima configurada, policies ADMINISTRADOR/GERENTE/PROFISSIONAL, fallback que exige autenticação, respostas 401/403 sem redirect HTML, antiforgery preparado, CORS sem origens por padrão, headers seguros, HTTPS obrigatório fora dos probes, rate limit separado por origem/categoria e liveness isento, erros globais genéricos e logs JSON sem mensagem interna do erro.

Data Protection exige diretório persistente externo em Production e usa DPAPI no Windows. Chaves, fotos futuras, certificados e secrets ficam fora do webroot e do Git. `.gitignore` bloqueia `.env`, chaves/certificados privados, arquivos locais de configuração, bin/obj/artifacts e TestResults.

LGPD prevista na arquitetura: DTOs mínimos e autorização por recurso; foto privada fora do banco/webroot; `PrivateFile`, retenção, anonimização/exclusão e política configurável entram nas etapas de negócio. Não persistir bodies, tokens, fotos, documentos ou senhas em logs/auditoria. A fundação contém somente `AuditEntry` e `IAuditWriter`; os eventos dos módulos ainda não existem.

## Migrations

1. `00000000000000_CreateIdentitySchema`: migration Identity histórica, preservada e movida para Infrastructure. O repositório não comprova que foi aplicada ao `GestaoPredioDB`.
2. `20260904174759_InfrastructureFoundation`: adiciona somente `AuditEntries` e índice por `OccurredAt`.
3. `20260904235115_AuthenticationAndProvisioning`: adiciona os campos Identity/auditoria da autenticação.
4. `20260905052933_ProfessionalsAndRooms`: migration somente aditiva para `PrivateFiles`, `Professionals`, `Rooms` e as colunas de auditoria relacionadas. Os scripts revisáveis e hashes ficam em `artifacts/sql`; a execução de produção é descrita em `docs/operations/professionals-rooms-production-migration.md` e nunca ocorre automaticamente no startup.

O script idempotente foi gerado localmente em `artifacts/migrations.sql`, que é ignorado e reproduzível. Antes de aplicar: inspecionar `__EFMigrationsHistory` e esquema real, revisar script e backup. Se o banco já tiver tabelas Identity sem histórico compatível, reconciliar; não executar a migration inicial cegamente. Aplicação exige autorização e identidade de deploy com DDL, sem ampliar a conta runtime.

## O que está implementado agora

- Solução separada em quatro camadas e referências corretas.
- Identity com `DisplayName`, `IsActive`, `MustChangePassword`, cookie seguro, roles e policies server-side.
- `ApplicationDbContext`, provider SQL Server, factory design-time e probe de conectividade.
- Health liveness `GET /health` e readiness de banco `GET /health/ready`, ambos com corpo genérico.
- Login, sessão, logout, troca obrigatória de senha e antiforgery; respostas genéricas, lockout e limites independentes por IP e identificador derivado do e-mail.
- Criação de usuários somente por ADMINISTRADOR, com senha temporária mostrada uma vez, e CLI transacional separada para o primeiro administrador.
- Tratamento global de erros, logging JSON, CORS restrito, rate limiting e segurança HTTP inicial.
- Base persistente de auditoria e migration correspondente.
- Configurações por ambiente sem connection string de produção.
- Publicação IIS com SPA em `wwwroot`; a CLI possui pacote separado e não entra no webroot.
- Documentação e ferramenta `dotnet-ef` fixada em `dotnet-tools.json`.

Não implementado: usuários reais, criação dos roles no banco, login/logout/antiforgery token, cadastros, agenda, locações, cobranças, visitas, fotos, retenção, notificações, SignalR, Meta ou Intelbras. O frontend ainda usa mocks/localStorage e autenticação demonstrativa; ele não integra a API atual e não faz parte do pacote IIS do backend.

## Testes e evidências atuais

Última verificação da fundação: restore concluído; build Release com 0 avisos/erros; 16 testes aprovados, 0 falhas/ignorados; publish concluído. O pacote publicado iniciou em Production e respondeu `/health` com HTTP 200 `Healthy`; sem conexão, `/health/ready` respondeu HTTP 503 `Unhealthy`, sem detalhes. `web.config` usa AspNetCoreModuleV2, InProcess e `recepcaototem.dll`.

Testes cobrem liveness/readiness, negação anônima, OpenAPI fechado em Production, CORS, HTTPS das rotas, HTTP 429 sem derrubar liveness, boundaries dos roles, erro sem stack/mensagem sensível e modelo EF SQL Server. O readiness saudável usa probe controlado; não comprova acesso ao SQL real. Não há banco de integração real conectado nesta etapa.

## Pendências e próximos passos exatos

### Infraestrutura, antes das regras de negócio

1. Obter acesso operacional ao servidor e autorização para as mudanças listadas em `docs/operations/iis-foundation.md`.
2. Fazer backup da publicação e da configuração atual; inspecionar site/pool sem alterar.
3. Configurar externamente ambiente, connection string integrada, AllowedHosts e diretório privado de Data Protection; revisar ACL proposta da conta `SOPH-SISPONTO\LumisApi` antes de aplicar.
4. Publicar pacote em `C:\Sites\Lumis\Api`, reciclando apenas `LumisApiPool`, e validar localmente `http://localhost:8082/health` e `/health/ready`.
5. Inspecionar esquema/histórico do `GestaoPredioDB`. Apresentar script e alterações antes de aplicar migrations. Executar com identidade de deploy separada somente após aprovação.
6. Revalidar readiness pelo processo IIS e registrar evidência/rollback.

### Desenvolvimento do MVP após a fundação

1. Implementar login/logout/session e provisionamento operacional seguro do primeiro Admin e dos roles, sem senha padrão.
2. Criar entidades/configurações de Professional, Tenant, Room, Lease e Charge com migrations aditivas e testes.
3. Implementar agenda, recorrências por janela, expediente/bloqueios e conflitos transacionais.
4. Implementar solicitações e aprovações, incluindo concorrência e antecedência mínima.
5. Integrar totem, armazenamento privado, visitas, retenção e auditoria.
6. Implementar ciclo de locação, alertas SignalR, acesso demonstrativo e notificador Meta demonstrativo/real condicionado.
7. Substituir localStorage/mocks do frontend pela API sem alterar o design aprovado.
8. Executar testes de segurança, backup/restauração, câmera real e homologação dos perfis.

Não adicionar regras para os pontos em aberto. Consultar o usuário quando cada ponto bloquear a respectiva etapa.

## Continuar em outra máquina

Pré-requisitos: Git com acesso ao repositório privado; SDK .NET 10 compatível com `global.json`; Node.js apenas ao trabalhar no frontend; SQL Server/LocalDB de desenvolvimento opcional e separado de produção.

```powershell
git clone https://github.com/1ryann/recepcaolumis.git
cd recepcaolumis
git switch main
git pull --ff-only
dotnet --version
dotnet tool restore
dotnet restore recepcaototem.sln
dotnet build recepcaototem.sln -c Release --no-restore
dotnet test recepcaototem.sln -c Release --no-build
dotnet publish recepcaototem/recepcaototem.csproj -c Release --no-restore -o artifacts/publish/LumisApi
```

Smoke test sem banco, em Development:

```powershell
dotnet run --project recepcaototem --launch-profile http
Invoke-RestMethod http://localhost:5218/health
```

Para desenvolvimento com SQL, definir `ConnectionStrings:DefaultConnection` por User Secrets no projeto web ou variável de ambiente, usando banco exclusivo de desenvolvimento. Não copiar valores de produção. O backend não lê `.env` automaticamente.

Gerar migration futura e script sem executar o banco:

```powershell
dotnet ef migrations add NomeDaMigration --project src/GestaoPredio.Infrastructure --startup-project recepcaototem --output-dir Persistence/Migrations
dotnet ef migrations script --idempotent --project src/GestaoPredio.Infrastructure --startup-project recepcaototem --output artifacts/migrations.sql
```

Nunca usar `dotnet ef database update` contra produção sem inspeção, backup, identidade de deploy e aprovação expressa. Nunca colocar connection strings, senhas, tokens, certificados, secrets Meta/Intelbras ou chaves privadas no Git.

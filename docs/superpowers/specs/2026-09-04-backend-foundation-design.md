# Fundação do backend — estrutura e modelagem inicial

Escopo: fundação do MVP congelado em `2026-09-04-mvp-congelado.md`. Esta proposta apresenta a organização antes das regras complexas. Não modifica políticas comerciais, visuais ou de aprovação.

## Estrutura proposta

```text
recepcaototem.sln
global.json
src/
  GestaoPredio.Domain/
    Common/
    Professionals/
    Rooms/
    Leasing/
    Scheduling/
    Visits/
    Billing/
    Access/
    Privacy/
    Auditing/
  GestaoPredio.Application/
    Abstractions/
    Authentication/
    Authorization/
    Professionals/
    Leasing/
    Scheduling/
    Visits/
    Billing/
    Privacy/
    Auditing/
  GestaoPredio.Infrastructure/
    Persistence/
      ApplicationDbContext.cs
      Configurations/
      Migrations/
    Identity/
    Auditing/
    Files/
    Jobs/
    Integrations/
recepcaototem/                 # host API ASP.NET Core já existente
  Program.cs
  Api/
    Endpoints/
    Middleware/
    Configuration/
  ClientApp/                  # frontend existente preservado
tests/
  GestaoPredio.Domain.Tests/
  GestaoPredio.Application.Tests/
  GestaoPredio.IntegrationTests/
docs/operations/
```

Domain não referencia EF, Identity ou ASP.NET. Application referencia Domain. Infrastructure referencia Application/Domain e implementa persistência/Identity/arquivos/serviços externos. O host referencia Application/Infrastructure para composição, expõe API e preserva compatibilidade do projeto atual. Não criar um segundo host ou duplicar o DbContext.

Alternativas consideradas: pastas no único projeto são menores, mas não impõem separação por compilação; mover também o host/frontend agora aumenta mudanças sem benefício imediato. Bibliotecas separadas e host existente atendem às camadas solicitadas com menor movimentação do visual.

## Identidade e permissões

`ApplicationUser : IdentityUser` permanece em Infrastructure. Manter chave string do Identity atual para evitar conversão desnecessária do esquema. Campos adicionais: nome de exibição, vínculo opcional `ProfessionalId` e estado ativo. Roles exatas: ADMINISTRADOR, GERENTE, PROFISSIONAL. Associação usuário-profissional e permissões passam pelo backend; nenhum ProfessionalId fornecido pelo cliente concede acesso.

Policies: administração de usuários/permissões/integrações somente Admin; gestão operacional Admin/Gerente; recursos profissionais por vínculo autenticado. Configurações operacionais concedidas à Gerente pelo escopo permanecem permitidas. Nenhum papel contorna regras de negócio ou auditoria.

Sessão web por cookie seguro e antiforgery; login/logout/session como primeiros endpoints. Erros 401/403 da API não redirecionam para HTML. Sem cadastro público de administradores, conta padrão ou senha no repositório. Roles e primeiro administrador provisionados por operação explícita de implantação, não por criação silenciosa no startup. A forma operacional de provisionamento será documentada com a implementação.

## Entidades iniciais

IDs de negócio GUID; datas operacionais em DateTimeOffset/UTC, datas de competência em DateOnly, horários recorrentes locais com fuso explícito. Valores monetários decimal(18,2); cálculo proporcional usa precisão intermediária antes do valor final em centavos. RowVersion em registros mutáveis configurado no EF; histórico sem exclusão em cascata por conveniência.

| Entidade | Dados e relações principais |
|---|---|
| Professional | Id, Name, Profession, WhatsApp, PhotoFileId opcional, IsActive; não contém sala atual fixa |
| Tenant | Id, tipo pessoa/empresa, nome e contato necessários; vários contratos; sem CPF/RG obrigatório inventado |
| Room | Id, Number, Floor, HourlyRate, DailyRate; disponibilidade derivada dos períodos |
| Lease | TenantId, ProfessionalId, RoomId, modalidade mensal/dia/hora, estado, início financeiro, início ocupação, término opcional, valor mensal opcional, conferência e encerramento com autor/data |
| RecurrenceRule | LeaseId, vigência, dia da semana, horário inicial/final e fuso; múltiplas regras por locação; sem materialização infinita |
| ReservationOccurrence | LeaseId, regra opcional, início original, início/fim efetivos, estado e RowVersion; fonte da visita e exceções pontuais |
| ReservationRequest | ProfessionalId, tipo nova/reagendamento/cancelamento, ocorrência original opcional, sala/período solicitado quando aplicável, estado, solicitante, resposta/decisor/justificativa |
| Charge | LeaseId, ocorrência opcional, referência de cobrança, vencimento, tarifa aplicada, quantidade/unidade, valor calculado, valor final, situação, autor de ajuste |
| Visit | OccurrenceId, VisitorName, PhotoFileId opcional para suportar remoção por retenção, ArrivedAt, estado; vínculo de profissional/sala obtido da ocorrência |
| VisitTransition | VisitId, estado anterior/novo, ActorUserId, momento e justificativa quando excepcional |
| AccessEvent | VisitId, solicitante, instante, resultado, modo demonstração/real, porta/dispositivo quando disponível, correlação e IP apropriado |
| BuildingSchedule | Dia da semana, início/fim do expediente global; configuração versionada |
| RoomBlock | RoomId, início/fim, autor e data de criação |
| PrivateFile | Chave interna, tipo, tamanho, finalidade, criação, limite de retenção e remoção; conteúdo fora do banco/webroot |
| PrivacySettings | Aviso, política, responsável, contato, retenção e políticas de fotos/visitantes |
| AuditEntry | Ator opcional para eventos automáticos/anônimos, ação, entidade/ID, UTC, resultado, correlação, IP e detalhes mínimos sem secrets |

A nulabilidade da referência da foto suporta retenção/exclusão, não cria autorização para dispensar captura. Solicitação de nova reserva não recebe vínculo contratual/financeiro inventado: representação permite pendência, e a decisão de negócio ainda não definida será consultada antes da aprovação funcional.

Estados: locação AGENDADA/ATIVA/ENCERRAMENTO PENDENTE/ENCERRADA; solicitação PENDENTE/APROVADA/RECUSADA; visita AGUARDANDO/EM ATENDIMENTO/ENCERRADA/CANCELADA. Estado da ocorrência separado do atendimento e pagamento; CANCELADA preserva histórico. Não adicionar estados comerciais sem necessidade aprovada.

## Persistência e migrations

Um ApplicationDbContext deriva de IdentityDbContext<ApplicationUser>. Entidades/configurações ficam organizadas por módulo. SQL Server provider 10.0.11 acompanha net10.0 já instalado.

Inspecionar e preservar migrations Identity existentes e histórico antes de movê-los para Infrastructure; não reescrever IDs de migrations nem executar novo CreateIdentity sobre banco existente. Criar migration aditiva de fundação, revisar SQL e versionar. Não assumir que o banco informado está vazio.

Configurar FKs, campos obrigatórios, limites de strings, índices de consulta e chave única de regra+início original. Soft delete somente onde fizer sentido; nenhuma exclusão em cascata deve apagar visitas, finanças ou auditoria automaticamente.

Índices únicos e RowVersion não bastam para impedir sobreposição de intervalos. A camada futura de agenda coordenará transações e bloqueios SQL por sala/profissional e revalidação, inclusive recorrências além da janela gerada. Essa regra complexa não pertence ao primeiro scaffold.

## Ambientes e IIS

Produção informada pelo usuário: SQL Server Express, instância local nomeada, banco já nomeado, IIS e conta Windows própria. Não copiar a connection string de produção para arquivos versionados. Injetar `ConnectionStrings__DefaultConnection` externamente no ambiente do processo IIS, sob ACL da implantação; appsettings só contém opções não secretas. Desenvolvimento usa User Secrets/variáveis e banco independente. Nenhum fallback automático para produção.

Windows Authentication é da conta de processo IIS para SQL Server; usuários web usam Identity/cookies. A identidade Windows da aplicação recebe apenas permissões de execução necessárias ao banco. Operador de migrations separado tem permissões DDL durante implantação; conta runtime não usa sa/sysadmin/db_owner por conveniência.

Não chamar Database.Migrate ou EnsureCreated no startup. Migrations por script idempotente revisado ou bundle executado explicitamente pelo operador autorizado, após backup. Nenhum acesso ou alteração do banco de produção foi autorizado como parte desta apresentação.

TLS SQL deve ser validado com certificado adequado ao nome da instância; não adotar TrustServerCertificate como padrão de produção para mascarar erros. Confirmar configuração real ao validar implantação.

Data Protection persiste em diretório privado com ACL da conta IIS e proteção apropriada para chaves. Fotos em diretório privado distinto, acesso pela API autorizada. Não gerar URL pública ou colocar uploads em wwwroot. Jobs da aplicação precisam sobreviver a reciclagem/ociosidade do IIS por estado persistido e configuração operacional apropriada; não depender de SQL Server Agent no Express.

## Segurança, auditoria e LGPD na fundação

- Cookie HttpOnly/Secure/SameSite apropriado; antiforgery nas mutações; expiração/lockout/revogação; sem token localStorage.
- Rate limiting por classe de operação com 429, login incluindo identidade normalizada e origem para reduzir abuso; limites técnicos configuráveis por ambiente sem mudar prazo comercial de solicitações.
- Autorização em endpoints/casos de uso e consulta por recurso; DTOs explícitos, não entidades recebidas diretamente.
- Erros globais padronizados com código/mensagem/correlação e sem SQL/stack trace; cancelamento de request não vira vazamento ou erro interno falso.
- Auditoria transacional para mutações, eventos de login independentes do sucesso; logs técnicos mínimos. Nunca persistir request completo, cookies, tokens, foto ou senha em auditoria.
- Retenção de foto prevista desde a entidade/armazenamento, remoção idempotente e verificável, política acessível; definir exclusão/anonimização sem invalidar a rastreabilidade financeira/histórica.
- CORS por origens explícitas se necessário, HTTPS/headers, proxies confiáveis e proteção de uploads. Sem integração de porta pelo browser.

## Ordem da fundação e verificação

1. Criar bibliotecas/referências e preservar host/frontend; compilação valida direção das dependências.
2. Introduzir entidades/configurações, mover DbContext/Identity e preservar histórico de migrations.
3. Registrar providers/configuração/validação de ambiente, sem conectar produção implicitamente.
4. Implementar sessão/roles/policies/DTOs, rate limit, antiforgery, tratamento de erros e auditoria.
5. Gerar migration versionada e SQL para revisão, sem aplicação automática.
6. Testar login/perfis/401/403/CSRF/429, acesso alheio, auditoria, esquema SQL e ausência de inicialização automática do banco. SQL de teste isolado, nunca produção.

Regras completas de agenda, recorrência, preço, aprovações, visitas e acesso serão implementadas nas etapas seguintes do plano congelado. Instalar uma fundação não significa declarar esses módulos funcionais.

# Profissionais e Salas — design aprovado

Data: 05/09/2026  
Status: aprovado para planejamento, ainda não autorizado para implementação ou produção

## Objetivo e limites

Esta etapa implementa o gerenciamento real de Profissionais e Salas sobre a fundação .NET 10, SQL Server, Identity e React existente. `ADMINISTRADOR` e `GERENTE` gerenciam os dois cadastros pela policy `Operations`; operações de associação entre profissional e conta Identity pertencem somente ao `ADMINISTRADOR`, pela policy `Administration`.

Não fazem parte desta etapa locações, reservas, recorrências, agenda, disponibilidade, bloqueios, visitantes, totem funcional, financeiro, WhatsApp Meta, Intelbras, MFA, Cloudflare, IIS ou deployment. Não haverá hard delete administrativo, criação automática de contas, endpoint `/api/professionals/me`, migration automática ou alteração da autenticação existente.

Profissionais e Salas usarão exclusivamente a API real, inclusive em Development. Dashboard, Recepção, Locações, Visitas e Configurações permanecem demonstrativos somente no servidor Vite em Development; o build de produção renderiza `ModuleUnavailable` e não monta o `AppStoreProvider`.

## Arquitetura

A solução mantém as dependências atuais:

- Domain contém entidades, constantes e normalizadores puros;
- Application contém casos de uso, contratos e abstrações de storage;
- Infrastructure contém EF Core, SQL Server e filesystem privado;
- API contém endpoints mínimos por feature, DTOs, policies, antiforgery e tradução de erros;
- React contém clientes tipados, telas e estado local de cada módulo real.

Não será criado generic repository nem arquitetura paralela. Regras de domínio e validações reutilizáveis não ficam duplicadas em endpoints.

## Modelo de dados

### Professional

| Campo | Tipo e regra |
|---|---|
| Id | `uniqueidentifier`, identidade técnica única |
| Name | `nvarchar(200)`, obrigatório |
| NormalizedName | `nvarchar(200)`, calculado no backend |
| Profession | `nvarchar(150)`, obrigatório |
| NormalizedProfession | `nvarchar(150)`, calculado no backend |
| WhatsApp | `varchar(16)`, E.164 canônico |
| PhotoFileId | `uniqueidentifier`, nullable |
| ApplicationUserId | `nvarchar(450)`, nullable |
| IsActive | `bit`, novos registros começam ativos |
| CreatedAt / UpdatedAt | `datetimeoffset`, UTC e controlados pelo servidor |
| RowVersion | SQL Server `rowversion`, concurrency token |

Nome, profissão e WhatsApp não possuem unicidade. Homônimos, especialidades iguais, números compartilhados e até combinações cadastrais idênticas são permitidos; referências futuras usam o GUID. `ApplicationUserId`, quando preenchido, é exclusivo por índice único filtrado. `PhotoFileId` também usa índice único filtrado.

### Room

| Campo | Tipo e regra |
|---|---|
| Id | `uniqueidentifier` |
| Name | `nvarchar(100)`, obrigatório e exibido na UI |
| NormalizedName | `nvarchar(100)`, chave de unicidade permanente |
| Description | `nvarchar(1000)`, nullable |
| HourlyRate / DailyRate | `decimal(18,2)`, obrigatórios |
| IsActive | `bit`, novos registros começam ativos |
| CreatedAt / UpdatedAt | `datetimeoffset`, UTC e controlados pelo servidor |
| RowVersion | SQL Server `rowversion`, concurrency token |

`Room.NormalizedName` possui índice único para salas ativas e inativas. Desativar não libera o nome. `Sala 1` e `Sala 01` continuam distintas.

Tarifas aceitam valores de zero a `9999999999999.99`, com no máximo duas casas. O backend usa `decimal` do início ao fim, rejeita valores negativos, acima do teto ou com escala maior que dois e não arredonda nem trunca. O banco mantém `decimal(18,2)` e checks de não negatividade.

### PrivateFile

| Campo | Tipo e regra |
|---|---|
| Id | `uniqueidentifier` |
| StorageKey | `varchar(64)`, aleatório e único |
| MimeType | `varchar(20)`, derivado do parser |
| Length | `bigint`, positivo |
| Purpose | `varchar(50)`, valor controlado |
| CreatedAt | `datetimeoffset`, UTC |

Um check no banco aceita inicialmente somente `PROFESSIONAL_PHOTO`. Dimensões são validadas, mas não persistidas. Os bytes ficam fora do SQL Server e do `wwwroot`.

As FKs `Professional.PhotoFileId -> PrivateFiles.Id` e `Professional.ApplicationUserId -> AspNetUsers.Id` usam `NoAction`, sem cascade. A auditoria não possui FK para entidades de domínio.

### Auditoria

`AuditEntry` recebe campos opcionais aditivos:

```text
TargetEntityType nvarchar(50)
TargetEntityId   uniqueidentifier
ChangedFields    nvarchar(500)
```

O índice `(TargetEntityType, TargetEntityId, OccurredAt)` é adicionado sem remover os existentes. `TargetEntityType` é definido apenas pelo backend como `PROFESSIONAL` ou `ROOM`. `ChangedFields` contém somente nomes de campos efetivamente alterados, em ordem estável, como `Name,Profession,WhatsApp`; nunca contém valores. Eventos de autenticação permanecem válidos com os novos campos nulos.

Eventos:

- `PROFESSIONAL_CREATED`, `PROFESSIONAL_UPDATED`, `PROFESSIONAL_ACTIVATED`, `PROFESSIONAL_DEACTIVATED`;
- `PROFESSIONAL_PHOTO_UPLOADED`, `PROFESSIONAL_PHOTO_REPLACED`, `PROFESSIONAL_PHOTO_REMOVED`;
- `PROFESSIONAL_USER_LINKED`, `PROFESSIONAL_USER_UNLINKED`, `PROFESSIONAL_USER_REPLACED`;
- `ROOM_CREATED`, `ROOM_UPDATED`, `ROOM_ACTIVATED`, `ROOM_DEACTIVATED`.

Eventos de vínculo usam `TargetEntityId = Professional.Id` e `TargetUserId = ApplicationUser.Id`. Auditoria não registra WhatsApp, nomes, e-mails, filename, storage key, caminhos, bytes, MIME desnecessário, tokens ou exceptions.

## Normalização e validação

Um único `TextNormalizer` executa trim, colapso de whitespace, decomposição Unicode, remoção de marcas diacríticas, recomposição e `ToUpperInvariant`. Ele calcula `Professional.NormalizedName`, `Professional.NormalizedProfession`, `Room.NormalizedName` e termos de busca. Campos normalizados nunca entram ou saem em DTOs públicos.

Profissionais e Salas pesquisam as colunas normalizadas no SQL Server. Contas Identity elegíveis pesquisam `DisplayName` e `Email` com `EF.Functions.Collate` e uma constante interna para `Latin1_General_100_CI_AI`; nenhuma collation do banco, tabelas ou Identity é alterada.

Um normalizador de WhatsApp aceita máscara, espaços e pontuação brasileira. Dez ou onze dígitos nacionais recebem `+55`; números internacionais precisam trazer `+` e seguir E.164, com até quinze dígitos. Letras, ramais, vazios, tamanhos inválidos e internacionais ambíguos são rejeitados. O banco armazena somente a forma canônica.

DTOs novos rejeitam propriedades JSON não mapeadas de forma localizada. Não haverá mudança global de `UnmappedMemberHandling` sem prova de compatibilidade com todos os contratos publicados de autenticação. Multipart aceita somente `file` e `concurrencyToken`.

## API de Profissionais

```text
GET    /api/admin/professionals
GET    /api/admin/professionals/{id:guid}
POST   /api/admin/professionals
PUT    /api/admin/professionals/{id:guid}
POST   /api/admin/professionals/{id:guid}/activate
POST   /api/admin/professionals/{id:guid}/deactivate
GET    /api/admin/professionals/{id:guid}/photo
PUT    /api/admin/professionals/{id:guid}/photo
DELETE /api/admin/professionals/{id:guid}/photo
```

Todos exigem `Operations`; GETs não exigem antiforgery, e todas as mutações exigem. O DTO público contém `id`, `name`, `profession`, `whatsapp`, `isActive`, `hasPhoto`, `photoUrl`, `hasLinkedUser`, datas e `concurrencyToken`. Não contém IDs Identity, storage key, path ou metadados internos.

POST aceita somente nome, profissão e WhatsApp, cria registro ativo e retorna 201 com `Location`. PUT cadastral aceita esses três campos e o token. Status, foto e vínculo usam operações explícitas. Não existe DELETE do profissional.

## API de Salas

```text
GET  /api/admin/rooms
GET  /api/admin/rooms/{id:guid}
POST /api/admin/rooms
PUT  /api/admin/rooms/{id:guid}
POST /api/admin/rooms/{id:guid}/activate
POST /api/admin/rooms/{id:guid}/deactivate
```

Todos exigem `Operations`, com antiforgery nas mutações. POST aceita nome, descrição e duas tarifas; PUT adiciona o token. Não aceita status, datas, normalizados ou campos técnicos. Não existe DELETE da sala.

O DTO contém `id`, `name`, `description`, tarifas numéricas, `isActive`, datas e token. `IsActive` representa apenas estado cadastral, nunca disponibilidade.

## Paginação e busca

As listagens aceitam `search`, `status`, `page` e `pageSize`. Página inicia em 1; tamanho padrão é 20 e varia de 1 a 100. Status aceita `all`, `active` e `inactive`. Busca possui até 100 caracteres; vazia após normalização equivale a ausente. Parâmetros inválidos retornam `INVALID_PAGE`, `INVALID_PAGE_SIZE`, `INVALID_STATUS` ou `INVALID_SEARCH`.

Filtros, `CountAsync`, ordenação e paginação permanecem em `IQueryable`, com `AsNoTracking` para leitura. Profissionais buscam nome ou profissão; Salas buscam nome. Ordenação usa nome normalizado e GUID. Página além do total retorna lista vazia com `totalCount` filtrado.

## Associação com Identity

```text
GET    /api/admin/professionals/eligible-users
GET    /api/admin/professionals/{id:guid}/user-link
PUT    /api/admin/professionals/{id:guid}/user-link
DELETE /api/admin/professionals/{id:guid}/user-link
```

Os quatro endpoints exigem `Administration`; mutações exigem antiforgery e token. A constraint `{id:guid}` evita colisão com `eligible-users`.

Contas elegíveis estão ativas, possuem role exata `PROFISSIONAL` e não estão vinculadas. A consulta paginada retorna apenas `userId`, `displayName` e `email`. O GET do vínculo retorna `{ linked: false }` ou os mesmos três dados mínimos; 404 significa profissional inexistente.

PUT revalida existência, atividade, role e exclusividade no momento da escrita. Primeiro vínculo audita `LINKED`, troca audita `REPLACED`, e repetir a mesma conta com token atual é no-op. DELETE remove somente a referência; ausência de vínculo com token atual é no-op. Nenhuma operação altera usuário, role ou estado Identity. Conta inválida retorna `400 INVALID_PROFESSIONAL_USER`; vínculo usado retorna `409 PROFESSIONAL_USER_ALREADY_LINKED`.

## Concorrência e idempotência

`RowVersion` é exposto como `concurrencyToken` Base64 opaco. Criação não recebe token. Toda mutação posterior valida primeiro estrutura e correspondência com o estado atual. Token ausente ou inválido retorna 400; token antigo retorna `409 RESOURCE_MODIFIED`, mesmo se o estado solicitado já tiver sido alcançado por outra operação.

Somente após validar o token o backend normaliza e detecta no-op. No-op retorna 200 com o mesmo recurso, datas e token, sem `SaveChanges` nem auditoria. Mutação real atualiza `UpdatedAt`, recebe novo rowversion, grava auditoria na mesma transação e retorna o novo estado. A UI recarrega em conflito e exige nova decisão do usuário, sem merge ou reenvio automático.

Erros SQL 2601/2627 somente são convertidos quando o índice conhecido for identificado com segurança: nome duplicado vira `ROOM_NAME_ALREADY_EXISTS`; conta já utilizada vira `PROFESSIONAL_USER_ALREADY_LINKED`. Outras falhas seguem o middleware global.

## Armazenamento privado e upload

Configurações:

```text
Storage__PrivateFilesPath
Storage__ProfessionalPhotoMaxBytes = 5242880
```

A raiz é obrigatória em todos os ambientes executáveis. Deve ser absoluta, existir e permitir probe seguro de criação/remoção. Após `Path.GetFullPath`, não pode estar dentro do content root/webroot/publicação nem ser pai deles; comparações respeitam o sistema operacional. Não há fallback. Em Production a aplicação não cria a raiz nem altera ACL, mas pode criar subdiretórios internos constantes. Falhas usam mensagem operacional genérica e nunca registram o caminho privado completo.

O limite padrão é 5 MiB, mínimo 1 byte e teto compilado 10 MiB. Options fortemente tipadas validam no startup. O limite multipart é específico ao endpoint, com pequena margem, e não substitui contagem durante a cópia.

Arquivos temporários ficam em subárea controlada no mesmo volume. Nomes e chaves são aleatórios; criação usa semântica `CreateNew`. Todo erro ou cancelamento limpa o temporário. O move final não sobrescreve; colisão gera nova chave ou falha fechada. Paths combinam apenas raiz validada, finalidade constante e chave interna, e são conferidos novamente abaixo da raiz.

O filename passa por `Path.GetFileName`, precisa resultar em nome não vazio e serve somente para obter a extensão final. Nomes como `dra.ana.png` e `foto.exe.png` podem ser aceitos; não existe blacklist intermediária. Extensão final, MIME multipart e formato real precisam concordar de forma case-insensitive. Nome ou extensão ausente é rejeitado. Filename não é persistido, registrado, devolvido nem usado no filesystem.

O parser defensivo valida estruturalmente JPEG, PNG e WebP, com bounds checks, aritmética segura, limites de chunks/segmentos e nenhuma alocação baseada em comprimento declarado. JPEG valida marcadores, scan, stuffing/restarts, dimensões, EOI e ausência de bytes posteriores. PNG valida assinatura, IHDR único e primeiro, comprimentos, CRCs e IEND final. WebP valida RIFF, padding, tamanho e VP8/VP8L/VP8X.

Limites: largura e altura entre 1 e 4096 e até 16.777.216 pixels, calculados em `long`. MIME persistido deriva apenas do parser. Qualquer falha retorna `400 INVALID_PROFESSIONAL_PHOTO`.

### Troca e remoção

Depois de validar o token, o novo arquivo é validado e movido fisicamente antes da transação. A transação cria `PrivateFile`, troca a referência, atualiza o profissional e audita. Falha de banco remove o novo arquivo. Após commit, a limpeza antiga segue estritamente: remover bytes e, somente se isso funcionar, remover o metadado. Falha mantém o metadado órfão e registra apenas seu GUID. Não haverá job de órfãos nesta etapa.

Remoção limpa a referência e audita na transação antes da mesma limpeza posterior. GET nunca repara estado. A leitura valida profissional, vínculo, metadado, finalidade, existência e tamanho físico compatível; não reprocessa ou corrige o arquivo. Inconsistência de storage retorna `503 PHOTO_UNAVAILABLE` e log seguro. Headers: MIME validado, `nosniff`, `Content-Disposition: inline` e `Cache-Control: private, no-store`. Não há range, thumbnail, resize, recompressão, EXIF, CDN ou retenção automática.

## Frontend

O cliente HTTP mantém cookies same-origin e antiforgery em memória e ganha GET/POST/PUT/DELETE/multipart, `ApiError` por código e `AbortSignal` para leituras obsoletas. Multipart deixa o browser criar o boundary. Mutações bloqueiam duplo submit; conexão perdida após envio gera estado indeterminado e recarga antes de outra ação, nunca afirma rollback.

Profissionais preserva tabela e modais, remove Sala e usa API para busca com debounce, status, paginação, CRUD sem delete, foto e vínculo. Preview usa `URL.createObjectURL` e revoga ao trocar, cancelar, fechar e desmontar. Criação e foto são duas etapas; falha de foto informa que o cadastro foi salvo sem foto. Imagem indisponível mostra placeholder. Somente Admin vê controles e dados mínimos do vínculo; Gerente vê apenas `hasLinkedUser`.

Salas preserva cards e identidade visual, mas remove ocupação, profissional, locação, vencimento e disponibilidade. Exibe cadastro, descrição, tarifas e estado ativo, com busca, filtro, paginação, criação, edição e status.

Entradas monetárias usam string controlada e parser brasileiro central: aceita `0`, `0,01`, `100`, `100,5`, `100,50` e `1.234,56`; rejeita expoente, valores especiais, três casas, formato anglo misto e texto. A estrutura é validada antes de remover milhares e trocar vírgula. O request envia JSON number. Não há cálculo financeiro. Visualização usa `Intl.NumberFormat`.

O bootstrap de produção não importa ou monta AppStore/mocks. Imports demonstrativos são dinâmicos somente no branch estático `import.meta.env.DEV`, sem flag externa. Em produção, módulos fora do escopo mostram `ModuleUnavailable` mantendo rotas e shell.

## Erros e segurança

401 e 403 continuam definidos por Identity/policies. Validações retornam 400, ausências 404, concorrência/duplicidades 409 e storage inconsistente 503. Respostas não expõem stack, SQL, paths, chaves ou detalhes do parser. Todas as mutações usam antiforgery; não há JWT, localStorage de autenticação, CORS amplo ou confiança em IDs do cliente para propriedade.

## TDD e verificação

Testes unitários cobrem normalizadores, tarifas, parser monetário React, concurrency token, options/path, formatos de imagem, storage keys e `ChangedFields`. Testes do parser usam fixtures válidas, truncamentos e amostras pseudoaleatórias limitadas com seed reproduzível e tempo previsível.

Integração usa somente `GestaoPredioModulesTests` ou derivados explícitos. A factory recusa Production, conexão ausente, `GestaoPredioDB`, banco fora do prefixo e qualquer fallback para a conexão normal. Cada cenário usa storage root exclusivo; testes paralelos não compartilham banco mutável, temporário ou chave.

A suíte cobre 401/403 e sucesso de Admin/Gerente, DTOs, validação, paginação, busca com acentos, duplicidades permitidas de profissional, unicidade de sala, concorrência, no-ops, auditoria, foto, compensação, vínculo e corridas. Regressão executa autenticação e CLI existentes. React cobre fluxos críticos, estados, moeda, concorrência, roles e isolamento dos mocks. O round-trip monetário cobre `0`, `0.01`, `0.10`, `100.99` e `9999999999999.99`, garantindo ausência de perda observável de centavos no fluxo esperado, sem afirmar exatidão binária do `Number`.

## Migration e operação futura

A migration `ProfessionalsAndRooms` cria `PrivateFiles`, `Professionals`, `Rooms`, colunas opcionais de auditoria, checks, FKs `NoAction` e índices. Ela não contém DROP, TRUNCATE, DELETE, rename, alteração de collation, cascade ou mudanças desnecessárias em Identity.

Serão gerados SQL idempotente completo e SQL forward específico de `AuthenticationAndProvisioning` para `ProfessionalsAndRooms`, com SHA-256. Nenhum será executado em produção.

O procedimento futuro será backup validado, revisão/aprovação do SQL forward, migration manual, publish compatível e validação. Rollback de aplicação pode preservar as estruturas aditivas. Produção nunca usará `dotnet ef database update <migration-anterior>` nem `Down()` automático; eventual reversão de banco exige aprovação separada e recuperação planejada por backup.

Antes de concluir a implementação local serão executados restore, build Release, todos os testes .NET, `npm ci`, testes/build React, publish e inspeção de pacote. O relatório trará números reais, SQL/hashes, arquivos principais, endpoints, policies, auditoria, storage, commits, riscos e `git status`. O pacote deve conter SPA e excluir CLI, fontes indevidas, `.env`, secrets, arquivos privados, temporários e mocks operacionais.

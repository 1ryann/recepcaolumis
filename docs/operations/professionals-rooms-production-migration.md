# Profissionais e Salas — migration de produção

Este procedimento descreve uma operação futura e não autoriza mudança de produção. A aplicação nunca executa migrations no startup e a identidade `SOPH-SISPONTO\LumisApi` não recebe DDL nem permissões adicionais.

## Artefatos para revisão

Revisar os arquivos gerados localmente em `artifacts/sql`:

- `ProfessionalsAndRooms.sql`: avanço de `20260904235115_AuthenticationAndProvisioning` até `20260905052933_ProfessionalsAndRooms`.
- `idempotent-current.sql`: script idempotente do histórico completo.
- `SHA256SUMS.txt`: SHA-256 aprovado dos dois scripts.

O script forward é somente aditivo: adiciona `ChangedFields`, `TargetEntityId` e `TargetEntityType` em `AuditEntries`; cria `PrivateFiles`, `Professionals` e `Rooms`; cria índices, checks e FKs `NoAction`. Ele não contém `DROP`, `TRUNCATE`, `DELETE`, `ALTER DATABASE`, `COLLATE`, cascade ou alteração de tabela Identity.

## Janela autorizada futura

1. Obter aprovação explícita, confirmar backup recente com restauração verificada e conferir `__EFMigrationsHistory`.
2. Comparar o SHA-256 do `ProfessionalsAndRooms.sql` com o valor aprovado em `SHA256SUMS.txt`.
3. Uma identidade operacional/deploy separada, com DDL temporário previamente autorizado, executa manualmente o script forward revisado. A conta runtime não executa SQL de schema.
4. Conferir a nova linha no histórico, tabelas, índices e checks. Não executar `Down`, `database update <migration anterior>` nem rollback improvisado.
5. Publicar o pacote somente pelo procedimento aprovado em separado. A migration não altera IIS, firewall, bindings, SQL Server ou permissões da aplicação.

## Arquivos privados de fotos

Antes de liberar upload de foto, configurar `Storage__PrivateFilesPath` externamente. A pasta deve ficar fora do webroot e da publicação e conceder somente Modify para `SOPH-SISPONTO\LumisApi`; o limite padrão é 5 MiB e o teto aceito é 10 MiB. Não copiar fotos ou a pasta privada no pacote IIS.

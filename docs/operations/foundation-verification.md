# Verificação da entrega de infraestrutura — 04/09/2026

- Documentação existente commitada antes do código: a5dd133.
- SDK selecionado: 10.0.400; alvo net10.0; EF Core/Identity/OpenAPI 10.0.11.
- dotnet restore: concluído.
- dotnet build Release: 0 avisos e 0 erros.
- dotnet test Release: 16 testes aprovados, 0 falhas, 0 ignorados.
- dotnet publish Release: concluído em artifacts/publish/LumisApi.
- web.config gerado e validado: AspNetCoreModuleV2, recepcaototem.dll, InProcess, stdout desativado.
- Nenhum arquivo ClientApp, Pages, .env, certificado privado ou secrets no pacote publicado.
- Pacote publicado iniciado localmente em Production: /health HTTP 200 Healthy; /health/ready sem conexão HTTP 503 Unhealthy, sem detalhes internos.
- SQL idempotente gerado em artifacts/migrations.sql, sem aplicar ao banco.
- Migration antiga Identity preservada; nova migration adiciona AuditEntries e índice, sem alterar as chaves Identity.
- Revisão independente identificou isolamento de rate limit; corrigido e validado: liveness isento, readiness e API em partições separadas por origem.

## O que os testes cobrem

Liveness sem SQL; readiness saudável com probe controlado e indisponível sem configuração; acesso anônimo negado sem redirecionamento; OpenAPI não público em Production; CORS desconhecido negado; HTTP de infraestrutura separado de HTTPS obrigatório nas demais rotas; HTTP 429 sem bloquear liveness; políticas dos perfis; erros sem exception message/stack; modelo EF SQL Server com tamanhos das chaves preservados e tabela de auditoria.

O probe controlado não é uma conexão real ao SQL Server. Nenhuma migration foi aplicada a um SQL local ou de produção nesta etapa.

## Pendência externa

Tentativa GET http://10.255.36.167:8082/health expirou. IIS real e conexão pelo processo da conta Windows no servidor ainda não foram validados. Não foram alteradas configurações administrativas, permissões, firewall ou estrutura do banco.

A implantação exige acesso ao servidor e aprovação para as mudanças detalhadas em iis-foundation.md. Não declarar a infraestrutura de produção validada antes de obter /health e /health/ready Healthy no servidor.

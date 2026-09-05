# Lumis — backend de infraestrutura

## Estado desta entrega

Escopo atual: fundação .NET 10 e primeira funcionalidade real de autenticação/provisionamento. O React é servido pela própria API na mesma origem e os módulos de negócio continuam fora desta etapa.

- Domain: nomes dos perfis e entidade mínima de auditoria.
- Application: contratos de diagnóstico de banco e gravação de auditoria.
- Infrastructure: Identity, EF Core SQL Server, DbContext e migrations.
- API: login/logout/sessão por cookie seguro, antiforgery, lockout, dois limites cumulativos do login, policies, auditoria e criação administrativa de usuários.
- CLI separada cria os roles e o primeiro administrador. Nada é criado automaticamente no startup.

## Continuar em casa

```powershell
git clone https://github.com/1ryann/recepcaolumis.git
cd recepcaolumis
dotnet --version
dotnet tool restore
dotnet restore recepcaototem.sln
dotnet build recepcaototem.sln
dotnet test recepcaototem.sln
```

Instalar SDK 10.0.400 ou patch compatível com global.json. GitHub exige acesso ao repositório privado. Se já possui o clone, usar `git pull --ff-only` na branch informada no relatório de entrega.

Para iniciar sem banco e validar apenas a API:

```powershell
dotnet run --project recepcaototem --launch-profile http
Invoke-RestMethod http://localhost:5218/health
```

`/health` responde Healthy mesmo sem SQL. `/health/ready` retorna HTTP 503/Unhealthy sem configuração ou conexão: não é indicação de problema no processo HTTP.

Para desenvolver com banco, configurar `ConnectionStrings:DefaultConnection` pelo User Secrets do projeto web ou variável de ambiente. Usar banco local exclusivo de desenvolvimento, nunca o banco de produção. Não gravar o valor no README ou appsettings versionado. O backend não carrega arquivos .env automaticamente.

Documentação OpenAPI em `/openapi/v1.json`, somente em Development. Não há Swagger UI nem cadastro público. A sessão web usa cookie `__Host-Lumis.Auth`; JWT/localStorage não são usados.

## Documentos de referência

1. [Entrega de infraestrutura e IIS](docs/operations/iis-foundation.md) — instruções atuais e fronteira de aprovação.
2. [Nomes das configurações](docs/operations/configuration.md).
3. [Operação futura da autenticação](docs/operations/authentication-deployment.md).
3. [Escopo congelado do MVP](docs/superpowers/specs/2026-09-04-mvp-congelado.md).
4. [Estrutura/modelagem futura](docs/superpowers/specs/2026-09-04-backend-foundation-design.md).
5. [Plano do MVP completo](docs/superpowers/plans/2026-09-04-mvp-10-dias.md).
6. [Diretrizes originais](docs/superpowers/specs/2026-09-04-diretrizes-originais.md) — a menção original a PostgreSQL foi substituída expressamente por SQL Server no escopo aprovado.

Os documentos históricos descrevem também trabalho futuro. As restrições desta entrega prevalecem: somente fundação, sem publicar/alterar IIS ou banco de produção sem aprovação.

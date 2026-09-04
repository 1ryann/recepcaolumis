# Configurações externas

Nenhum valor de conexão de produção é versionado. Os nomes abaixo são suficientes para preparar o ambiente; valores reais são inseridos externamente pelo operador.

| Nome da variável | Uso |
|---|---|
| ASPNETCORE_ENVIRONMENT | Development local; Production no IIS |
| ConnectionStrings__DefaultConnection | SQL Server; produção exige Integrated Security/Trusted Connection e rejeita usuário/senha SQL |
| Security__DataProtectionPath | Diretório privado e persistente das chaves; obrigatório em Production; fora da publicação |
| AllowedHosts | Hosts explícitos aceitos, separados por ponto e vírgula; incluir localhost para verificação local |
| Cors__AllowedOrigins__0 | Primeira origem web explicitamente autorizada; omitir para negar acesso cross-origin |
| Cors__AllowedOrigins__1 | Outra origem autorizada, somente se necessária |
| RateLimiting__PermitLimit | Limite por origem/categoria; liveness isento |
| RateLimiting__WindowSeconds | Janela técnica do limite |

User Secrets em desenvolvimento usa `ConnectionStrings:DefaultConnection`. Valores de configuração IIS podem ficar no application pool, fora do checkout/publicação, protegidos pela administração do servidor. Não colocar a senha da conta Windows em nenhum desses arquivos: a conta é configurada no pool pela administração.

Windows Authentication de banco é da identidade do processo; usuários da API serão autenticados pelo Identity. Nunca habilitar autenticação Windows da aplicação web apenas para viabilizar acesso ao SQL.

Produção segue Trusted Connection conforme instrução do usuário. O usuário autorizou considerar TrustServerCertificate=True para a instância interna; isso é uma exceção de validação do certificado SQL, não desativa HTTPS da API. Configurar o valor externamente. Preferir certificado validável quando disponível. O código não insere nem modifica essa opção automaticamente.

Somente /health e /health/ready funcionam sobre HTTP em Production, para o binding interno solicitado. Demais rotas exigem HTTPS. Portas internas e SQL não devem ser publicados na internet. Não habilitar Swagger/OpenAPI em Production.

Data Protection usa DPAPI no Windows com identidade de usuário e diretório configurado. O perfil da conta do pool deve estar carregado para proteção por usuário; validar antes de modificar IIS. Chaves não ficam em Git/pasta pública e devem fazer parte do planejamento de recuperação. A API não cria nem amplia ACLs do servidor.

Logging técnico em JSON, sem bodies, tokens ou exception messages. EF logging suprimido para evitar exposição de detalhes SQL nesta fundação. A auditoria persistente é uma base para futuros casos de uso, não uma afirmação de auditoria funcional de módulos ainda inexistentes.

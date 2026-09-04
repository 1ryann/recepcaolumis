NÃO criar uma nova identidade visual.
NÃO substituir o layout atual.
NÃO transformar o sistema em um template administrativo diferente.

Utilize o design existente como base.

As alterações visuais serão feitas somente conforme eu solicitar durante
o desenvolvimento.

Você pode corrigir automaticamente apenas problemas evidentes como:

- espaçamentos inconsistentes;
- responsividade;
- alinhamentos;
- acessibilidade;
- contraste;
- estados de hover/focus;
- componentes quebrados;
- experiência em touchscreen;
- consistência entre telas.

Antes de fazer mudanças visuais significativas, apresente a proposta e
aguarde minha aprovação.

==================================================
PRIORIDADE MÁXIMA: SEGURANÇA
==================================================

A segurança deve ser tratada como requisito principal do sistema,
principalmente porque teremos:

- área administrativa;
- informações de locatários;
- dados de visitantes;
- fotografias;
- contratos;
- informações financeiras;
- controle de acesso físico;
- futura integração bancária;
- futura integração fiscal.

Não tratar segurança como etapa posterior.

Projetar o sistema seguindo o princípio:

SECURITY BY DESIGN

Toda funcionalidade nova deve considerar autenticação, autorização,
validação, auditoria e exposição mínima de dados.

==================================================
AUTENTICAÇÃO
==================================================

Utilizar ASP.NET Core Identity ou arquitetura equivalente robusta.

Para aplicações web administrativas, preferir autenticação segura com
cookie:

- HttpOnly;
- Secure;
- SameSite adequado;
- expiração;
- renovação segura de sessão.

NÃO armazenar tokens sensíveis em localStorage.

Implementar:

- hash seguro de senha;
- política mínima de senha;
- bloqueio temporário após diversas tentativas incorretas;
- proteção contra brute force;
- expiração de sessão;
- logout seguro;
- invalidação de sessões quando necessário.

Preparar arquitetura para MFA futuramente.

==================================================
AUTORIZAÇÃO
==================================================

Implementar autorização no BACKEND.

Nunca confiar apenas na interface.

Perfis:

ADMINISTRADOR
GERENTE
PROFISSIONAL

Exemplo:

Um profissional não poderá obter dados de outro profissional simplesmente
alterando um ID na URL ou enviando uma request manual.

Toda consulta deverá verificar:

USUÁRIO AUTENTICADO
+
PERMISSÃO
+
RECURSO SOLICITADO

Implementar proteção contra IDOR/BOLA.

==================================================
PROTEÇÃO DA ÁREA ADMINISTRATIVA
==================================================

Todas as rotas administrativas devem exigir autenticação e autorização.

Exemplo:

/api/admin/*
/api/locacoes/*
/api/pagamentos/*
/api/configuracoes/*

Nunca expor dados administrativos sem autorização.

Mesmo que alguém descubra os endpoints ou tente chamar a API diretamente
via Postman, curl, DevTools ou scripts, o backend deverá negar o acesso.

Não considerar ocultar botão ou página como mecanismo de segurança.

==================================================
RATE LIMITING
==================================================

Implementar rate limiting no backend.

Criar políticas diferentes para:

LOGIN
API pública
TOTEM
ÁREA ADMINISTRATIVA
CONTROLE DE ACESSO

Exemplos de proteção:

- limitar tentativas de login;
- limitar criação de visitas;
- impedir spam no envio de WhatsApp;
- impedir milhares de requests sequenciais;
- limitar tentativas de abertura da porta.

Quando ultrapassar o limite:

HTTP 429 Too Many Requests.

Registrar tentativas abusivas relevantes na auditoria.

==================================================
VALIDAÇÃO DE REQUESTS
==================================================

Nunca confiar em dados recebidos do frontend.

Validar no backend:

- tipos;
- tamanhos;
- campos obrigatórios;
- formatos;
- IDs;
- enums;
- datas;
- valores;
- arquivos;
- permissões.

Utilizar FluentValidation ou abordagem equivalente.

Rejeitar propriedades inesperadas quando apropriado.

Não permitir Mass Assignment / Overposting.

Utilizar DTOs específicos para entrada e saída.

Nunca receber diretamente entidades do banco nos controllers.

==================================================
BANCO DE DADOS
==================================================

Utilizar Entity Framework Core e consultas parametrizadas.

Nunca montar SQL utilizando concatenação de dados enviados pelo usuário.

Proteger contra SQL Injection.

Criar migrations controladas.

Não expor mensagens internas do banco para o frontend.

==================================================
PROTEÇÃO CONTRA XSS
==================================================

Não renderizar HTML fornecido pelo usuário sem sanitização.

Evitar dangerouslySetInnerHTML.

Quando conteúdo HTML for realmente necessário:

sanitizar antes da renderização.

Configurar Content Security Policy sempre que possível.

==================================================
CSRF
==================================================

Como a autenticação utilizará cookies, implementar proteção contra CSRF
nas operações que alteram dados.

Exemplos:

POST
PUT
PATCH
DELETE

Utilizar mecanismos anti-forgery adequados ao ASP.NET Core + SPA.

==================================================
CORS
==================================================

Não utilizar:

AllowAnyOrigin()

em produção.

Permitir somente os domínios oficiais do frontend.

Separar configuração:

Development
Staging
Production

==================================================
SECURITY HEADERS
==================================================

Configurar headers apropriados, incluindo quando aplicável:

Content-Security-Policy
X-Content-Type-Options
Referrer-Policy
Permissions-Policy
Strict-Transport-Security

Forçar HTTPS em produção.

==================================================
UPLOAD DE ARQUIVOS
==================================================

O sistema poderá receber:

- fotos;
- contratos;
- documentos.

Implementar validação rígida.

Validar:

- extensão;
- MIME real;
- tamanho máximo;
- tipo permitido.

Gerar nomes internos aleatórios.

Não utilizar diretamente o nome original do arquivo.

Nunca permitir execução de arquivos enviados.

Não armazenar uploads executáveis dentro de diretórios públicos do
servidor.

Preparar armazenamento privado com acesso autorizado.

==================================================
CONTROLE DA FECHADURA
==================================================

A abertura da porta é uma operação sensível.

NUNCA permitir:

frontend → fechadura

O fluxo obrigatório será:

FRONTEND
→ BACKEND AUTENTICADO
→ AUTORIZAÇÃO
→ AUDITORIA
→ SERVIÇO DE CONTROLE DE ACESSO
→ CONTROLADORA

O backend deverá verificar:

- usuário autenticado;
- perfil permitido;
- profissional relacionado;
- visitante relacionado quando aplicável;
- rate limit;
- estado da operação.

Registrar:

- usuário;
- visitante;
- data;
- horário;
- resultado;
- IP;
- dispositivo quando disponível.

Adicionar confirmação antes da abertura.

Não permitir chamadas repetidas de abertura em sequência.

==================================================
SEGREDOS E CREDENCIAIS
==================================================

Nunca colocar no código:

- senha de banco;
- token Meta;
- token WhatsApp;
- chave bancária;
- segredo JWT;
- credenciais Intelbras;
- connection string de produção.

Utilizar:

Environment Variables
ou
Secret Manager

Criar .env.example apenas com nomes das variáveis.

Nunca commitar .env real.

==================================================
LOGS
==================================================

Utilizar logs estruturados.

Exemplo:

Serilog.

Não registrar:

- senhas;
- tokens;
- cookies;
- documentos completos;
- fotos;
- dados financeiros sensíveis.

Separar:

LOG TÉCNICO
de
AUDITORIA DE NEGÓCIO.

==================================================
AUDITORIA
==================================================

Criar AuditLog para ações importantes.

Registrar:

- usuário;
- ação;
- entidade;
- ID da entidade;
- data/hora;
- resultado;
- IP;
- informações necessárias para investigação.

Exemplos:

LOGIN_SUCCESS
LOGIN_FAILED
USER_CREATED
PROFESSIONAL_UPDATED
LEASE_CREATED
PAYMENT_CHANGED
DOOR_OPEN_REQUESTED
DOOR_OPENED
DOOR_OPEN_FAILED

Não permitir que usuários comuns apaguem logs de auditoria.

==================================================
TRATAMENTO DE ERROS
==================================================

Criar middleware global de exceções.

Em produção:

NUNCA retornar stack trace.

Não retornar:

SQL;
connection string;
caminhos internos;
nomes de servidores;
detalhes de implementação.

Retornar respostas padronizadas.

Exemplo:

{
    "code": "RESOURCE_NOT_FOUND",
    "message": "Registro não encontrado."
}

Manter detalhes técnicos somente nos logs internos.

Banco de dados:

Utilizar PostgreSQL com Entity Framework Core.

Regras obrigatórias:

- não expor banco diretamente para internet;
- banco deve aceitar conexão apenas do backend;
- usuário do banco com privilégio mínimo necessário;
- não utilizar usuário superadmin na aplicação;
- connection string somente por variável de ambiente;
- SSL/TLS quando aplicável;
- backups automáticos;
- migrations versionadas;
- índices nos campos de busca;
- constraints e foreign keys;
- soft delete quando fizer sentido;
- auditoria para alterações importantes;
- nunca salvar senha em texto puro;
- nunca salvar tokens ou secrets sem proteção;
- não guardar fotos diretamente no banco se puder usar armazenamento de arquivos/objetos;
- banco deve armazenar apenas a referência/caminho seguro da foto.

==================================================
PROTEÇÃO CONTRA ENUMERAÇÃO
==================================================

Evitar respostas que permitam descobrir usuários cadastrados.

No login, por exemplo, não retornar:

"Usuário existe, mas a senha está errada."

Utilizar resposta genérica:

"Usuário ou senha inválidos."

==================================================
LGPD — PRIVACY BY DESIGN
==================================================

O sistema deverá ser desenvolvido seguindo princípios de PRIVACY BY DESIGN.

Aplicar:

- finalidade;
- adequação;
- necessidade;
- minimização;
- segurança;
- prevenção;
- transparência;
- responsabilização.

Coletar somente os dados necessários para funcionamento do sistema.

==================================================
DADOS DE VISITANTES
==================================================

O sistema poderá armazenar:

- nome;
- foto;
- horário;
- profissional visitado;
- sala;
- informações relacionadas à visita.

Não adicionar reconhecimento facial.

A fotografia será utilizada apenas como identificação visual do visitante
no fluxo de atendimento.

Na tela de captura apresentar um aviso curto de privacidade informando
que a fotografia será utilizada para identificação durante o atendimento.

Exemplo:

"Sua foto será utilizada para identificação durante esta visita e tratada
conforme a política de privacidade do estabelecimento."

Não definir juridicamente a base legal no código.

Deixar os textos e políticas configuráveis para que o responsável pelo
tratamento possa adequá-los conforme orientação jurídica.

==================================================
RETENÇÃO DE DADOS
==================================================

Não manter fotografias indefinidamente sem necessidade.

Criar configuração administrativa:

Retenção de fotografias de visitantes

Exemplo:

7 dias
15 dias
30 dias
90 dias
Personalizado

Preparar processo para remoção automática após o período configurado.

Histórico poderá permanecer sem a fotografia quando necessário.

==================================================
DIREITOS DO TITULAR
==================================================

Preparar ferramentas administrativas que permitam localizar dados por
titular quando necessário.

Criar estrutura para:

- localizar;
- consultar;
- corrigir;
- exportar;
- anonimizar;
- excluir.

A execução deverá respeitar permissões administrativas.

Registrar operações de exclusão/anonymização na auditoria.

==================================================
MINIMIZAÇÃO
==================================================

Não solicitar CPF, RG, endereço, data de nascimento ou outros dados do
visitante se eles não forem necessários para o atendimento.

Inicialmente utilizar somente:

Nome
Foto
Data/hora
Profissional
Sala

Adicionar novos campos somente quando houver justificativa funcional.

==================================================
POLÍTICA DE PRIVACIDADE
==================================================

Criar área:

Configurações
→ Privacidade e LGPD

Permitir configurar:

- texto de privacidade;
- responsável pelo tratamento;
- contato;
- prazo de retenção;
- política de fotografias;
- política de visitantes.

Criar também uma visualização acessível pelo totem.

==================================================
DADOS SENSÍVEIS NO FRONTEND
==================================================

Retornar somente os campos necessários para cada tela.

Exemplo:

O totem NÃO precisa receber:

- valor de aluguel;
- informações financeiras;
- contratos;
- configurações administrativas.

Criar DTOs diferentes para:

Totem
Profissional
Gerente
Administrador

Seguir o princípio de menor privilégio.

==================================================
CRIPTOGRAFIA
==================================================

Utilizar HTTPS/TLS para comunicação.

Senhas devem ser armazenadas exclusivamente através de hash robusto.

Dados altamente sensíveis ou credenciais externas deverão ser
criptografados/protegidos conforme necessidade.

Nunca criar criptografia própria.

Utilizar bibliotecas e mecanismos estabelecidos pela plataforma.

==================================================
BACKUP
==================================================

Preparar estratégia de backup do banco.

Backups não poderão ficar publicamente acessíveis.

Credenciais, arquivos e dados pessoais presentes em backups também devem
receber proteção adequada.

==================================================
PROTEÇÃO DE INFRAESTRUTURA
==================================================

Preparar produção para operar atrás de reverse proxy.

Exemplo:

Cloudflare
+
Nginx/Caddy
+
ASP.NET Core

Quando utilizado Cloudflare ou equivalente, preparar para:

- WAF;
- proteção contra bots;
- rate limiting adicional;
- bloqueio de padrões suspeitos;
- proteção DDoS.

Não depender exclusivamente do frontend ou Cloudflare.

O backend deverá possuir sua própria segurança.

==================================================
DEPENDÊNCIAS
==================================================

Utilizar dependências mantidas e atualizadas.

Evitar bibliotecas desnecessárias.

Executar auditoria de dependências.

Corrigir vulnerabilidades críticas e altas antes da produção.

==================================================
AMBIENTES
==================================================

Separar:

Development
Staging
Production

Nunca utilizar banco de produção em desenvolvimento.

Nunca utilizar credenciais de produção no ambiente local.

==================================================
TESTES DE SEGURANÇA
==================================================

Antes de considerar o sistema pronto, testar pelo menos:

1. acesso administrativo sem login;
2. acesso com perfil incorreto;
3. alteração manual de IDs;
4. brute force no login;
5. rate limiting;
6. requests inválidas;
7. SQL Injection;
8. XSS;
9. CSRF;
10. upload de arquivo inválido;
11. acesso direto a arquivos privados;
12. abertura de porta sem autorização;
13. repetição de request de abertura;
14. enumeração de usuários;
15. exposição de stack trace;
16. exposição de secrets;
17. CORS;
18. sessão expirada;
19. alteração de dados de outro profissional;
20. acesso a endpoints administrativos via request manual.

==================================================
REGRA DE DESENVOLVIMENTO
==================================================

SEGURANÇA > VELOCIDADE > FUNCIONALIDADES SECUNDÁRIAS

Não remover verificações de segurança para facilitar o desenvolvimento.

Caso uma funcionalidade conflite com os requisitos de segurança,
apresente o problema antes de implementar uma solução insegura.

==================================================
INTEGRAÇÕES FUTURAS
==================================================

Continuar preparando a arquitetura para:

- boleto;
- PIX;
- integração bancária;
- emissão fiscal/NFS-e;
- assinatura digital;
- sistema contábil.

Porém, continuar NÃO deixando essas integrações funcionais nesta etapa.

Elas devem aparecer como:

"Não configurado"

e utilizar abstrações/interfaces internas para implementação futura.

==================================================
ANTES DE IMPLEMENTAR
==================================================

Primeiro:

1. analise o projeto existente;
2. identifique a arquitetura atual;
3. identifique o design system existente;
4. identifique riscos de segurança;
5. identifique problemas de LGPD;
6. apresente um plano das alterações;
7. liste arquivos/módulos que serão alterados;
8. não altere o design significativamente;
9. não implemente integrações externas futuras;
10. aguarde minha aprovação antes de começar mudanças estruturais.
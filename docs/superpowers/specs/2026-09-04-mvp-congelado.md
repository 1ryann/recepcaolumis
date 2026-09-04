# MVP de 10 dias — escopo congelado

Fonte: resumo consolidado aprovado na conversa, suas sete correções e confirmação final. Este registro não autoriza funcionalidades adicionais. Entrega completa prevista para os 20 dias seguintes. Segurança e privacidade acompanham a primeira etapa.

## Requisitos por módulo

| ID | Compromisso aprovado |
|---|---|
| R01 | Preservar identidade, layout, componentes e experiência touch existentes; mudanças visuais significativas exigem aprovação. |
| R02 | Totem: profissional com foto/nome/profissão/sala, nome do visitante, webcam, confirmação, sucesso e retorno automático. Disponível de 1h antes até o término da ocorrência. Aviso de encerrado por mais 30min sem novas chegadas. |
| R03 | Profissionais: cadastro, edição, ativação/desativação e WhatsApp. Locatário pode ser pessoa/empresa diferente do ocupante e ter várias locações. Sala e profissional não podem ter períodos sobrepostos; podem ter vínculos em períodos distintos. |
| R04 | Salas: cadastro, tarifas por hora/dia, agenda e disponibilidade por período. |
| R05 | Locações por mês/dia/hora; início imediato/agendado. Agendada válida ativa automaticamente na data, sem nova aprovação; conferência gerencial não bloqueia. Adiamento preserva cobrança e reserva conforme disponibilidade. Separar início financeiro e ocupação. |
| R06 | Mensal recorrente com início obrigatório e término opcional. Geração por janela futura controlada, nunca infinita. Conflitos devem considerar regras além da janela materializada. |
| R07 | Nova reserva, reagendamento e cancelamento solicitados pelo profissional com mínimo de 1h antes do início desejado/original, conforme operação. Nova reserva depende de disponibilidade. Todas dependem de aprovação Gerente/Admin; PENDENTE/APROVADA/RECUSADA, resposta datada, recusa com justificativa obrigatória. |
| R08 | Reagendar apenas ocorrência selecionada na mesma sala, revalidando sala/profissional ao aprovar. Reserva original permanece até aprovação. Cancelamento libera período, mantém valor, histórico CANCELADA e aprovador. |
| R09 | Reagendamento/cancelamento bloqueados se houver visita AGUARDANDO/EM ATENDIMENTO na ocorrência. Encerrar locação manualmente bloqueado se houver essas visitas em qualquer ocorrência. |
| R10 | Visitas: chegada AGUARDANDO; iniciar EM ATENDIMENTO; concluir ENCERRADA; cancelar AGUARDANDO/EM ATENDIMENTO. Não encerrar diretamente de AGUARDANDO. Profissional atua nas próprias; gestão em qualquer. Correção excepcional gerencial exige justificativa, autor, data/hora e estados anterior/novo. |
| R11 | Encerramento de locação preserva passado e cancela futuras próprias; não afeta outras locações. Data final com visitas abertas gera ENCERRAMENTO PENDENTE, bloqueia novas ocorrências/chegadas, mantém visitas; última resolução conclui automaticamente. Não gerar após data final. |
| R12 | Visitas sobrevivem ao fim da reserva. Alertas em tempo real só Gerente/Admin: aguardando fora do horário/atendimento excedido; informativo sem próxima próxima de iniciar, RISCO DE CONFLITO a 1h da próxima, CONFLITO ATIVO no início. Contador, sala, profissional, visitante, término previsto, tempo excedido e próxima reserva. Nunca deslocar reservas automaticamente. |
| R13 | Liberar acesso separado do atendimento; profissional para próprias visitas, Gerente/Admin para qualquer. Confirmação, autenticação, autorização, auditoria, limite e proteção de repetição. MVP simula e não afirma abertura física. Registrar autor, tempo, visitante, profissional, resultado e porta/dispositivo quando aplicável. Atendimento pode iniciar sem liberação. |
| R14 | Financeiro: valor/vencimento/pago-pendente-atrasado por período, separado do status da locação. Hora proporcional aos minutos; diária por data no expediente, não 24h. Gestão ajusta valor final antes de salvar. Preservar tarifa, quantidade, calculado/final e autor. Mensal informado na locação. Adiamento/cancelamento sem abatimento. Profissional consulta valor/vencimento/status próprios, sem editar. |
| R15 | Admin total: usuários/permissões/configurações/integrações; Gerente gestão operacional, incluindo expediente/bloqueios e privacidade operacional; Profissional somente recursos próprios e solicitações. Admin também respeita regras, justificativas e auditoria. |
| R16 | SQL Server + EF Core, Identity ou equivalente, cookie HttpOnly/Secure/SameSite, sessão/lockout/rate limit, autorização por recurso, DTOs/validação, SQL parametrizado, CSRF, CORS restrito, HTTPS/headers, erros sem detalhes, segredos externos, logs mínimos e auditoria protegida. Ambientes separados, backup privado e restauração verificada, testes essenciais antes de produção. |
| R17 | Nome/foto/data/hora/profissional/sala; sem reconhecimento facial. Aviso pré-foto, política acessível/textos configuráveis; fotos privadas, retenção configurável com remoção; estrutura de exclusão/anonimização e auditoria. Sem dados pessoais sensíveis desnecessários ou definição jurídica de base legal no código. |
| R18 | Expediente global por dia da semana. Bloqueio por sala não pode sobrepor reserva válida; listar conflitos. Mudança de expediente não pode invalidar reservas/recorrências; listar sala/profissional/data/horários e manter configuração anterior até resolver. |
| R19 | WhatsApp nome/foto: real se Meta/credenciais disponíveis, senão teste explicitamente simulado; nunca afirmar entrega sem evidência. Não condiciona aceite. Admin controla integração; nenhum alerta externo gerencial no MVP. |

## Fora desta entrega

Financeiro avançado, crédito/estorno/abatimento/isenção automática, alteração desta e próximas ocorrências, troca de sala, expediente por sala, antecedência configurável, notificações externas gerenciais, MFA funcional, dashboards/relatórios avançados. Fechadura física e WhatsApp definitivo dependem de terceiros. Banco/boleto/PIX/NFS-e/assinatura/contabilidade apenas interfaces e “Não configurado”, sem compromisso operacional até dia 30. Reconhecimento facial excluído.

## Pontos não definidos — não criar política implicitamente

Destino contratual e responsável financeiro de uma nova reserva solicitada; efeito de solicitações pendentes sobre disponibilidade; aprovação após início pretendido; horários de chegada que se sobrepõem para o mesmo profissional; falha da câmera; cobrança futura ao encerrar locação inteira e competência/vencimento de novas cobranças; calendário de fechamento/feriados e fuso real do prédio. Perguntar quando necessário, sem bloquear trabalhos independentes.

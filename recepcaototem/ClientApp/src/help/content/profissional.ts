import type { Trilha } from './types'

export const trilhaProfissional: Trilha = {
  id: 'profissional',
  titulo: 'Para profissionais',
  resumo: 'Como funcionam suas visitas, suas solicitações de agenda e suas locações.',
  publica: true,
  versao: 1,
  passos: [
    { id: 'prof-visitas-proprias', titulo: 'Você acompanha apenas as suas visitas', texto: 'Cada profissional vê e movimenta somente as visitas dos próprios atendimentos. A gestão pode atuar em qualquer visita, e toda correção excepcional registra autor, data e os estados anterior e novo.' },
    { id: 'prof-agenda-por-solicitacao', titulo: 'A agenda muda por solicitação', texto: 'Você não altera a agenda diretamente. Nova reserva, reagendamento e cancelamento são solicitações enviadas à gestão, que aprova ou recusa. A recusa sempre vem com justificativa.' },
    { id: 'prof-antecedencia', titulo: 'Uma hora de antecedência', texto: 'Nova reserva, reagendamento e cancelamento exigem pelo menos uma hora de antecedência. Nova reserva também depende de haver horário disponível.' },
    { id: 'prof-reserva-durante-analise', titulo: 'Sua reserva continua válida durante a análise', texto: 'Enquanto a solicitação está pendente, a reserva original segue valendo. A disponibilidade é verificada de novo no momento da aprovação.' },
    { id: 'prof-bloqueio-visita-aberta', titulo: 'Visita em andamento bloqueia mudanças', texto: 'Quando a ocorrência já tem visita aguardando ou em atendimento, reagendamento e cancelamento ficam bloqueados. Resolva a visita primeiro.' },
    { id: 'prof-financeiro-consulta', titulo: 'Financeiro é consulta', texto: 'Você consulta valor, vencimento e situação das suas locações, sem alterá-los. Pagamento em atraso não encerra a locação nem retira você do totem.' },
    { id: 'prof-liberacao-acesso', titulo: 'Liberação de acesso é registrada', texto: 'Você pode solicitar liberação de acesso nas suas próprias visitas. Toda ação é confirmada, limitada e registrada em auditoria.' },
  ],
}

import type { Trilha } from './types'

export const trilhaProfissional: Trilha = {
  id: 'profissional',
  titulo: 'Para profissionais',
  resumo: 'Sua área, suas visitas, suas solicitações de agenda e suas locações.',
  publica: true,
  versao: 1,
  passos: [
    { id: 'prof-entrar', titulo: 'Onde você entra', texto: 'Sua entrada é em /profissional/login, e depois de entrar o sistema leva você direto para a sua área. Quem ainda não tem cadastro pede acesso em /profissional/cadastro e acompanha a resposta na tela de aguardando aprovação.' },
    { id: 'prof-inicio', titulo: 'Início e agenda', texto: 'A tela inicial resume o seu dia. Agenda mostra os seus períodos e atendimentos; Reservas reúne as suas reservas e as solicitações que você enviou.' },
    { id: 'prof-disponibilidade', titulo: 'Disponibilidade', texto: 'Em Disponibilidade você informa quando aceita atender. Ela orienta o autoagendamento do cliente, e não substitui a locação: você só aparece nas janelas em que tem sala contratada.' },
    { id: 'prof-visitas-proprias', titulo: 'Você acompanha apenas as suas visitas', texto: 'Em Atendimentos ficam as visitas dos seus próprios atendimentos. A gestão pode atuar em qualquer visita, e toda correção excepcional registra autor, data e os estados anterior e novo.' },
    { id: 'prof-agenda-por-solicitacao', titulo: 'A agenda muda por solicitação', texto: 'Você não altera a agenda diretamente. Nova reserva, reagendamento e cancelamento são solicitações enviadas à gestão, que aprova ou recusa. A recusa sempre vem com justificativa.' },
    { id: 'prof-antecedencia', titulo: 'Uma hora de antecedência', texto: 'Nova reserva, reagendamento e cancelamento exigem pelo menos uma hora de antecedência. Nova reserva também depende de haver horário disponível.' },
    { id: 'prof-reserva-durante-analise', titulo: 'Sua reserva continua válida durante a análise', texto: 'Enquanto a solicitação está pendente, a reserva original segue valendo. A disponibilidade é verificada de novo no momento da aprovação.' },
    { id: 'prof-bloqueio-visita-aberta', titulo: 'Visita em andamento bloqueia mudanças', texto: 'Quando a ocorrência já tem visita aguardando ou em atendimento, reagendamento e cancelamento ficam bloqueados. Resolva a visita primeiro.' },
    { id: 'prof-locacoes-financeiro', titulo: 'Locações e financeiro são consulta', texto: 'Em Locações e Financeiro você consulta valor, vencimento e situação das suas locações, sem alterá-los. Pagamento em atraso não encerra a locação nem retira você do totem.' },
    { id: 'prof-perfil', titulo: 'Seu perfil', texto: 'Em Perfil ficam seus dados cadastrais, sua foto e a troca da sua senha. A foto é o que o visitante vê no totem ao procurar por você.' },
  ],
}

import type { Trilha } from './types'

export const trilhaAdmin: Trilha = {
  id: 'admin',
  titulo: 'Para a administração',
  resumo: 'Como o painel se organiza, o que cada área resolve e em que ordem usar.',
  publica: false,
  versao: 1,
  passos: [
    { id: 'admin-navegacao', alvo: 'nav-lateral', rota: '/admin', titulo: 'A navegação', texto: 'Por aqui você alcança todas as áreas do painel. Gestão reúne o dia a dia do edifício; Preferências guarda as configurações.' },
    { id: 'admin-salas', alvo: 'pagina-salas', rota: '/admin/salas', titulo: 'Salas e tarifas', texto: 'Cadastre os espaços do edifício com as tarifas por hora e por diária. Sem sala cadastrada não há o que reservar, então esta costuma ser a primeira tela a preencher.' },
    { id: 'admin-profissionais', alvo: 'pagina-profissionais', rota: '/admin/profissionais', titulo: 'Profissionais', texto: 'Dados cadastrais, foto e acesso das pessoas que atendem no LUMIS. É aqui que se vincula a conta que permite ao profissional entrar e ver os próprios atendimentos.' },
    { id: 'admin-clientes', alvo: 'pagina-clientes', rota: '/admin/clientes', titulo: 'Clientes', texto: 'Os cadastros nascem sozinhos, dos agendamentos e da área do cliente. Um cadastro desativado não agenda, não faz check-in e não recebe avisos.' },
    { id: 'admin-locacoes', alvo: 'pagina-locacoes', rota: '/admin/locacoes', titulo: 'Locações', texto: 'Contratos e períodos de ocupação das salas. A locação é o que autoriza um profissional a ocupar uma sala numa janela de tempo; a agenda decorre dela.' },
    { id: 'admin-configuracoes', alvo: 'pagina-configuracoes', rota: '/admin/configuracoes', titulo: 'Expediente e bloqueios', texto: 'O horário de funcionamento do estabelecimento e as indisponibilidades das salas. Reduzir o expediente não invalida reserva já existente: os conflitos aparecem para você resolver.' },
    { id: 'admin-reservas-visitas', titulo: 'Reservas e visitas', texto: 'Reservas mostra a agenda operacional; Visitas acompanha quem chegou, quem está em atendimento e quem já saiu. Correções excepcionais registram autor, data e os estados anterior e novo.' },
    { id: 'admin-recepcao', titulo: 'Monitor da recepção', texto: 'A tela de Recepção concentra o que está acontecendo agora: chegadas, atendimentos em curso e alertas de visitante fora do horário, risco de conflito e conflito ativo. O sistema nunca desloca reservas sozinho.' },
    { id: 'admin-solicitacoes', titulo: 'Solicitações e interesses', texto: 'Solicitações de Profissionais reúne quem pediu cadastro e aguarda aprovação. Interesses de Locação junta quem demonstrou interesse numa sala pelo catálogo público.' },
    { id: 'admin-desativar', titulo: 'Desativar em vez de excluir', texto: 'Salas, profissionais e clientes são desativados, nunca excluídos. O histórico de visitas, locações e cobranças depende desses registros continuarem existindo.' },
    { id: 'admin-onde-esta-ajuda', alvo: 'ajuda', titulo: 'Onde reencontrar este tutorial', texto: 'A qualquer momento, o botão de ajuda no rodapé da barra lateral abre a central de ajuda, onde você relê tudo isto e pode refazer o tour.' },
  ],
}

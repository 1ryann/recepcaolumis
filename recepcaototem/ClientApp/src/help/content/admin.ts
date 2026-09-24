import type { Trilha } from './types'

export const trilhaAdmin: Trilha = {
  id: 'admin',
  titulo: 'Para a administração',
  resumo: 'Onde ficam salas e profissionais, e o que já está disponível no sistema.',
  publica: false,
  versao: 1,
  passos: [
    { id: 'admin-navegacao', titulo: 'A navegação', texto: 'Por aqui você alcança as áreas do sistema. Gestão reúne o dia a dia do edifício; Preferências guarda as configurações.' },
    { id: 'admin-modulos-em-construcao', titulo: 'O que já está disponível', texto: 'Salas e Profissionais já operam com dados reais. Visão geral, Locações, Visitas e Configurações ainda estão em construção e mostram um aviso ao serem abertas.' },
    { id: 'admin-nova-sala', titulo: 'Cadastrar uma sala', texto: 'Cada sala tem nome, descrição e as tarifas por hora e por diária. As tarifas são informadas em reais.' },
    { id: 'admin-busca-salas', titulo: 'Encontrar uma sala', texto: 'A busca filtra por nome, e o seletor ao lado mostra apenas salas ativas ou inativas.' },
    { id: 'admin-novo-profissional', titulo: 'Cadastrar um profissional', texto: 'O cadastro reúne nome, profissão e WhatsApp. O número é normalizado pela API no padrão internacional.' },
    { id: 'admin-busca-profissionais', titulo: 'Encontrar um profissional', texto: 'A busca aceita nome ou profissão.' },
    { id: 'admin-filtro-status', titulo: 'Filtrar por situação', texto: 'O seletor separa profissionais ativos de inativos. Ativos são os que podem aparecer no totem.' },
    { id: 'admin-acoes-linha', titulo: 'As ações de cada profissional', texto: 'Na linha de cada pessoa você edita os dados cadastrais e administra a foto, guardada em armazenamento privado.' },
    { id: 'admin-vincular-conta', titulo: 'Vincular uma conta de acesso', texto: 'Somente administradores vinculam um profissional a uma conta do sistema. É esse vínculo que permite à pessoa entrar e ver os próprios atendimentos.', somenteRoles: ['ADMINISTRADOR'] },
    { id: 'admin-ativar-desativar', titulo: 'Desativar em vez de excluir', texto: 'Profissionais e salas são desativados, nunca excluídos. O histórico de visitas e locações depende desses registros continuarem existindo.' },
    { id: 'admin-paginacao', titulo: 'Listas longas', texto: 'Quando a lista passa do tamanho de uma página, a navegação aparece no rodapé do painel.' },
    { id: 'admin-onde-esta-ajuda', titulo: 'Onde reencontrar este tutorial', texto: 'A qualquer momento, o botão de ajuda no rodapé da barra lateral abre a central de ajuda, onde você pode reler tudo e refazer o tour.' },
  ],
}

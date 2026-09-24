import type { Trilha } from './types'

export const trilhaCliente: Trilha = {
  id: 'cliente',
  titulo: 'Para quem vai ser atendido',
  resumo: 'Como agendar, o que acontece na chegada e o que fazemos com os seus dados.',
  publica: true,
  versao: 1,
  passos: [
    { id: 'cliente-conta', titulo: 'Sua conta', texto: 'Você cria sua conta em /cliente/cadastro e entra em /cliente/login. Também é possível ser atendido sem conta: nesse caso a recepção registra seu agendamento na hora, pelo seu telefone.' },
    { id: 'cliente-agendar', titulo: 'Agendar um atendimento', texto: 'Na sua área, Agendar mostra os profissionais e os horários disponíveis. Um atendimento acontece sempre numa sala reservada, num horário definido.' },
    { id: 'cliente-agendamentos', titulo: 'Acompanhar seus agendamentos', texto: 'Agendamentos lista o que você marcou, com a situação de cada um. Abrindo um agendamento você vê os detalhes e o que pode fazer com ele.' },
    { id: 'cliente-reagendar', titulo: 'Reagendar pelo link', texto: 'Quando você recebe um link de reagendamento, ele abre direto o seu agendamento, sem precisar entrar na conta. O link é pessoal: trate-o como trataria uma senha.' },
    { id: 'cliente-chegada', titulo: 'Ao chegar no edifício', texto: 'Você informa sua chegada no totem da recepção e escolhe o profissional que vai atender. O profissional recebe o aviso e vem buscá-lo quando estiver pronto.' },
    { id: 'cliente-janela', titulo: 'Quando o totem aceita sua chegada', texto: 'O atendimento aparece no totem a partir de uma hora antes do horário marcado e até o fim dele. Depois disso o totem informa que o atendimento foi encerrado e não aceita nova chegada.' },
    { id: 'cliente-qr', titulo: 'Check-in por QR', texto: 'Quando você recebe um código QR do seu agendamento, ele identifica a sua chegada diretamente, sem precisar procurar seu nome na lista.' },
    { id: 'cliente-privacidade', titulo: 'Foto e privacidade', texto: 'A chegada pode incluir uma foto, sempre com aviso antes da captura. A imagem fica em armazenamento privado, é usada apenas para identificar a sua visita e segue a política de retenção do edifício.' },
  ],
}

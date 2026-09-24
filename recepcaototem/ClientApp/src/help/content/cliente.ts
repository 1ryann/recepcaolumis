import type { Trilha } from './types'

export const trilhaCliente: Trilha = {
  id: 'cliente',
  titulo: 'Para quem vai ser atendido',
  resumo: 'Como agendar, o que acontece na chegada e o que fazemos com os seus dados.',
  publica: true,
  versao: 1,
  passos: [
    { id: 'cliente-agendamento', titulo: 'O agendamento', texto: 'Um atendimento acontece em uma sala reservada, em um horário definido. O agendamento pode ser feito por você ou registrado na recepção no momento da chegada.' },
    { id: 'cliente-chegada', titulo: 'Ao chegar no edifício', texto: 'Você informa sua chegada no totem da recepção e escolhe o profissional que vai atender. O profissional recebe o aviso e vem buscá-lo quando estiver pronto.' },
    { id: 'cliente-janela', titulo: 'Quando o totem aceita sua chegada', texto: 'O atendimento aparece no totem a partir de uma hora antes do horário marcado e até o fim dele. Depois disso o totem informa que o atendimento foi encerrado e não aceita nova chegada.' },
    { id: 'cliente-qr', titulo: 'Check-in por QR', texto: 'Quando você recebe um código QR do seu agendamento, ele identifica a sua chegada diretamente, sem precisar procurar seu nome na lista.' },
    { id: 'cliente-privacidade', titulo: 'Foto e privacidade', texto: 'A chegada pode incluir uma foto, sempre com aviso antes da captura. A imagem fica em armazenamento privado, é usada apenas para identificar a sua visita e segue a política de retenção do edifício.' },
  ],
}

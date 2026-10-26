// O que a dona recebe e por onde (#2070), só leitura. Fonte: os avisos do Balcão no console
// (`dominio/aviso.js`) e o catálogo de notificações da API (`NotificacoesGlobaisSeed`): Push para o
// aparelho inscrito, In-App para o sino e e-mail para a conta. O sino também recebe o Push que não
// chegou ao aparelho (#1765-7). Modelos e rotinas não se editam aqui (ADR-0057 item 9).
export const CANAIS_DE_AVISO = {
  TELA: 'Na tela, com o console aberto',
  APARELHO: 'Neste aparelho, com o console fechado',
  SINO: 'No sino de Avisos',
  EMAIL: 'Por e-mail',
}

const { TELA, APARELHO, SINO, EMAIL } = CANAIS_DE_AVISO

export const AVISOS_DA_DONA = [
  { titulo: 'Conversa que precisa de você', quando: 'O atendimento automático passou a conversa para você.', canais: [TELA, APARELHO] },
  { titulo: 'Contato novo', quando: 'Um cliente escreveu pela primeira vez.', canais: [TELA, APARELHO, SINO] },
  { titulo: 'Mensagem nova de cliente', quando: 'Chegou mensagem numa conversa que já existe.', canais: [TELA] },
  { titulo: 'Pagamento confirmado', quando: 'O pagamento de um pedido entrou.', canais: [TELA, APARELHO, SINO] },
  { titulo: 'Pedido mudou de etapa', quando: 'Um pedido avançou na cozinha ou na entrega.', canais: [TELA, APARELHO, SINO] },
  { titulo: 'Lembrete na hora', quando: 'Chegou a hora de um lembrete seu ou da loja.', canais: [APARELHO] },
  { titulo: 'Loja fechada dentro do horário', quando: 'Alguém fechou a loja na mão no horário de funcionamento, com o motivo.', canais: [APARELHO, SINO] },
  { titulo: 'Link de pagamento vencido de novo', quando: 'O pedido espera a equipe: mande outro link, combine o pagamento ou cancele.', canais: [SINO] },
  { titulo: 'Pagamento sem confirmação ao cliente', quando: 'O pagamento entrou, mas o WhatsApp não respondeu ao avisar o cliente.', canais: [SINO] },
  { titulo: 'Pagamento em disputa no Mercado Pago', quando: 'O cliente abriu uma disputa; o pedido segue como está.', canais: [SINO] },
  { titulo: 'Caixa esquecido aberto', quando: 'O caixa ficou aberto de um dia para o outro.', canais: [SINO] },
  { titulo: 'Convite e troca de senha', quando: 'O link para entrar no EasyStok ou criar uma senha nova.', canais: [EMAIL] },
]

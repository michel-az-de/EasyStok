// O que a dona recebe e por onde (#2070), só leitura. Três fontes: os avisos do Balcão na tela
// (`tela`, de `dominio/aviso.js`), as rotinas do catálogo de notificações da API (`rotinas`, códigos
// do `NotificacoesGlobaisSeed`) e os alertas de estoque que o analisador põe no sino (sem rotina).
// A prova-2070-formularios confere os canais contra o seed e exige motivo para cada rotina ativa que
// fica fora daqui. Modelos e rotinas não se editam aqui (ADR-0057 item 9).
import { TIPOS_DE_AVISO } from './aviso.js'

export const CANAIS_DE_AVISO = {
  TELA: 'Na tela, com o console aberto',
  APARELHO: 'Neste aparelho, com o console fechado',
  SINO: 'No sino de Avisos',
  EMAIL: 'Por e-mail',
  WHATSAPP: 'Por WhatsApp',
}

const { TELA, APARELHO, SINO, EMAIL, WHATSAPP } = CANAIS_DE_AVISO

// Canal de entrega da rotina (API) como a dona o vê. O Push vai ao aparelho inscrito; o In-App, ao sino.
export const CANAL_DA_ROTINA = { Push: APARELHO, InApp: SINO, Email: EMAIL, WhatsApp: WHATSAPP }

const aviso = (titulo, quando, canais, { tela = null, rotinas = [] } = {}) => ({ titulo, quando, canais, tela, rotinas })

export const AVISOS_DA_DONA = [
  aviso('Conversa que precisa de você', 'O atendimento automático passou a conversa para você.', [TELA, APARELHO],
    { tela: TIPOS_DE_AVISO.PASSOU_PARA_VOCE, rotinas: ['conversa_escalada_global'] }),
  aviso('Contato novo', 'Um cliente escreveu pela primeira vez.', [TELA, APARELHO, SINO],
    { tela: TIPOS_DE_AVISO.CONTATO_NOVO, rotinas: ['contato_novo_balcao_global'] }),
  aviso('Mensagem nova de cliente', 'Chegou mensagem numa conversa que já existe.', [TELA], { tela: TIPOS_DE_AVISO.MENSAGEM_NOVA }),
  aviso('Pagamento confirmado', 'O pagamento de um pedido entrou.', [TELA, APARELHO, SINO],
    { tela: TIPOS_DE_AVISO.PAGAMENTO_CONFIRMADO, rotinas: ['pedido_pago_balcao_global'] }),
  aviso('Pedido mudou de etapa', 'Um pedido avançou na cozinha ou na entrega.', [TELA, APARELHO, SINO],
    { tela: TIPOS_DE_AVISO.ETAPA_DO_PEDIDO, rotinas: ['etapa_pedido_balcao_global'] }),
  aviso('Pedido agendado', 'No dia do pedido agendado, 1 hora antes e 10 minutos antes.', [SINO],
    { rotinas: ['pedido_agendado_hoje_global', 'pedido_agendado_1h_global', 'pedido_agendado_10min_global'] }),
  aviso('Lembrete na hora', 'Chegou a hora de um lembrete seu ou da loja.', [APARELHO], { rotinas: ['lembrete_vencido_global'] }),
  aviso('Loja fechada dentro do horário', 'Alguém fechou a loja na mão no horário de funcionamento, com o motivo.', [APARELHO, SINO],
    { rotinas: ['loja_fechada_no_horario_global'] }),
  aviso('Link de pagamento vencido de novo', 'O pedido espera a equipe: mande outro link, combine o pagamento ou cancele.', [SINO],
    { rotinas: ['cobranca_aguardando_equipe_global'] }),
  aviso('Pagamento sem confirmação ao cliente', 'O pagamento entrou, mas o WhatsApp não respondeu ao avisar o cliente.', [SINO],
    { rotinas: ['pagamento_sem_aviso_ao_cliente_global'] }),
  aviso('Pagamento em disputa no Mercado Pago', 'O cliente abriu uma disputa; o pedido segue como está.', [SINO],
    { rotinas: ['pagamento_em_mediacao_global'] }),
  aviso('Caixa esquecido aberto', 'O caixa ficou aberto de um dia para o outro. O sino avisa quem abriu; a gerência recebe por e-mail e WhatsApp.',
    [SINO, EMAIL, WHATSAPP], { rotinas: ['caixa_esquecido_aberto_global', 'prazo_estourado_global'] }),
  aviso('Alertas de estoque', 'Validade perto ou vencida, abaixo do mínimo, produto parado, reposição sugerida e pedido de compra atrasado ou recebido. Os limites ficam em Alertas de estoque.',
    [SINO]),
  aviso('Convite, senha e contato da conta', 'O link para entrar no EasyStok ou criar uma senha nova, o aviso de senha trocada e o de e-mail trocado ou a confirmar. Pelo WhatsApp quando a conta tem um número que pode receber.',
    [EMAIL, WHATSAPP], { rotinas: ['convite_acesso_global', 'reset_senha_global', 'senha_alterada_global', 'contato_alterado_global', 'confirmacao_email_global'] }),
]

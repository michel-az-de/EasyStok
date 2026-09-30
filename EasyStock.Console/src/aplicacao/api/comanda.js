import * as acao from '../acoes'
import {
  corpoDoPedido, formaDoMeio, gerarPedido, janelaDoId, listarJanelas, obterPedido, pedidoDaApi,
  trocarFormaPagamento,
} from '../../infra/api/comandaApi'

// Comanda e cobrança no modo API (F03, S10/S11). Montar a comanda (itens, observação,
// janela, meio) continua local até o envio; "Enviar ao cliente" cria o pedido no EasyStok,
// que cobra pelo Mercado Pago e manda o resumo com o link pela conversa. Depois disso o
// pedido é do EasyStok: a polling traz pago, expirado e o resto da esteira.
//
// O que a F03 não liga (confirmar à mão, estorno, cancelar, esteira, reenvio avulso) não
// mexe na memória do navegador com pedido já criado: avisa e deixa como está, para a tela
// nunca mostrar um estado que o EasyStok não tem.
const NAO_LIGADO = 'ainda não está ligado ao EasyStok (F04 em diante). Use o EasyStok para isso.'

const EDITA_COMANDA = ['adicionarItem', 'removerItem', 'ajustarQuantidade', 'ajustarObservacao', 'escolherJanela', 'forcarEncaixe']
const SO_NO_EASYSTOK = {
  confirmarPagamento: 'Marcar pago à mão',
  marcarComprovante: 'Comprovante',
  aceitarDivergencia: 'Aceitar diferença',
  desfazerPagamento: 'Desfazer pagamento',
  marcarEstorno: 'Estorno',
  cancelarPedido: 'Cancelar pedido',
  avancarEsteira: 'Avançar a esteira',
  corrigirPasso: 'Voltar a etapa',
  desfazerEsteira: 'Desfazer a etapa',
}

export function criarAcoesComandaApi(acoes, { despachar, estadoRef }) {
  const avisar = (mensagem) => despachar({ tipo: acao.AVISO_API, mensagem })
  const pedidoDe = (id) => estadoRef.current.conversas.find((c) => c.id === id)?.pedido ?? null
  const pedidoCriado = (id) => pedidoDe(id)?.pedidoId ?? null

  async function recarregar(id) {
    const pedido = pedidoDaApi(await obterPedido(id))
    if (pedido) despachar({ tipo: acao.SINCRONIZAR_PEDIDO, id, pedido })
  }

  const soSemPedidoCriado = (nome, rotulo) => (id, ...resto) => {
    if (!pedidoCriado(id)) return acoes[nome](id, ...resto)
    avisar(`${rotulo}: o pedido já está no EasyStok e ${rotulo === 'Comanda' ? 'não muda por aqui.' : NAO_LIGADO}`)
    return undefined
  }

  const trocarForma = (id, meio) => {
    const pedidoId = pedidoCriado(id)
    despachar({ tipo: acao.ESCOLHER_MEIO_PAGAMENTO, id, meio })
    return trocarFormaPagamento(pedidoId, formaDoMeio(meio))
      .then(() => recarregar(id))
      .catch((erro) => avisar(`Forma de pagamento: ${erro.message}`))
  }

  return {
    ...Object.fromEntries(EDITA_COMANDA.map((nome) => [nome, soSemPedidoCriado(nome, 'Comanda')])),
    ...Object.fromEntries(Object.entries(SO_NO_EASYSTOK).map(([nome, rotulo]) => [nome, soSemPedidoCriado(nome, rotulo)])),

    // Janelas com vaga no prazo dos itens da comanda (S16). Promessa direta para a tela.
    carregarJanelasComanda: (itens) => listarJanelas({ itens }),

    gerarPedido: (id, meio = null) => {
      const pedido = pedidoDe(id)
      if (!pedido || pedidoCriado(id)) return
      if (!janelaDoId(pedido.janela)) {
        avisar('Escolha a janela de entrega antes de enviar ao cliente.')
        return
      }
      const corpo = corpoDoPedido({ ...pedido, meio: meio ?? pedido.meio })
      gerarPedido(id, corpo)
        .then((gerado) => {
          if (!gerado.enviadoAoCliente) {
            avisar('Pedido criado no EasyStok, mas o resumo não saiu ao cliente (conversa fora da janela de 24 h ou canal fora do ar).')
          }
          return recarregar(id)
        })
        .catch((erro) => avisar(`Pedido não criado: ${erro.message}`))
    },

    // Com pedido criado, trocar o meio é a troca de forma da S11: online ↔ na entrega.
    alterarMeioPagamento: (id, meio) => (pedidoCriado(id) ? trocarForma(id, meio) : acoes.alterarMeioPagamento(id, meio)),

    // Gerar ou reenviar cobrança com pedido criado: só a troca de forma é ligada. O link
    // vencido é reemitido e enviado pelo próprio EasyStok (S11, job de expiração).
    gerarCobranca: (id, pedido, meio = null) => {
      if (!pedidoCriado(id)) return acoes.gerarCobranca(id, pedido, meio)
      const atual = pedidoDe(id)
      if (meio && formaDoMeio(meio) !== formaDoMeio(atual?.meio)) return trocarForma(id, meio)
      avisar('A cobrança já está no EasyStok. Link vencido é reemitido e enviado sozinho.')
      return undefined
    },
    reenviarCobranca: (id, pedido, valorForcado = null, meio = null) => {
      if (!pedidoCriado(id)) return acoes.reenviarCobranca(id, pedido, valorForcado, meio)
      const atual = pedidoDe(id)
      if (meio && formaDoMeio(meio) !== formaDoMeio(atual?.meio)) return trocarForma(id, meio)
      avisar('A cobrança já está no EasyStok. Link vencido é reemitido e enviado sozinho.')
      return undefined
    },
  }
}

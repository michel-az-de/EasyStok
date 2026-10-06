import * as acao from '../acoes'
import {
  FORMA_ONLINE, corpoDoPedido, formaDoMeio, gerarPedido, janelaDoId, listarJanelas, obterPedido, pedidoDaApi,
  reemitirCobranca, trocarFormaPagamento, registrarPagamentoManual, desfazerPagamentoManual,
} from '../../infra/api/comandaApi'

// Comanda e cobrança no modo API (F03, S10/S11). Montar a comanda (itens, observação,
// janela, meio) continua local até o envio; "Enviar ao cliente" cria o pedido no EasyStok,
// que cobra pelo Mercado Pago e manda o resumo com o link pela conversa. Depois disso o
// pedido é do EasyStok: a polling traz pago, expirado e o resto da esteira.
//
// O que ainda não está ligado (estorno, cancelar, esteira, reenvio avulso) não
// mexe na memória do navegador com pedido já criado: avisa e deixa como está, para a tela
// nunca mostrar um estado que o EasyStok não tem.
//
// Sem pedido criado (#1271) vale o mesmo: só a comanda é rascunho local. Gerar ou reenviar a
// cobrança cria o pedido (o EasyStok cobra e manda o resumo); pagamento e esteira avisam,
// porque um rascunho "Pago" ou "Em preparo" nunca chega à cozinha.
const NAO_LIGADO = 'ainda não está ligado ao EasyStok (F04 em diante). Use o EasyStok para isso.'
const SEM_PEDIDO = 'o pedido ainda não está no EasyStok. Gere a cobrança ou envie ao cliente primeiro.'

const EDITA_COMANDA = ['adicionarItem', 'removerItem', 'ajustarQuantidade', 'ajustarObservacao', 'escolherJanela', 'forcarEncaixe']
const SO_NO_EASYSTOK = {
  marcarComprovante: 'Comprovante',
  aceitarDivergencia: 'Aceitar diferença',
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

  // O meio escolhido na tela vai junto: a API devolve a forma, não o meio (F07, item 5).
  async function recarregar(id) {
    const pedido = pedidoDaApi(await obterPedido(id), pedidoDe(id))
    if (pedido) despachar({ tipo: acao.SINCRONIZAR_PEDIDO, id, pedido })
  }

  // Uma ida ao EasyStok por conversa (#1287): o segundo clique com a primeira em voo
  // recebe a mesma promessa, sem outro POST. O botão espera essa promessa para destravar.
  const emVoo = new Map()
  const umaPorConversa = (id, fazer) => {
    if (emVoo.has(id)) return emVoo.get(id)
    const promessa = fazer()
    if (!promessa) return promessa
    const final = promessa.finally(() => emVoo.delete(id))
    emVoo.set(id, final)
    return final
  }

  const soSemPedidoCriado = (nome) => (id, ...resto) => {
    if (!pedidoCriado(id)) return acoes[nome](id, ...resto)
    avisar('Comanda: o pedido já está no EasyStok e não muda por aqui.')
    return undefined
  }

  const soNoEasyStok = (rotulo) => (id) => {
    avisar(`${rotulo}: ${pedidoCriado(id) ? NAO_LIGADO : SEM_PEDIDO}`)
    return undefined
  }

  const trocarForma = (id, meio) => umaPorConversa(id, () => {
    const pedidoId = pedidoCriado(id)
    despachar({ tipo: acao.ESCOLHER_MEIO_PAGAMENTO, id, meio })
    // Falhou: o meio local já mudou, então volta ao que o EasyStok tem (F07, item 5).
    return trocarFormaPagamento(pedidoId, formaDoMeio(meio))
      .then(() => recarregar(id))
      .catch((erro) => {
        avisar(`Forma de pagamento: ${erro.message}`)
        return recarregar(id).catch(() => {})
      })
  })

  // "Enviar ao cliente", ou gerar a cobrança antes dele: o EasyStok cria o pedido, cobra e
  // manda o resumo pela conversa. Só o POST decide "não criado" (#1287): a recarga que
  // falha depois dele é só a tela atrasada, a polling traz o pedido.
  const criarPedido = (id, meio = null) => umaPorConversa(id, () => {
    const pedido = pedidoDe(id)
    if (!pedido || pedidoCriado(id)) return undefined
    if (!janelaDoId(pedido.janela)) {
      avisar('Escolha a janela de entrega antes de enviar ao cliente.')
      return undefined
    }
    const corpo = corpoDoPedido({ ...pedido, meio: meio ?? pedido.meio })
    return gerarPedido(id, corpo).then(
      (gerado) => {
        // Mercado Pago fora: o pedido nasce sem link e nada o emite sozinho (#1287).
        const faltas = []
        if (!gerado?.cobranca && corpo.forma === FORMA_ONLINE) {
          faltas.push('a cobrança não saiu (Mercado Pago fora do ar). Use "Gerar cobrança" para emitir o link')
        }
        if (!gerado?.enviadoAoCliente) {
          faltas.push('o resumo não saiu ao cliente (conversa fora da janela de 24 h ou canal fora do ar)')
        }
        const aviso = faltas.length > 0 ? `Pedido criado no EasyStok, mas ${faltas.join('; e ')}.` : null
        if (aviso) avisar(aviso)
        return recarregar(id).catch(() => avisar(aviso ? `${aviso} Atualizando a tela…` : 'Pedido criado no EasyStok, atualizando a tela…'))
      },
      (erro) => avisar(`Pedido não criado: ${erro.message}`),
    )
  })

  // Pedido criado sem link (Mercado Pago fora na hora): a operadora pede a emissão (S11).
  const reemitir = (id) => umaPorConversa(id, () => reemitirCobranca(pedidoCriado(id)).then(
    () => recarregar(id).catch(() => {}),
    (erro) => avisar(`Cobrança: ${erro.message}`),
  ))

  // Com pedido criado, gerar ou reenviar a cobrança troca a forma (S11) ou, sem cobrança
  // online, emite o link; o link vencido é reemitido e enviado pelo EasyStok (job de expiração).
  const cobrar = (id, meio) => {
    if (!pedidoCriado(id)) return criarPedido(id, meio)
    const atual = pedidoDe(id)
    if (meio && formaDoMeio(meio) !== formaDoMeio(atual?.meio)) return trocarForma(id, meio)
    if (!atual?.cobranca && formaDoMeio(atual?.meio) === FORMA_ONLINE) return reemitir(id)
    avisar('A cobrança já está no EasyStok. Link vencido é reemitido e enviado sozinho.')
    return undefined
  }

  const receber = (id, valor, metodo) => {
    if (!pedidoCriado(id)) return soNoEasyStok('Receber pagamento')(id)
    if (!metodo) { avisar('Selecione como recebeu o pagamento.'); return undefined }
    return umaPorConversa(id, () => registrarPagamentoManual(pedidoCriado(id), valor, metodo).then(
      () => recarregar(id).catch(() => avisar('Pagamento registrado. Atualizando a tela…')),
      (erro) => avisar(`Pagamento não registrado: ${erro.message}`),
    ))
  }
  const desfazer = (id, motivo) => {
    if (!pedidoCriado(id)) return soNoEasyStok('Desfazer pagamento')(id)
    return umaPorConversa(id, () => desfazerPagamentoManual(pedidoCriado(id), motivo).then(
      () => recarregar(id).catch(() => avisar('Pagamento desfeito. Atualizando a tela…')),
      (erro) => avisar(`Pagamento não desfeito: ${erro.message}`),
    ))
  }

  return {
    confirmarPagamento: receber,
    desfazerPagamento: desfazer,
    ...Object.fromEntries(EDITA_COMANDA.map((nome) => [nome, soSemPedidoCriado(nome)])),
    ...Object.fromEntries(Object.entries(SO_NO_EASYSTOK).map(([nome, rotulo]) => [nome, soNoEasyStok(rotulo)])),

    // Janelas com vaga no prazo dos itens da comanda (S16). Promessa direta para a tela.
    carregarJanelasComanda: (itens) => listarJanelas({ itens }),

    gerarPedido: (id, meio = null) => criarPedido(id, meio),

    // Com pedido criado, trocar o meio é a troca de forma da S11: online ↔ na entrega.
    alterarMeioPagamento: (id, meio) => (pedidoCriado(id) ? trocarForma(id, meio) : acoes.alterarMeioPagamento(id, meio)),

    gerarCobranca: (id, pedido, meio = null) => cobrar(id, meio),
    reenviarCobranca: (id, pedido, valorForcado = null, meio = null) => cobrar(id, meio),
  }
}

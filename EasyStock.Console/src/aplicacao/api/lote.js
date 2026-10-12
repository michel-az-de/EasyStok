import * as acao from '../acoes'
import { construirLancamentos, passosNaApi } from '../../dominio/loteDePapel'
import { lancarLotePapel } from '../../infra/api/esteiraApi'
import { obterPedido, pedidoDaApi } from '../../infra/api/comandaApi'

// Lote de papel no modo API (#1241, F11, S46). Quem decide o que lançar continua sendo
// `construirLancamentos` (mesma régua da tela). Cada lançamento vira um passo por etapa no
// vocabulário da API, todos com o horário em que a conexão voltou (o modal não pede a hora
// de cada canhoto). O EasyStok aplica, não manda aviso retroativo ao cliente e diz o que
// recusou; a tela relê o pedido de quem foi lançado e só então fecha a pendência.
//
// Devolve `{ aplicados, rejeitados: [{ numero, motivo }] }`, ou `null` quando nem chegou ao
// EasyStok (a pendência fica aberta para tentar de novo).
export function criarAcoesLoteApi({ despachar, estadoRef }) {
  const avisar = (mensagem) => despachar({ tipo: acao.AVISO_API, mensagem })

  async function lancar(selecoes, agora) {
    const { conversas, conexao, catalogo } = estadoRef.current
    const lancamentos = construirLancamentos(conversas, conexao.pedidosAbertos, selecoes, catalogo?.janelas ?? [], agora)
    const ocorreuEm = new Date(conexao.voltouEm ?? agora).toISOString()

    const rejeitados = []
    const linhas = []
    const origem = []
    for (const lanc of lancamentos) {
      const pedidoId = conversas.find((c) => c.id === lanc.conversaId)?.pedido?.pedidoId
      if (!pedidoId) {
        rejeitados.push({ numero: lanc.numero, motivo: 'o pedido ainda não está no EasyStok' })
        continue
      }
      for (const passo of passosNaApi(lanc.de, lanc.para)) {
        linhas.push({ pedidoId, passo, ocorreuEm })
        origem.push(lanc)
      }
    }

    let resultado = { aplicados: 0, linhas: [] }
    if (linhas.length > 0) {
      try {
        resultado = await lancarLotePapel(linhas)
      } catch (erro) {
        avisar(`Lote de papel: ${erro.message}`)
        return null
      }
    }

    // `linha` é a posição no lote enviado (base 1). Uma recusa por pedido basta para a tela.
    for (const r of resultado?.linhas ?? []) {
      if (r.sucesso) continue
      const lanc = origem[r.linha - 1]
      if (lanc && !rejeitados.some((x) => x.numero === lanc.numero)) {
        rejeitados.push({ numero: lanc.numero, motivo: r.motivo ?? 'recusado pelo EasyStok' })
      }
    }

    const relidos = [...new Set(origem.map((l) => l.conversaId))]
    await Promise.all(relidos.map(async (id) => {
      try {
        const anterior = estadoRef.current.conversas.find((c) => c.id === id)?.pedido ?? null
        const pedido = pedidoDaApi(await obterPedido(id), anterior)
        if (pedido) despachar({ tipo: acao.SINCRONIZAR_PEDIDO, id, pedido })
      } catch { /* a próxima sincronização traz */ }
    }))

    despachar({ tipo: acao.LOTE_PAPEL_LANCADO_API })
    if (rejeitados.length > 0) {
      avisar(`Lote de papel: não lançou ${rejeitados.map((r) => `${r.numero} (${r.motivo})`).join(', ')}.`)
    }
    return { aplicados: resultado?.aplicados ?? 0, rejeitados }
  }

  return { lancarLotePapel: lancar }
}

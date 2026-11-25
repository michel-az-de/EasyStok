// #2478: lista de pedidos de Cadastros. Mesmo ciclo de `useListaCadastros` (espera curta na busca, cancela a
// consulta velha), mas a lista anterior fica na tela enquanto a nova chega: trocar de página não pisca.
import { useEffect, useState } from 'react'
import * as api from '../infra/api/pedidosCadastroApi'
import { criarRegistroDePagamento } from './pagamentoDoPedido'
import { inscreverPedidoRegistrado } from './pedidoRegistrado'
export { listarPedidosDoCliente, obterPedido } from '../infra/api/pedidosCadastroApi'
export { cancelarPedido } from '../infra/api/comandaApi'

export function useListaPedidos(filtros, contexto) {
  const [resultado, definir] = useState({ itens: [], total: 0, carregando: true, erro: null, carregado: false })
  const [revisao, recarregar] = useState(0)
  const [novoId, destacar] = useState(null)
  // #2944 (E1-19): pedido registrado na janela aparece aqui sem recarregar a página, destacado.
  useEffect(() => inscreverPedidoRegistrado((id) => { destacar(id); recarregar((r) => r + 1) }), [])
  const { busca, pagina, situacao, periodo, ordem, entrega } = filtros
  useEffect(() => {
    const abortar = new AbortController()
    definir((r) => ({ ...r, carregando: true, erro: null }))
    const timer = setTimeout(() => api.listarPedidos({ busca, pagina, situacao, periodo, ordem, entrega }, contexto, abortar.signal).then(
      (r) => { if (!abortar.signal.aborted) definir({ ...r, carregando: false, erro: null, carregado: true }) },
      (e) => { if (!abortar.signal.aborted) definir({ itens: [], total: 0, carregando: false, erro: e.message, carregado: true }) },
    ), busca ? 250 : 0)
    return () => { clearTimeout(timer); abortar.abort() }
  }, [busca, pagina, situacao, periodo, ordem, entrega, contexto.empresaId, contexto.identidade, revisao])
  return { ...resultado, novoId, revisao, recarregar: () => recarregar((r) => r + 1) }
}

// #3294: a faixa de indicadores acompanha a lista: mesmos filtros (menos situação e página) e recarrega junto
// (`revisao` é a da lista). Falha só na faixa: a lista segue de pé.
export function useResumoPedidos({ busca, periodo, entrega }, contexto, revisao) {
  const [resultado, definir] = useState({ resumo: null, carregando: true, erro: null })
  useEffect(() => {
    const abortar = new AbortController()
    definir((r) => ({ ...r, carregando: true, erro: null }))
    const timer = setTimeout(() => api.obterResumoDePedidos({ busca, periodo, entrega }, contexto, abortar.signal).then(
      (resumo) => { if (!abortar.signal.aborted) definir({ resumo, carregando: false, erro: null }) },
      (e) => { if (!abortar.signal.aborted) definir({ resumo: null, carregando: false, erro: e.message || 'Os indicadores não carregaram.' }) },
    ), busca ? 250 : 0)
    return () => { clearTimeout(timer); abortar.abort() }
  }, [busca, periodo, entrega, contexto.empresaId, contexto.identidade, revisao])
  return resultado
}

// #3076 (E4, L1): o "Registrar pagamento" do detalhe de Pedidos é a mesma baixa da conversa. O detalhe só tem o
// `PedidoResult` (sem cobrança nem `requerAprovacao`): o botão só liga em pedido operacional, e o registro vale
// para a vida da aba, para o reenvio depois de uma queda reusar a chave.
const registrarNoDetalhe = criarRegistroDePagamento()
export const registrarPagamentoDoDetalhe = (pedido, valor, metodo) =>
  registrarNoDetalhe({ pedidoId: pedido.id, pedido: { pedidoId: pedido.id, statusApi: pedido.status }, valor, metodo })

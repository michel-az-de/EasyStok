// #2478: lista de pedidos de Cadastros. Mesmo ciclo de `useListaCadastros` (espera curta na busca, cancela a
// consulta velha), mas a lista anterior fica na tela enquanto a nova chega: trocar de página não pisca.
import { useEffect, useState } from 'react'
import * as api from '../infra/api/pedidosCadastroApi'
export { obterPedido } from '../infra/api/pedidosCadastroApi'

export function useListaPedidos(filtros, contexto) {
  const [resultado, definir] = useState({ itens: [], total: 0, carregando: true, erro: null, carregado: false })
  const [revisao, recarregar] = useState(0)
  const { busca, pagina, situacao, periodo, ordem } = filtros
  useEffect(() => {
    const abortar = new AbortController()
    definir((r) => ({ ...r, carregando: true, erro: null }))
    const timer = setTimeout(() => api.listarPedidos({ busca, pagina, situacao, periodo, ordem }, contexto, abortar.signal).then(
      (r) => { if (!abortar.signal.aborted) definir({ ...r, carregando: false, erro: null, carregado: true }) },
      (e) => { if (!abortar.signal.aborted) definir({ itens: [], total: 0, carregando: false, erro: e.message, carregado: true }) },
    ), busca ? 250 : 0)
    return () => { clearTimeout(timer); abortar.abort() }
  }, [busca, pagina, situacao, periodo, ordem, contexto.empresaId, contexto.identidade, revisao])
  return { ...resultado, recarregar: () => recarregar((r) => r + 1) }
}

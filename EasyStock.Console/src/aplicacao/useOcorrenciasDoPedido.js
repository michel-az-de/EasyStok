import { useEffect, useState } from 'react'
import { listarOcorrencias } from '../infra/api/ocorrenciasApi'

// #3339 (R1D-01): no modo API a conversa não traz a ocorrência do pedido. Quem decide algo agora (o modal de
// Concluir) lê a lista uma vez ao abrir. `estado` diz em que ponto a leitura está: 'sem-pedido' (nada a ler),
// 'lendo', 'pronta' (a `lista` vale) ou 'falhou' (quem decide trata como incerto, nunca como "sem ocorrência").
export function useOcorrenciasDoPedido(pedidoId) {
  const [lido, setLido] = useState({ pedidoId: null, lista: null, falhou: false })
  useEffect(() => {
    if (!pedidoId) return undefined
    let vivo = true
    listarOcorrencias(pedidoId)
      .then((resposta) => { if (vivo) setLido(Array.isArray(resposta) ? { pedidoId, lista: resposta, falhou: false } : { pedidoId, lista: null, falhou: true }) })
      .catch(() => { if (vivo) setLido({ pedidoId, lista: null, falhou: true }) })
    return () => { vivo = false }
  }, [pedidoId])
  if (!pedidoId) return { lista: null, estado: 'sem-pedido' }
  // O que foi lido de outro pedido não vale para este: já conta como "lendo" no mesmo render em que o pedido troca.
  if (lido.pedidoId !== pedidoId) return { lista: null, estado: 'lendo' }
  return lido.falhou ? { lista: null, estado: 'falhou' } : { lista: lido.lista, estado: 'pronta' }
}

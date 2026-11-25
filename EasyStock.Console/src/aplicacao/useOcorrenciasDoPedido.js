import { useEffect, useState } from 'react'
import { listarOcorrencias } from '../infra/api/ocorrenciasApi'

// #3339 (R1D-01): no modo API a conversa não traz a ocorrência do pedido. Quem decide algo agora (o modal de
// Concluir) lê a lista uma vez ao abrir. Falha ou espera deixam `null`, e o fluxo segue como estava.
export function useOcorrenciasDoPedido(pedidoId) {
  const [lista, setLista] = useState(null)
  useEffect(() => {
    setLista(null)
    if (!pedidoId) return undefined
    let vivo = true
    listarOcorrencias(pedidoId)
      .then((resposta) => { if (vivo) setLista(Array.isArray(resposta) ? resposta : null) })
      .catch(() => {})
    return () => { vivo = false }
  }, [pedidoId])
  return lista
}

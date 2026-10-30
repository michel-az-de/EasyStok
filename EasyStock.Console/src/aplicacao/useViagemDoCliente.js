import { useCallback, useEffect, useState } from 'react'
import { consultarViagemDoCliente } from './viagemDoCliente'
import { viagemDoPedido } from '../dominio/mapaViagem'

export function useViagemDoCliente({ clienteId, pedidoId, ativo }) {
  const chave = `${clienteId}:${pedidoId}`
  const [estado, setEstado] = useState({ chave: null })
  const [tentativa, setTentativa] = useState(0)
  const atual = ativo && estado.chave === chave ? estado : {}
  useEffect(() => {
    if (!ativo || !clienteId || !pedidoId) return undefined
    const controle = new AbortController()
    let vivo = true, pendente = false
    async function carregar() {
      if (pendente || document.hidden) return
      pendente = true
      try {
        const dados = await consultarViagemDoCliente(clienteId, pedidoId, controle.signal)
        const viagem = viagemDoPedido(dados, pedidoId)
        if (vivo) setEstado({ chave, viagem, lida: true })
      } catch (erro) {
        if (vivo && !controle.signal.aborted) setEstado({ chave, erro: erro.message })
      } finally { pendente = false }
    }
    carregar()
    const timer = setInterval(carregar, 15000)
    document.addEventListener('visibilitychange', carregar)
    return () => { vivo = false; controle.abort(); clearInterval(timer); document.removeEventListener('visibilitychange', carregar) }
  }, [ativo, clienteId, pedidoId, chave, tentativa])
  const atualizar = useCallback(() => { setEstado({ chave }); setTentativa((v) => v + 1) }, [chave])
  return { viagem: atual.viagem ?? null, erro: atual.erro ?? null, lida: atual.lida === true,
    carregando: Boolean(ativo && !atual.lida && !atual.erro), atualizar }
}

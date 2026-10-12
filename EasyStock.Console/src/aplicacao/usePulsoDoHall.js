import { useEffect, useState } from 'react'
import { lerCaixaDoHub, lerInboxDoHub, lerPedidosDoHub } from '../infra/api/hubApi'

export function usePulsoDoHall({ ativo, atendimento, pedidos, financeiro }) {
  const [fontes, setFontes] = useState({})
  const [tentativa, setTentativa] = useState(0)
  useEffect(() => {
    if (!ativo) return undefined
    let encerrado = false
    let lendo = false
    let controle
    const leitores = [atendimento && ['atendimento', lerInboxDoHub], pedidos && ['pedidos', lerPedidosDoHub], financeiro && ['financeiro', lerCaixaDoHub]].filter(Boolean)
    const atualizar = async () => {
      if (encerrado || lendo || document.visibilityState === 'hidden') return
      lendo = true
      controle = new AbortController()
      const sinal = AbortSignal.any([controle.signal, AbortSignal.timeout(25000)])
      await Promise.allSettled(leitores.map(async ([id, ler]) => {
        setFontes((anterior) => ({ ...anterior, [id]: { ...anterior[id], estado: 'carregando' } }))
        try {
          const dados = await ler(sinal)
          if (!encerrado) setFontes((anterior) => ({ ...anterior, [id]: { dados, estado: 'ok', em: Date.now() } }))
        } catch (erro) {
          if (!encerrado) setFontes((anterior) => ({ ...anterior, [id]: { ...(erro.status === 401 || erro.status === 403 ? {} : anterior[id]), estado: 'erro', erro: erro.message } }))
        }
      }))
      lendo = false
    }
    atualizar()
    const intervalo = window.setInterval(atualizar, 60000)
    const aoVoltar = () => atualizar()
    window.addEventListener('focus', aoVoltar)
    document.addEventListener('visibilitychange', aoVoltar)
    return () => {
      encerrado = true
      controle?.abort()
      window.clearInterval(intervalo)
      window.removeEventListener('focus', aoVoltar)
      document.removeEventListener('visibilitychange', aoVoltar)
    }
  }, [ativo, atendimento, pedidos, financeiro, tentativa])
  return { fontes, atualizar: () => setTentativa((valor) => valor + 1) }
}

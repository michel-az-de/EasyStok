import { useCallback, useEffect, useRef, useState } from 'react'
import { filaComAviso } from '../dominio/aviso'
import { notificar } from '../infra/notificacaoNavegador'

// #1807: o despachante único de aviso do Balcão, montado uma vez no Provider.
// `useAvisoSonoro` já tocou o som; aqui cada aviso entra na pilha da tela
// (`componentes/Toast.jsx`, some sozinho em ~6 s) e, com a aba escondida e a
// permissão dada, vira notificação do navegador. Nada disto vai para log: o
// aviso leva nome do cliente, que é dado pessoal.
export const DURACAO_DO_AVISO_MS = 6000

const abaEscondida = () => typeof document !== 'undefined' && document.visibilityState === 'hidden'

export function useFilaDeAvisos() {
  const [avisos, setAvisos] = useState([])
  const contador = useRef(0)
  const relogios = useRef(new Map())

  const dispensar = useCallback((id) => {
    clearTimeout(relogios.current.get(id))
    relogios.current.delete(id)
    setAvisos((atual) => atual.filter((a) => a.id !== id))
  }, [])

  const avisar = useCallback((aviso) => {
    contador.current += 1
    const id = `aviso-${contador.current}`
    setAvisos((atual) => filaComAviso(atual, aviso, id))
    relogios.current.set(id, setTimeout(() => dispensar(id), DURACAO_DO_AVISO_MS))
    if (abaEscondida()) notificar(aviso.tipo, aviso.conversaId, aviso.titulo, aviso.corpo)
  }, [dispensar])

  useEffect(() => {
    const ativos = relogios.current
    return () => { for (const relogio of ativos.values()) clearTimeout(relogio) }
  }, [])

  return { avisos, avisar, dispensar }
}

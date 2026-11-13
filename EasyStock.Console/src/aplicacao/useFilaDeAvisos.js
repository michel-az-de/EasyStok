import { useCallback, useEffect, useRef, useState } from 'react'
import { filaComAviso } from '../dominio/aviso'
import { notificar } from '../infra/notificacaoNavegador'
import { criarRelogiosDeAviso } from './relogiosDeAviso'

// #1807: o despachante único de aviso do Balcão, montado uma vez no Provider.
// `useAvisoSonoro` já tocou o som; aqui cada aviso entra na pilha da tela
// (`componentes/Toast.jsx`, some sozinho em ~6 s) e, com a aba escondida e a
// permissão dada, vira notificação do navegador. Nada disto vai para log: o
// aviso leva nome do cliente, que é dado pessoal.
// #2185 (F28): com o ponteiro ou o foco em cima do aviso o relógio para (`pausar` e `retomar`).
export const DURACAO_DO_AVISO_MS = 6000

const abaEscondida = () => typeof document !== 'undefined' && document.visibilityState === 'hidden'

export function useFilaDeAvisos() {
  const [avisos, setAvisos] = useState([])
  const contador = useRef(0)
  const [relogios] = useState(() => criarRelogiosDeAviso({
    duracaoMs: DURACAO_DO_AVISO_MS,
    aoVencer: (id) => setAvisos((atual) => atual.filter((a) => a.id !== id)),
  }))

  const dispensar = useCallback((id) => {
    relogios.cancelar(id)
    setAvisos((atual) => atual.filter((a) => a.id !== id))
  }, [relogios])

  const avisar = useCallback((aviso) => {
    contador.current += 1
    const id = `aviso-${contador.current}`
    setAvisos((atual) => filaComAviso(atual, aviso, id))
    relogios.iniciar(id)
    if (abaEscondida()) notificar(aviso.tipo, aviso.conversaId, aviso.titulo, aviso.corpo)
  }, [relogios])

  useEffect(() => () => relogios.limpar(), [relogios])

  return { avisos, avisar, dispensar, pausar: relogios.pausar, retomar: relogios.retomar }
}

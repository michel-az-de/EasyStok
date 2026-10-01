import { useEffect, useState } from 'react'
import { proximoInstante } from './relogio'

// Relógio da aplicação. O protótipo parte de um instante fixo para a tela abrir
// sempre igual, e avança sozinho para a contagem da janela não congelar.
// `real` (modo API): cada tique e a volta da aba ao primeiro plano releem o
// relógio do sistema (F07, item 1).
export function useRelogio(inicio, passoMs = 30000, { real = false } = {}) {
  const [agora, setAgora] = useState(() => new Date(inicio).getTime())

  useEffect(() => {
    const tique = () => setAgora((t) => proximoInstante(t, passoMs, real, Date.now()))
    const id = setInterval(tique, passoMs)
    const aoVoltar = () => { if (real && document.visibilityState === 'visible') tique() }
    document.addEventListener('visibilitychange', aoVoltar)
    return () => {
      clearInterval(id)
      document.removeEventListener('visibilitychange', aoVoltar)
    }
  }, [passoMs, real])

  return agora
}

import { useEffect, useState } from 'react'

// Relógio da aplicação. O protótipo parte de um instante fixo para a tela abrir
// sempre igual, e avança sozinho para a contagem da janela não congelar.
export function useRelogio(inicio, passoMs = 30000) {
  const [agora, setAgora] = useState(() => new Date(inicio).getTime())

  useEffect(() => {
    const id = setInterval(() => setAgora((t) => t + passoMs), passoMs)
    return () => clearInterval(id)
  }, [passoMs])

  return agora
}

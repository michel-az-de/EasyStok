import { useEffect, useState } from 'react'

// Tique local de 1s por cima do relogio da tela, que anda de 30 em 30s. A
// contagem base vem de `situacaoDaCobranca` (o dominio continua sendo a
// fonte); aqui so interpolamos os segundos entre um tique grande e outro.
// `ancora` muda a cada tique grande OU a cada cobranca nova (reenvio), e e
// nesse instante que o tique local zera.
// #1577: `corre` é falso quando não há contagem na tela (pago, vencido, cancelado,
// em conferência); o intervalo seguia de 1 em 1 s e re-renderizava a Ficha à toa.
// Zerou antes do próximo tique grande, também para.
export function useTiqueLocal(restamMsBase, ancora, corre = true) {
  const [decorrido, setDecorrido] = useState(0)

  useEffect(() => {
    setDecorrido(0)
    if (!corre) return undefined
    let passou = 0
    const id = setInterval(() => {
      passou += 1000
      setDecorrido(passou)
      if (passou >= restamMsBase) clearInterval(id)
    }, 1000)
    return () => clearInterval(id)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ancora, corre])

  return Math.max(restamMsBase - decorrido, 0)
}

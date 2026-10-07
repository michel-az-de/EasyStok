import { useEffect } from 'react'
import { lerLembretesDaApi } from './api/lembretes'

// Leitura periódica do sininho no modo API (#1426). O avaliador do servidor roda uma vez por
// minuto; 15 s basta para o aviso aparecer logo sem pesar como a inbox (5 s).
const INTERVALO_MS = 15000

export function useLembretesApi({ ativo, despachar }) {
  useEffect(() => {
    if (!ativo) return undefined
    let vivo = true
    let proximo = null
    const ciclo = async () => {
      await lerLembretesDaApi((despacho) => { if (vivo) despachar(despacho) })
      if (vivo) proximo = setTimeout(ciclo, INTERVALO_MS)
    }
    ciclo()
    return () => {
      vivo = false
      clearTimeout(proximo)
    }
  }, [ativo, despachar])
}

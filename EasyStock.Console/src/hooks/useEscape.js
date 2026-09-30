import { useEffect } from 'react'

// Fecha camada sobreposta pelo teclado. Usado por modal e popover.
export function useEscape(ativo, aoFechar) {
  useEffect(() => {
    if (!ativo) return undefined
    const aoTeclar = (evento) => { if (evento.key === 'Escape') aoFechar() }
    document.addEventListener('keydown', aoTeclar)
    return () => document.removeEventListener('keydown', aoTeclar)
  }, [ativo, aoFechar])
}

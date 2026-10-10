import { useEffect } from 'react'

// Fecha camada sobreposta pelo teclado. Usado por modal e popover.
export function useEscape(ativo, aoFechar, { capturar = false } = {}) {
  useEffect(() => {
    if (!ativo) return undefined
    const aoTeclar = (evento) => { if (evento.key === 'Escape' && !evento.defaultPrevented) aoFechar(evento) }
    document.addEventListener('keydown', aoTeclar, capturar)
    return () => document.removeEventListener('keydown', aoTeclar, capturar)
  }, [ativo, aoFechar, capturar])
}

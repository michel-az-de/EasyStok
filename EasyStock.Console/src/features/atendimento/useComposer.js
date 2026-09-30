import { useCallback, useState } from 'react'

// Estado do composer. Sem efeito de reinício: quem monta passa `key` com o id da
// conversa, então trocar de conversa remonta e o rascunho nasce vazio.
export function useComposer(aoEnviar) {
  const [rascunho, setRascunho] = useState('')
  const [popover, setPopover] = useState(null)

  const enviar = useCallback(() => {
    const texto = rascunho.trim()
    if (!texto) return
    aoEnviar(texto)
    setRascunho('')
  }, [rascunho, aoEnviar])

  const alternar = useCallback((qual) => {
    setPopover((atual) => (atual === qual ? null : qual))
  }, [])

  return { rascunho, setRascunho, popover, setPopover, alternar, enviar }
}

import { useCallback, useRef, useState } from 'react'

// O Assumir em andamento é de uma conversa só e não aceita o segundo toque (#2185 F22). #3234 (C5): a barra do
// modo muda de lugar (selo no cabeçalho ou faixa abaixo dele) e remonta; a memória fica no painel, que não remonta.
export function useAssumirSemRepetir() {
  const [assumindo, setAssumindo] = useState(() => new Set())
  const emVoo = useRef(new Set())
  const assumir = useCallback(async (id, aoAssumir) => {
    if (emVoo.current.has(id)) return
    emVoo.current.add(id)
    setAssumindo(new Set(emVoo.current))
    try { await aoAssumir() } finally {
      emVoo.current.delete(id)
      setAssumindo(new Set(emVoo.current))
    }
  }, [])
  return { assumindo, assumir }
}

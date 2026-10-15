import { useCallback, useLayoutEffect, useRef } from 'react'

// Função de identidade fixa que chama sempre a versão mais nova de `fn` (#1577). É o que deixa passar um
// handler a componente em `memo` sem furar o memo quando o pai recria a função a cada render.
export function useChamadaEstavel(fn) {
  const ultima = useRef(fn)
  useLayoutEffect(() => { ultima.current = fn })
  return useCallback((...argumentos) => ultima.current?.(...argumentos), [])
}

import { useEffect } from 'react'
import { ligarMenuDetalhes } from './menuDetalhes'

export { fecharMenu } from './menuDetalhes'

// #2230: liga o fechamento leve (toque fora e Escape) ao <details> do `ref`.
export function useMenuDetalhes(ref) {
  useEffect(() => ligarMenuDetalhes(document, () => ref.current), [ref])
}

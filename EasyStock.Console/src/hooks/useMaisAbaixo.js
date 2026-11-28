import { useEffect, useState } from 'react'

// #3386 (r6, H6): marca com `data-mais-abaixo` o contêiner rolável enquanto houver conteúdo abaixo da dobra, para o CSS
// esmaecer a borda inferior (sinal de que dá para rolar). Escreve no DOM direto: rolar não re-renderiza a tela.
export function useMaisAbaixo() {
  const [no, setNo] = useState(null)
  useEffect(() => {
    if (!no) return undefined
    const medir = () => { no.dataset.maisAbaixo = no.scrollHeight - no.scrollTop - no.clientHeight > 2 ? 'true' : 'false' }
    medir()
    no.addEventListener('scroll', medir, { passive: true })
    const tamanho = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(medir)
    tamanho?.observe(no)
    const mudanca = typeof MutationObserver === 'undefined' ? null : new MutationObserver(() => {
      tamanho?.disconnect(); tamanho?.observe(no); [...no.children].forEach((filho) => tamanho?.observe(filho)); medir()
    })
    mudanca?.observe(no, { childList: true })
    ;[...no.children].forEach((filho) => tamanho?.observe(filho))
    return () => { no.removeEventListener('scroll', medir); tamanho?.disconnect(); mudanca?.disconnect() }
  }, [no])
  return setNo
}

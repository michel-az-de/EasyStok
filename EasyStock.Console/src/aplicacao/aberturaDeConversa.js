import { useEffect, useRef } from 'react'

// #2185 (F8, F26): abrir uma conversa por fora da lista (sino de lembretes, aviso na tela, "Não entregues")
// só trocava a seleção. No celular a aba continuava em "Conversas" e no computador o foco ficava no sino ou
// caía no corpo da página. Quem abre a conversa avisa aqui, depois de selecionar; a moldura leva à aba da
// conversa e a lista leva o foco ao campo de mensagem, como já fazia ao abrir pelo clique na lista (#2135).
export const EVENTO_ABRIR_CONVERSA = 'easystok:abrir-conversa'

export function anunciarAberturaDeConversa(id) {
  try { globalThis.window?.dispatchEvent(new CustomEvent(EVENTO_ABRIR_CONVERSA, { detail: { id } })) } catch { /* sem janela */ }
}

export function useAoAbrirConversa(aoAbrir) {
  const atual = useRef(aoAbrir)
  useEffect(() => { atual.current = aoAbrir })
  useEffect(() => {
    const ouvir = (evento) => atual.current?.(evento.detail?.id ?? null)
    window.addEventListener(EVENTO_ABRIR_CONVERSA, ouvir)
    return () => window.removeEventListener(EVENTO_ABRIR_CONVERSA, ouvir)
  }, [])
}

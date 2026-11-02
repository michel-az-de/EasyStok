import { useEffect, useRef } from 'react'

// #2185 (F8, F26): abrir uma conversa por fora da lista (sino de lembretes, aviso na tela, "Não entregues")
// só trocava a seleção. No celular a aba continuava em "Conversas" e no computador o foco ficava no sino ou
// caía no corpo da página. Quem abre a conversa avisa aqui, depois de selecionar; a moldura leva à aba da
// conversa e a lista leva o foco ao campo de mensagem, como já fazia ao abrir pelo clique na lista (#2135).
export const EVENTO_ABRIR_CONVERSA = 'easystok:abrir-conversa'

// O aviso na tela também abre a conversa a partir da Cozinha, de Entregas e do Hall, onde a moldura ainda vai
// montar. Sem ninguém ouvindo, a abertura fica pendente para quem montar; todos da mesma montagem recebem
// (moldura e lista) e ela some em seguida, para quem montar depois não reabrir nada.
export function criarCanalDeAbertura(alvo) {
  let ouvintes = 0
  let pendente = null
  return {
    anunciar(id) {
      if (ouvintes === 0) pendente = { id }
      try { alvo?.dispatchEvent(new CustomEvent(EVENTO_ABRIR_CONVERSA, { detail: { id } })) } catch { /* sem janela */ }
    },
    inscrever(aoAbrir) {
      const ouvir = (evento) => aoAbrir(evento.detail?.id ?? null)
      ouvintes += 1
      alvo?.addEventListener(EVENTO_ABRIR_CONVERSA, ouvir)
      if (pendente) {
        aoAbrir(pendente.id)
        setTimeout(() => { pendente = null })
      }
      return () => {
        ouvintes -= 1
        alvo?.removeEventListener(EVENTO_ABRIR_CONVERSA, ouvir)
      }
    },
  }
}

const canal = criarCanalDeAbertura(globalThis.window)

export const anunciarAberturaDeConversa = (id) => canal.anunciar(id)

export function useAoAbrirConversa(aoAbrir) {
  const atual = useRef(aoAbrir)
  useEffect(() => { atual.current = aoAbrir })
  useEffect(() => canal.inscrever((id) => atual.current?.(id)), [])
}

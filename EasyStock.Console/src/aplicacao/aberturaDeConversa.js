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
    // #2871: `intencao` diz o que fazer na conversa aberta (`{ novoPedido: true }` abre o cardápio da comanda).
    anunciar(id, intencao = {}) {
      const detalhe = { ...intencao, id }
      if (ouvintes === 0) pendente = detalhe
      try { alvo?.dispatchEvent(new CustomEvent(EVENTO_ABRIR_CONVERSA, { detail: detalhe })) } catch { /* sem janela */ }
    },
    inscrever(aoAbrir) {
      const ouvir = (evento) => aoAbrir(evento.detail?.id ?? null, evento.detail ?? {})
      ouvintes += 1
      alvo?.addEventListener(EVENTO_ABRIR_CONVERSA, ouvir)
      if (pendente) {
        aoAbrir(pendente.id, pendente)
        setTimeout(() => { pendente = null })
      }
      return () => {
        ouvintes -= 1
        alvo?.removeEventListener(EVENTO_ABRIR_CONVERSA, ouvir)
      }
    },
  }
}

// #2871 (revisão da #2874): o "Novo pedido" abre a comanda quando a conversa pedida chega à seleção. Se ela não chegar
// logo, a intenção vence, para o cardápio não abrir sozinho quando a operadora entrar nessa conversa mais tarde.
export const VALIDADE_DO_NOVO_PEDIDO_MS = 10000
export function destinoDoNovoPedido(pendente, selecionadaId, agora = Date.now()) {
  if (!pendente) return 'nada'
  if (agora > pendente.ate) return 'descartar'
  return selecionadaId === pendente.id ? 'abrir' : 'esperar'
}

const canal = criarCanalDeAbertura(globalThis.window)

export const anunciarAberturaDeConversa = (id, intencao) => canal.anunciar(id, intencao)

export function useAoAbrirConversa(aoAbrir) {
  const atual = useRef(aoAbrir)
  useEffect(() => { atual.current = aoAbrir })
  useEffect(() => canal.inscrever((id, detalhe) => atual.current?.(id, detalhe)), [])
}

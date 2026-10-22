// #1686 (item 9): o motivo que segura a barra do pedido vira link que leva ao campo. A seção abre
// (modal da Ficha e seção recolhida) e o foco vai ao campo marcado com `data-campo-ficha`. O modal
// monta no próximo quadro: tenta por alguns quadros antes de desistir em silêncio.
const FOCAVEL = ['input', 'select', 'textarea', 'button'].map((tag) => `${tag}:not([disabled])`)

export function campoDaFicha(raiz, alvo) {
  const area = raiz?.querySelector?.(`[data-campo-ficha~="${alvo}"]`)
  if (!area) return null
  return area.querySelector(FOCAVEL.map((s) => `[data-foco-preferido] ${s}`).join(', '))
    ?? (area.matches?.(FOCAVEL.join(', ')) ? area : area.querySelector(FOCAVEL.join(', ')))
}

export function focarCampoDaFicha(alvo, { quadros = 20, raiz = globalThis.document } = {}) {
  const proximo = globalThis.requestAnimationFrame ?? ((fn) => setTimeout(fn, 16))
  const tentar = (resta) => {
    const campo = campoDaFicha(raiz, alvo)
    if (campo) {
      campo.scrollIntoView?.({ block: 'center' })
      campo.focus()
      return
    }
    if (resta > 0) proximo(() => tentar(resta - 1))
  }
  proximo(() => tentar(quadros))
}

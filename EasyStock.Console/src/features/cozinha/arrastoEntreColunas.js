import { pointerWithin, rectIntersection } from '@dnd-kit/core'

// Arrasto entre colunas da Cozinha, comum à demonstração (`ConteudoCozinha.jsx`)
// e ao modo API (`TelaCozinhaApi.jsx`, issue #1446).

// Teclado: setas para a esquerda e para a direita pulam de coluna em coluna
// (o KeyboardSensor padrão anda 25 px por tecla, e cinco colunas de 220 px
// virariam dezenas de toques).
export function coordenadasEntreColunas(evento, { context: { collisionRect, droppableRects, droppableContainers } }) {
  const direita = evento.code === 'ArrowRight'
  if ((!direita && evento.code !== 'ArrowLeft') || !collisionRect) return undefined
  evento.preventDefault()
  const centro = collisionRect.left + collisionRect.width / 2
  const alvos = droppableContainers.getEnabled()
    .map((coluna) => droppableRects.get(coluna.id))
    .filter(Boolean)
    .filter((r) => (direita ? r.left + r.width / 2 > centro + 1 : r.left + r.width / 2 < centro - 1))
  if (alvos.length === 0) return undefined
  const distancia = (r) => Math.abs(r.left + r.width / 2 - centro)
  const alvo = alvos.reduce((a, b) => (distancia(a) <= distancia(b) ? a : b))
  return { x: alvo.left + (alvo.width - collisionRect.width) / 2, y: collisionRect.top }
}

// Ponteiro primeiro (solta onde a mão está); sem ponteiro, que é o teclado,
// a coluna que mais cobre o cartão.
export const colisaoPorColuna = (args) => {
  const soba = pointerWithin(args)
  return soba.length > 0 ? soba : rectIntersection(args)
}

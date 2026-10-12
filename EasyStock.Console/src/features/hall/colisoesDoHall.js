import { closestCenter, pointerWithin } from '@dnd-kit/core'

export function colisoesDoHall(argumentos) {
  const ponto = argumentos.pointerCoordinates
  if (!ponto) return closestCenter(argumentos)
  const retangulos = [...argumentos.droppableRects.values()]
  if (!retangulos.length) return []
  const dentroDaGrade = ponto.x >= Math.min(...retangulos.map((retangulo) => retangulo.left))
    && ponto.x <= Math.max(...retangulos.map((retangulo) => retangulo.right))
    && ponto.y >= Math.min(...retangulos.map((retangulo) => retangulo.top))
    && ponto.y <= Math.max(...retangulos.map((retangulo) => retangulo.bottom))
  if (!dentroDaGrade) return []
  const sobreCartao = pointerWithin(argumentos)
  return sobreCartao.length ? sobreCartao : closestCenter(argumentos)
}

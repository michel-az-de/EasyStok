import { useEffect, useId, useRef } from 'react'
import css from './AlcaLargura.module.css'

const PASSO = 16
const PASSO_GRANDE = 64

const limitar = (valor, min, max) => Math.min(max, Math.max(min, valor))

// Alça vertical entre colunas (seção 9 da direção visual). Arrasto com o
// ponteiro, setas pelo teclado, Home/End nos limites, duplo clique volta ao
// padrão. `invertida` é para a coluna que cresce para a ESQUERDA (Ficha e o
// painel do cardápio, ancorados à direita): arrastar o ponteiro para a
// esquerda aumenta a largura, não diminui.
export function AlcaLargura({
  rotulo, valor, min, max, padrao, invertida = false, aoMudar,
}) {
  const id = useId()
  const limparArrasto = useRef(null)
  useEffect(() => () => limparArrasto.current?.(), [])

  const aoDescerPonteiro = (eventoDescida) => {
    // Só o botão principal. `activationConstraint` de verdade (distância,
    // toque longo) fica com o dnd-kit no arrasto de prato e de item (seção 9,
    // C3); aqui a alça é código próprio, só um pointermove.
    if (eventoDescida.button !== 0) return
    limparArrasto.current?.()
    eventoDescida.preventDefault()
    eventoDescida.currentTarget.focus()
    const inicio = { x: eventoDescida.clientX, valor }
    const ponteiro = eventoDescida.pointerId

    function mover(evento) {
      if (evento.pointerId !== ponteiro) return
      const delta = evento.clientX - inicio.x
      const ajuste = invertida ? -delta : delta
      aoMudar(limitar(inicio.valor + ajuste, min, max))
    }
    function limpar() {
      window.removeEventListener('pointermove', mover)
      window.removeEventListener('pointerup', soltar)
      window.removeEventListener('pointercancel', soltar)
      window.removeEventListener('blur', soltar)
      limparArrasto.current = null
    }
    function soltar(evento) {
      if (evento.pointerId !== undefined && evento.pointerId !== ponteiro) return
      limpar()
    }
    limparArrasto.current = limpar
    window.addEventListener('pointermove', mover)
    window.addEventListener('pointerup', soltar)
    window.addEventListener('pointercancel', soltar)
    window.addEventListener('blur', soltar)
  }

  // Setas mudam 16 px, Shift+setas 64 px, Home e End vão direto aos limites.
  const aoTeclar = (evento) => {
    if (evento.altKey || evento.ctrlKey || evento.metaKey) return
    const sinalDireita = invertida ? -1 : 1
    const passo = evento.shiftKey ? PASSO_GRANDE : PASSO
    if (evento.key === 'ArrowRight') {
      aoMudar(limitar(valor + sinalDireita * passo, min, max))
    } else if (evento.key === 'ArrowLeft') {
      aoMudar(limitar(valor - sinalDireita * passo, min, max))
    } else if (evento.key === 'Home') {
      aoMudar(min)
    } else if (evento.key === 'End') {
      aoMudar(max)
    } else if (evento.key === 'Enter') {
      aoMudar(limitar(padrao, min, max))
    } else {
      return
    }
    evento.preventDefault()
  }

  return (
    <div
      className={css.alca}
      role="separator"
      aria-label={rotulo}
      aria-orientation="vertical"
      aria-valuenow={Math.round(valor)}
      aria-valuemin={min}
      aria-valuemax={max}
      aria-valuetext={`${Math.round(valor)} pixels`}
      aria-describedby={`${id}-instrucoes`}
      tabIndex={0}
      onPointerDown={aoDescerPonteiro}
      onKeyDown={aoTeclar}
      onDoubleClick={() => aoMudar(limitar(padrao, min, max))}
    >
      <span className={css.linha} />
      <span className="sr" id={`${id}-instrucoes`}>Use as setas esquerda e direita para ajustar. Shift ajusta mais. Home e End vão aos limites. Enter restaura a largura padrão.</span>
    </div>
  )
}

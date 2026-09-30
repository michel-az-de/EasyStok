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
  const aoDescerPonteiro = (eventoDescida) => {
    // Só o botão principal. `activationConstraint` de verdade (distância,
    // toque longo) fica com o dnd-kit no arrasto de prato e de item (seção 9,
    // C3); aqui a alça é código próprio, só um pointermove.
    if (eventoDescida.button !== 0) return
    const inicio = { x: eventoDescida.clientX, valor }

    function mover(evento) {
      const delta = evento.clientX - inicio.x
      const ajuste = invertida ? -delta : delta
      aoMudar(limitar(inicio.valor + ajuste, min, max))
    }
    function soltar() {
      window.removeEventListener('pointermove', mover)
      window.removeEventListener('pointerup', soltar)
    }
    window.addEventListener('pointermove', mover)
    window.addEventListener('pointerup', soltar)
  }

  // Setas mudam 16 px, Shift+setas 64 px, Home e End vão direto aos limites.
  const aoTeclar = (evento) => {
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
      tabIndex={0}
      onPointerDown={aoDescerPonteiro}
      onKeyDown={aoTeclar}
      onDoubleClick={() => aoMudar(padrao)}
    >
      <span className={css.linha} />
    </div>
  )
}
